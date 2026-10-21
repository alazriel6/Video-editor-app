using AiVideoEditor.Core.Interfaces;
using AiVideoEditor.Core.Models;
using AiVideoEditor.MediaEngine.Decoding;
using AiVideoEditor.MediaEngine.Rendering.Shaders;
using AiVideoEditor.MediaEngine.Shaders;
using SkiaSharp;

namespace AiVideoEditor.MediaEngine.Rendering;

public sealed class CompositorEngine : IDisposable
{
    private readonly VideoFrameReader _frameReader;
    private readonly ShaderTransitionRenderer _shaderRenderer;
    private IVideoSegmentationService? _segmentationService;

    public bool ShowSafeMargins { get; set; } = false;
    public SKRect LastRenderedClipRect { get; private set; }
    public TimelineClip? LastRenderedPrimaryClip { get; private set; }
    public (int[] R, int[] G, int[] B, int[] Luma)? LastScopeHistograms { get; private set; }

    public CompositorEngine(VideoFrameReader frameReader, IVideoSegmentationService? segmentationService = null)
    {
        _frameReader = frameReader;
        _shaderRenderer = new ShaderTransitionRenderer();
        _segmentationService = segmentationService;
    }

    public void SetSegmentationService(IVideoSegmentationService segmentationService)
    {
        _segmentationService = segmentationService;
    }

    public void RenderSourceAssetFrame(
        SKCanvas canvas,
        MediaAsset? asset,
        double sourceTimeSeconds,
        int targetWidth,
        int targetHeight)
    {
        canvas.Clear(new SKColor(17, 18, 21));
        if (asset == null)
        {
            DrawMonitorPlaceholder(canvas, "No Media Selected", "Double-click a file in Media Pool or click Import Media", targetWidth, targetHeight);
            return;
        }

        var frame = _frameReader.GetFrame(asset, sourceTimeSeconds, targetWidth, targetHeight);
        if (frame == null)
        {
            DrawMonitorPlaceholder(canvas, asset.Name, $"Audio Clip ({asset.Duration:0.0}s)", targetWidth, targetHeight);
            return;
        }

        float scaleX = (float)targetWidth / Math.Max(1, frame.Width);
        float scaleY = (float)targetHeight / Math.Max(1, frame.Height);
        float fitScale = Math.Min(scaleX, scaleY);
        float drawW = frame.Width * fitScale;
        float drawH = frame.Height * fitScale;
        var destRect = new SKRect(
            (targetWidth - drawW) * 0.5f,
            (targetHeight - drawH) * 0.5f,
            (targetWidth + drawW) * 0.5f,
            (targetHeight + drawH) * 0.5f);

        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
        canvas.DrawBitmap(frame, destRect, paint);

        // Source In / Out Range Bar at bottom of Source Monitor
        float barY = targetHeight - 14f;
        float barLeft = 24f;
        float barWidth = targetWidth - 48f;
        double dur = Math.Max(0.1, asset.Duration);
        float inX = barLeft + (float)Math.Clamp(asset.MarkIn / dur, 0.0, 1.0) * barWidth;
        float outX = barLeft + (float)Math.Clamp(asset.MarkOut / dur, 0.0, 1.0) * barWidth;
        float curX = barLeft + (float)Math.Clamp(sourceTimeSeconds / dur, 0.0, 1.0) * barWidth;

        using var trackBg = new SKPaint { Color = new SKColor(34, 36, 44, 220) };
        using var rangeFill = new SKPaint { Color = new SKColor(59, 130, 246, 180) };
        using var markerPaint = new SKPaint { Color = new SKColor(243, 244, 246), StrokeWidth = 2f };
        using var playheadPaint = new SKPaint { Color = new SKColor(239, 68, 68), StrokeWidth = 2f };

        canvas.DrawRect(barLeft, barY, barWidth, 5f, trackBg);
        if (outX > inX)
            canvas.DrawRect(inX, barY, outX - inX, 5f, rangeFill);

        canvas.DrawLine(inX, barY - 4f, inX, barY + 9f, markerPaint);
        canvas.DrawLine(outX, barY - 4f, outX, barY + 9f, markerPaint);
        canvas.DrawLine(curX, barY - 6f, curX, barY + 11f, playheadPaint);

        if (ShowSafeMargins)
            DrawBroadcastSafeMargins(canvas, targetWidth, targetHeight);
    }

    public void RenderCompositeFrame(
        SKCanvas canvas,
        ProjectDocument document,
        double currentTimeSeconds,
        int targetWidth,
        int targetHeight)
    {
        canvas.Clear(new SKColor(17, 18, 21));
        LastRenderedPrimaryClip = null;

        bool anySolo = document.Tracks.Any(t => t.IsSolo);

        var videoTracks = document.Tracks
            .Where(t => t.Type == TrackType.Video && t.IsVisible && !t.IsMuted && (!anySolo || t.IsSolo))
            .Reverse()
            .ToList();

        bool anyRendered = false;

        foreach (var track in videoTracks)
        {
            var activeClips = track.Clips
                .Where(c => currentTimeSeconds >= c.TimelineStart && currentTimeSeconds < c.TimelineStart + c.Duration)
                .OrderBy(c => c.TimelineStart)
                .ToList();

            if (activeClips.Count == 2)
            {
                var clipA = activeClips[0];
                var clipB = activeClips[1];
                var assetA = document.MediaPool.FirstOrDefault(m => m.Id == clipA.MediaAssetId);
                var assetB = document.MediaPool.FirstOrDefault(m => m.Id == clipB.MediaAssetId);

                if (assetA != null && assetB != null)
                {
                    double srcTimeA = ComputeClipSourceTime(clipA, currentTimeSeconds);
                    double srcTimeB = ComputeClipSourceTime(clipB, currentTimeSeconds);

                    using var frameA = PrepareClipBitmap(assetA, clipA, srcTimeA, targetWidth, targetHeight);
                    using var frameB = PrepareClipBitmap(assetB, clipB, srcTimeB, targetWidth, targetHeight);

                    if (frameA != null && frameB != null)
                    {
                        double overlapStart = clipB.TimelineStart;
                        double overlapEnd = clipA.TimelineStart + clipA.Duration;
                        float progress = (float)Math.Clamp((currentTimeSeconds - overlapStart) / Math.Max(0.05, overlapEnd - overlapStart), 0.0, 1.0);
                        string transType = NormalizeTransitionName(clipB.InTransition?.Type ?? clipA.OutTransition?.Type ?? "CrossDissolve");

                        if (!_shaderRenderer.TryRenderDualClipShader(canvas, frameA, frameB, targetWidth, targetHeight, transType, progress))
                        {
                            DrawSingleClipToCanvas(canvas, frameA, clipA, currentTimeSeconds, targetWidth, targetHeight);
                            DrawSingleClipToCanvas(canvas, frameB, clipB, currentTimeSeconds, targetWidth, targetHeight);
                        }
                        anyRendered = true;
                        continue;
                    }
                }
            }

            foreach (var clip in activeClips)
            {
                if (clip.TextData != null && clip.MediaAssetId == Guid.Empty)
                {
                    RenderTextOverlay(canvas, clip, currentTimeSeconds, targetWidth, targetHeight);
                    LastRenderedPrimaryClip = clip;
                    anyRendered = true;
                    continue;
                }

                var asset = document.MediaPool.FirstOrDefault(m => m.Id == clip.MediaAssetId);
                if (asset == null) continue;

                double srcTime = ComputeClipSourceTime(clip, currentTimeSeconds);
                using var frameBmp = PrepareClipBitmap(asset, clip, srcTime, targetWidth, targetHeight);
                if (frameBmp != null)
                {
                    DrawSingleClipToCanvas(canvas, frameBmp, clip, currentTimeSeconds, targetWidth, targetHeight);
                    LastRenderedPrimaryClip = clip;
                    anyRendered = true;
                }
            }
        }

        // Render Subtitle / Text tracks on top
        var textTracks = document.Tracks
            .Where(t => t.Type == TrackType.Text && t.IsVisible && !t.IsMuted && (!anySolo || t.IsSolo))
            .ToList();

        foreach (var track in textTracks)
        {
            var activeTextClips = track.Clips
                .Where(c => currentTimeSeconds >= c.TimelineStart && currentTimeSeconds < c.TimelineStart + c.Duration)
                .ToList();

            foreach (var textClip in activeTextClips)
            {
                RenderTextOverlay(canvas, textClip, currentTimeSeconds, targetWidth, targetHeight);
                anyRendered = true;
            }
        }

        if (!anyRendered)
        {
            DrawMonitorPlaceholder(
                canvas,
                "Program Monitor",
                "Import a video file (Ctrl+I) or drag media onto Video 1",
                targetWidth,
                targetHeight);
        }

        if (ShowSafeMargins)
        {
            DrawBroadcastSafeMargins(canvas, targetWidth, targetHeight);
        }
    }

    private static double ComputeClipSourceTime(TimelineClip clip, double currentTimeSeconds)
    {
        if (clip.IsFreezeFrame)
            return clip.FreezeSourceTimestamp;

        double elapsed = Math.Max(0, (currentTimeSeconds - clip.TimelineStart) * clip.Speed);
        if (clip.ReversePlayback)
            return Math.Max(clip.SourceIn, clip.SourceOut - elapsed);
        return clip.SourceIn + elapsed;
    }

    private static string NormalizeTransitionName(string type) => type switch
    {
        "CrossDissolve" => "CrossFade",
        "DipToBlack" or "DipToWhite" or "FlashCut" => type,
        "WhipZoom" => "ZoomIn",
        "SlideLeft" or "Push" => "WipeLeft",
        _ => type
    };

    private SKBitmap? PrepareClipBitmap(MediaAsset asset, TimelineClip clip, double sourceTime, int width, int height)
    {
        var cachedFrame = _frameReader.GetFrame(asset, sourceTime, width, height);
        if (cachedFrame == null) return null;

        var workingCopy = cachedFrame.Copy();

        if (clip.SubjectMatteEnabled && _segmentationService != null)
        {
            IntPtr ptr = workingCopy.GetPixels();
            int byteCount = workingCopy.RowBytes * workingCopy.Height;
            unsafe
            {
                var span = new Span<byte>((void*)ptr, byteCount);
                _segmentationService.ApplyBackgroundRemoval(span, workingCopy.Width, workingCopy.Height);
            }
        }

        return workingCopy;
    }

    private void DrawSingleClipToCanvas(
        SKCanvas canvas,
        SKBitmap frame,
        TimelineClip clip,
        double currentTime,
        int canvasWidth,
        int canvasHeight)
    {
        canvas.Save();

        double clipLocalTime = Math.Max(0, currentTime - clip.TimelineStart);

        // Evaluate Keyframe Animation Tracks at clipLocalTime
        float evalScale = clip.EvaluateScale(clipLocalTime);
        float evalPosX = clip.EvaluatePosX(clipLocalTime);
        float evalPosY = clip.EvaluatePosY(clipLocalTime);
        float evalRot = clip.EvaluateRotation(clipLocalTime);
        float evalOpacity = clip.EvaluateOpacity(clipLocalTime);

        // Camera Shake effect (Section 5.2)
        float shakeAmt = clip.CameraShakeAmount;
        if (clip.ColorFilterPreset == "CameraShake" && shakeAmt < 10f) shakeAmt = 35f;
        if (shakeAmt > 0.5f)
        {
            float amp = shakeAmt * 0.28f;
            evalPosX += (float)(Math.Sin(currentTime * 38.0) * amp + Math.Cos(currentTime * 71.0) * (amp * 0.4f));
            evalPosY += (float)(Math.Cos(currentTime * 43.0) * amp * 0.8f);
            evalRot += (float)(Math.Sin(currentTime * 29.0) * (shakeAmt * 0.03f));
        }

        float scaleFactor = canvasWidth / 960f;
        float centerX = canvasWidth * 0.5f + evalPosX * scaleFactor;
        float centerY = canvasHeight * 0.5f + evalPosY * scaleFactor;

        canvas.Translate(centerX, centerY);
        if (Math.Abs(evalRot) > 0.01f)
            canvas.RotateDegrees(evalRot);

        float flipX = clip.FlipHorizontal ? -1f : 1f;
        float flipY = clip.FlipVertical ? -1f : 1f;
        canvas.Scale(evalScale * flipX, evalScale * flipY);
        canvas.Translate(-canvasWidth * 0.5f, -canvasHeight * 0.5f);

        float scaleX = (float)canvasWidth / Math.Max(1, frame.Width);
        float scaleY = (float)canvasHeight / Math.Max(1, frame.Height);
        float fitScale = Math.Min(scaleX, scaleY);
        float drawW = frame.Width * fitScale;
        float drawH = frame.Height * fitScale;
        var destRect = new SKRect(
            (canvasWidth - drawW) * 0.5f,
            (canvasHeight - drawH) * 0.5f,
            (canvasWidth + drawW) * 0.5f,
            (canvasHeight + drawH) * 0.5f);

        LastRenderedClipRect = destRect;

        if (clip.CropLeft > 0 || clip.CropRight > 0 || clip.CropTop > 0 || clip.CropBottom > 0)
        {
            var cropRect = new SKRect(
                destRect.Left + destRect.Width * Math.Clamp(clip.CropLeft / 100f, 0f, 0.45f),
                destRect.Top + destRect.Height * Math.Clamp(clip.CropTop / 100f, 0f, 0.45f),
                destRect.Right - destRect.Width * Math.Clamp(clip.CropRight / 100f, 0f, 0.45f),
                destRect.Bottom - destRect.Height * Math.Clamp(clip.CropBottom / 100f, 0f, 0.45f));
            canvas.ClipRect(cropRect);
        }

        SKImageFilter? imageFilter = null;
        if (clip.GaussianBlurSigma > 0.2f)
        {
            imageFilter = SKImageFilter.CreateBlur(clip.GaussianBlurSigma, clip.GaussianBlurSigma);
        }
        else if (clip.DropShadowEnabled)
        {
            imageFilter = SKImageFilter.CreateDropShadow(0, 6, 12, 12, new SKColor(0, 0, 0, 180));
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Medium,
            Color = new SKColor(255, 255, 255, (byte)Math.Clamp(evalOpacity * 255f, 0f, 255f)),
            ColorFilter = BuildColorGradingFilter(clip),
            ImageFilter = imageFilter
        };

        double elapsed = currentTime - clip.TimelineStart;
        double remaining = (clip.TimelineStart + clip.Duration) - currentTime;

        if (clip.InTransition != null && clip.InTransition.Duration > 0.01 && elapsed <= clip.InTransition.Duration)
        {
            float progress = (float)(elapsed / clip.InTransition.Duration);
            ApplyClipTransition(canvas, frame, destRect, clip.InTransition.Type, progress, true, paint);
        }
        else if (clip.OutTransition != null && clip.OutTransition.Duration > 0.01 && remaining <= clip.OutTransition.Duration)
        {
            float progress = (float)(1.0 - (remaining / clip.OutTransition.Duration));
            ApplyClipTransition(canvas, frame, destRect, clip.OutTransition.Type, progress, false, paint);
        }
        else
        {
            canvas.DrawBitmap(frame, destRect, paint);
        }

        // Chromatic Aberration (RGB Split) effect (Section 5.2)
        float rgbSplit = clip.ChromaticAberrationAmount;
        if (clip.ColorFilterPreset == "ChromaticAberration" && rgbSplit < 5f) rgbSplit = 35f;
        if (rgbSplit > 0.5f)
        {
            float offsetPx = (rgbSplit / 100f) * 14f * scaleFactor;
            using var redPaint = new SKPaint
            {
                Color = new SKColor(255, 40, 40, 85),
                BlendMode = SKBlendMode.Screen
            };
            var shiftedRect = new SKRect(destRect.Left - offsetPx, destRect.Top, destRect.Right - offsetPx, destRect.Bottom);
            canvas.DrawBitmap(frame, shiftedRect, redPaint);
        }

        // Film Grain overlay (Section 5.2)
        if (clip.FilmGrainAmount > 1f)
        {
            byte grainAlpha = (byte)Math.Clamp(clip.FilmGrainAmount * 0.9f, 4f, 90f);
            using var grainPaint = new SKPaint
            {
                Color = new SKColor(245, 235, 215, grainAlpha),
                BlendMode = SKBlendMode.Overlay
            };
            int seed = (int)(currentTime * 100);
            for (int g = 0; g < 45; g++)
            {
                float gx = destRect.Left + ((seed * 31 + g * 97) % (int)Math.Max(10, destRect.Width));
                float gy = destRect.Top + ((seed * 17 + g * 53) % (int)Math.Max(10, destRect.Height));
                canvas.DrawRect(gx, gy, 2f, 2f, grainPaint);
            }
        }

        // Vignette overlay (Section 5.2 & 6)
        float vigNorm = clip.Vignette > 1.05f ? clip.Vignette / 100f : clip.Vignette;
        if (vigNorm > 0.01f)
        {
            byte vigAlpha = (byte)Math.Clamp(vigNorm * 225f, 0f, 240f);
            using var vigPaint = new SKPaint
            {
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(destRect.MidX, destRect.MidY),
                    Math.Max(destRect.Width, destRect.Height) * 0.62f,
                    new[] { SKColors.Transparent, new SKColor(0, 0, 0, vigAlpha) },
                    new[] { 0.42f, 1.0f },
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(destRect, vigPaint);
        }

        // Update live RGB/Luma scope histograms from graded frame (< 0.35ms)
        LastScopeHistograms = ColorScienceProcessor.SampleScopeHistograms(frame, paint.ColorFilter);

        canvas.Restore();
    }

    private void ApplyClipTransition(
        SKCanvas canvas,
        SKBitmap frame,
        SKRect destRect,
        string transitionType,
        float progress,
        bool isEntering,
        SKPaint paint)
    {
        float t = isEntering ? progress : (1f - progress);
        if (transitionType == "DipToBlack")
        {
            canvas.DrawBitmap(frame, destRect, paint);
            byte blackAlpha = (byte)Math.Clamp((1f - t) * 255f, 0f, 255f);
            using var dipPaint = new SKPaint { Color = new SKColor(0, 0, 0, blackAlpha) };
            canvas.DrawRect(destRect, dipPaint);
        }
        else if (transitionType == "DipToWhite" || transitionType == "FlashCut")
        {
            canvas.DrawBitmap(frame, destRect, paint);
            byte whiteAlpha = (byte)Math.Clamp((1f - t) * 255f, 0f, 255f);
            using var flashPaint = new SKPaint { Color = new SKColor(255, 255, 255, whiteAlpha) };
            canvas.DrawRect(destRect, flashPaint);
        }
        else if (transitionType == "SlideLeft")
        {
            canvas.Save();
            float dx = (1f - t) * destRect.Width * (isEntering ? 1f : -1f);
            canvas.Translate(dx, 0);
            canvas.DrawBitmap(frame, destRect, paint);
            canvas.Restore();
        }
        else
        {
            _shaderRenderer.ApplySingleClipTransition(canvas, frame, destRect, NormalizeTransitionName(transitionType), progress, isEntering, paint);
        }
    }

    /// <summary>
    /// Exact Rec.709 Normalized Color Science Pipeline (Section 3.2).
    /// </summary>
    public static SKColorFilter? BuildColorGradingFilter(TimelineClip clip)
    {
        return ColorScienceProcessor.BuildFromClip(clip);
    }

    private static void RenderTextOverlay(
        SKCanvas canvas,
        TimelineClip clip,
        double currentTime,
        int canvasWidth,
        int canvasHeight)
    {
        var spec = clip.TextData ?? new TextOverlaySpec { Text = clip.Label };
        if (string.IsNullOrWhiteSpace(spec.Text)) return;

        double localTime = Math.Max(0, currentTime - clip.TimelineStart);
        float evalScale = clip.EvaluateScale(localTime);
        float evalPosX = clip.EvaluatePosX(localTime);
        float evalPosY = clip.EvaluatePosY(localTime);
        float evalRot = clip.EvaluateRotation(localTime);
        float evalOpacity = clip.EvaluateOpacity(localTime);

        string displayText = spec.Text;

        // Text Animations (Section 7.2)
        if (spec.TextAnimation == "Typewriter")
        {
            double animDur = Math.Min(1.5, Math.Max(0.3, clip.Duration * 0.6));
            double ratio = Math.Clamp(localTime / animDur, 0.0, 1.0);
            int charCount = Math.Max(1, (int)Math.Ceiling(displayText.Length * ratio));
            displayText = displayText[..Math.Min(displayText.Length, charCount)];
        }
        else if (spec.TextAnimation == "Pop / Bounce In" && localTime < 0.35)
        {
            float p = (float)(localTime / 0.35);
            evalScale *= 0.4f + 0.6f * (1f + MathF.Sin(p * MathF.PI) * 0.25f);
        }
        else if (spec.TextAnimation == "Fade Up By Word" && localTime < 0.45)
        {
            float p = (float)(localTime / 0.45);
            evalOpacity *= p;
            evalPosY += (1f - p) * 35f;
        }
        else if (spec.TextAnimation == "Slide In" && localTime < 0.4)
        {
            float p = (float)(localTime / 0.4);
            evalPosX -= (1f - p) * 180f;
        }

        float scaleFactor = canvasHeight / 1080f;
        float fontSize = Math.Max(14f, spec.FontSize * scaleFactor * evalScale);

        var weight = spec.FontWeightStyle switch
        {
            "Black" => SKFontStyleWeight.Black,
            "Regular" => SKFontStyleWeight.Normal,
            _ => SKFontStyleWeight.Bold
        };
        var slant = (spec.Italic || spec.FontWeightStyle == "Italic") ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var typeface = SKTypeface.FromFamilyName(spec.FontFamily, weight, SKFontStyleWidth.Normal, slant);

        float x = canvasWidth * 0.5f + evalPosX * (canvasWidth / 960f);
        float y = canvasHeight * 0.85f + (evalPosY + spec.YOffset) * (canvasHeight / 540f);

        canvas.Save();
        canvas.Translate(x, y);
        if (Math.Abs(evalRot) > 0.01f)
            canvas.RotateDegrees(evalRot);

        var align = spec.Alignment switch
        {
            "Left" => SKTextAlign.Left,
            "Right" => SKTextAlign.Right,
            _ => SKTextAlign.Center
        };

        var textColor = SKColor.TryParse(spec.TextColorHex, out var tc) ? tc : SKColors.White;
        var outlineColor = SKColor.TryParse(spec.OutlineColorHex, out var oc) ? oc : SKColors.Black;

        using var fillPaint = new SKPaint
        {
            Typeface = typeface,
            TextSize = fontSize,
            TextAlign = align,
            Color = textColor.WithAlpha((byte)Math.Clamp(evalOpacity * 255f, 0f, 255f)),
            IsAntialias = true
        };

        string[] lines = displayText.Replace("\r\n", "\n").Split('\n');
        float maxLineWidth = 0f;
        foreach (var line in lines)
        {
            float w = fillPaint.MeasureText(line);
            if (w > maxLineWidth) maxLineWidth = w;
        }

        float lineStep = fontSize * Math.Clamp(spec.LineHeight, 0.8f, 2.5f);
        float totalTextH = lines.Length * lineStep;

        // 1. Background Box (Caption Pill)
        if (spec.BackgroundBoxEnabled || spec.StylePreset is "BroadcastBox" or "BoxedSubtitle")
        {
            var bgBase = SKColor.TryParse(spec.BackgroundColorHex, out var bc) ? bc : new SKColor(17, 18, 21);
            byte bgAlpha = (byte)Math.Clamp((spec.BackgroundOpacity / 100f) * evalOpacity * 255f, 0f, 255f);
            using var boxPaint = new SKPaint { Color = bgBase.WithAlpha(bgAlpha), IsAntialias = true };

            float padX = spec.BackgroundPaddingX * scaleFactor;
            float padY = spec.BackgroundPaddingY * scaleFactor;
            float radius = spec.BackgroundCornerRadius * scaleFactor;

            float left = align == SKTextAlign.Center ? -maxLineWidth * 0.5f - padX
                : align == SKTextAlign.Right ? -maxLineWidth - padX : -padX;
            float right = left + maxLineWidth + padX * 2f;
            var boxRect = new SKRect(left, -fontSize - padY * 0.5f, right, (lines.Length - 1) * lineStep + padY);
            canvas.DrawRoundRect(boxRect, radius, radius, boxPaint);
        }

        // 2. Render each line with Drop Shadow, Stroke, and Fill
        for (int i = 0; i < lines.Length; i++)
        {
            float ly = i * lineStep;
            string lineText = lines[i];

            if (spec.ShadowEnabled && spec.ShadowBlur > 0.1f)
            {
                var shadowCol = SKColor.TryParse(spec.ShadowColorHex, out var sc) ? sc : new SKColor(0, 0, 0, 180);
                using var shadowPaint = new SKPaint
                {
                    Typeface = typeface,
                    TextSize = fontSize,
                    TextAlign = align,
                    Color = shadowCol,
                    IsAntialias = true,
                    ImageFilter = SKImageFilter.CreateBlur(spec.ShadowBlur * 0.5f, spec.ShadowBlur * 0.5f)
                };
                canvas.DrawText(lineText, spec.ShadowOffsetX * scaleFactor, ly + spec.ShadowOffsetY * scaleFactor, shadowPaint);
            }

            if (spec.StrokeEnabled && spec.StrokeWidth > 0.1f)
            {
                using var strokePaint = new SKPaint
                {
                    Typeface = typeface,
                    TextSize = fontSize,
                    TextAlign = align,
                    Color = outlineColor.WithAlpha((byte)Math.Clamp(evalOpacity * 255f, 0f, 255f)),
                    IsStroke = true,
                    StrokeWidth = Math.Max(1.5f, spec.StrokeWidth * scaleFactor * 2f),
                    StrokeJoin = SKStrokeJoin.Round,
                    IsAntialias = true
                };
                canvas.DrawText(lineText, 0, ly, strokePaint);
            }

            canvas.DrawText(lineText, 0, ly, fillPaint);
        }

        canvas.Restore();
    }

    private static void DrawBroadcastSafeMargins(SKCanvas canvas, int width, int height)
    {
        using var guidePaint = new SKPaint
        {
            Color = new SKColor(156, 163, 175, 100),
            IsStroke = true,
            StrokeWidth = 1f
        };

        var actionSafe = new SKRect(width * 0.05f, height * 0.05f, width * 0.95f, height * 0.95f);
        var titleSafe = new SKRect(width * 0.10f, height * 0.10f, width * 0.90f, height * 0.90f);
        canvas.DrawRect(actionSafe, guidePaint);
        canvas.DrawRect(titleSafe, guidePaint);

        float cx = width * 0.5f;
        float cy = height * 0.5f;
        canvas.DrawLine(cx - 12, cy, cx + 12, cy, guidePaint);
        canvas.DrawLine(cx, cy - 12, cx, cy + 12, guidePaint);
    }

    private static void DrawMonitorPlaceholder(
        SKCanvas canvas,
        string title,
        string subtitle,
        int width,
        int height)
    {
        canvas.Clear(new SKColor(17, 18, 21));
        using var borderPaint = new SKPaint { Color = new SKColor(45, 48, 59), IsStroke = true, StrokeWidth = 1f };
        canvas.DrawRect(1, 1, width - 2, height - 2, borderPaint);

        using var titlePaint = new SKPaint
        {
            Color = new SKColor(243, 244, 246),
            TextSize = Math.Max(14f, height * 0.032f),
            TextAlign = SKTextAlign.Center,
            IsAntialias = true
        };
        using var subPaint = new SKPaint
        {
            Color = new SKColor(156, 163, 175),
            TextSize = Math.Max(11f, height * 0.022f),
            TextAlign = SKTextAlign.Center,
            IsAntialias = true
        };

        canvas.DrawText(title, width * 0.5f, height * 0.47f, titlePaint);
        canvas.DrawText(subtitle, width * 0.5f, height * 0.54f, subPaint);
    }

    public void Dispose()
    {
        _frameReader.Dispose();
    }
}
