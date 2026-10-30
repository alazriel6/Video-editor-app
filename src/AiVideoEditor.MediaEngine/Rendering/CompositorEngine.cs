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
    private SKBitmap? _offscreenBufferA;
    private SKBitmap? _offscreenBufferB;
    private SKBitmap? _adjustmentBackdropBuffer;
    private SKBitmap? _compoundClipBuffer;

    public bool ShowSafeMargins { get; set; } = false;
    public bool EnableScopeUpdates { get; set; } = false;
    public bool IsExporting
    {
        get => !_frameReader.EnableBackgroundPrefetch;
        set => _frameReader.EnableBackgroundPrefetch = !value;
    }
    public SKRect LastRenderedClipRect { get; private set; }
    public TimelineClip? LastRenderedPrimaryClip { get; private set; }
    public (int[] R, int[] G, int[] B, int[] Luma)? LastScopeHistograms { get; private set; }
    public ScopeRenderData? LastScopeData { get; private set; }

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

        bool anySolo = false;
        var tracks = document.Tracks;
        int trackCount = tracks.Count;
        for (int i = 0; i < trackCount; i++)
        {
            if (tracks[i].IsSolo)
            {
                anySolo = true;
                break;
            }
        }

        bool anyRendered = false;

        // Iterate video tracks in reverse order (top track rendered over bottom track)
        for (int t = trackCount - 1; t >= 0; t--)
        {
            var track = tracks[t];
            if (track.Type != TrackType.Video || !track.IsVisible || track.IsMuted || (anySolo && !track.IsSolo))
                continue;

            var clips = track.Clips;
            int clipCount = clips.Count;

            TimelineClip? activeTransitionBlock = null;
            TimelineClip? clip1 = null;
            TimelineClip? clip2 = null;
            int activeNonTransitionCount = 0;

            for (int i = 0; i < clipCount; i++)
            {
                var clip = clips[i];
                if (currentTimeSeconds >= clip.TimelineStart && currentTimeSeconds < clip.TimelineStart + clip.Duration)
                {
                    if (clip.Kind == ClipKind.Transition)
                    {
                        activeTransitionBlock = clip;
                    }
                    else
                    {
                        if (activeNonTransitionCount == 0)
                        {
                            clip1 = clip;
                            activeNonTransitionCount = 1;
                        }
                        else if (activeNonTransitionCount == 1)
                        {
                            if (clip.TimelineStart < clip1!.TimelineStart)
                            {
                                clip2 = clip1;
                                clip1 = clip;
                            }
                            else
                            {
                                clip2 = clip;
                            }
                            activeNonTransitionCount = 2;
                        }
                        else
                        {
                            activeNonTransitionCount++;
                        }
                    }
                }
            }

            if (activeTransitionBlock != null)
            {
                if (TryRenderStandaloneTransitionBlock(
                    canvas,
                    document,
                    track,
                    activeTransitionBlock,
                    currentTimeSeconds,
                    targetWidth,
                    targetHeight))
                {
                    anyRendered = true;
                    continue;
                }
            }

            if (activeNonTransitionCount == 2 && clip1 != null && clip2 != null)
            {
                var frameA = RenderClipToOffscreenBuffer(document, clip1, currentTimeSeconds, targetWidth, targetHeight, slotIndex: 0);
                var frameB = RenderClipToOffscreenBuffer(document, clip2, currentTimeSeconds, targetWidth, targetHeight, slotIndex: 1);

                if (frameA != null && frameB != null)
                {
                    double overlapStart = clip2.TimelineStart;
                    double overlapEnd = clip1.TimelineStart + clip1.Duration;
                    float rawProgress = (float)Math.Clamp((currentTimeSeconds - overlapStart) / Math.Max(0.05, overlapEnd - overlapStart), 0.0, 1.0);
                    var activeSpec = clip2.InTransition ?? clip1.OutTransition;
                    float progress = activeSpec != null
                        ? TransitionEasingHelper.ApplyEasing(rawProgress, activeSpec.Easing, activeSpec.Speed)
                        : TransitionEasingHelper.ApplyEasing(rawProgress, "EaseInOut", 1.0);
                    string transType = NormalizeTransitionName(activeSpec?.Type ?? "CrossDissolve");
                    string transDir = activeSpec?.Direction ?? "Left";

                    if (!_shaderRenderer.TryRenderDualClipShader(canvas, frameA, frameB, targetWidth, targetHeight, transType, progress, transDir))
                    {
                        var fullRect = new SKRect(0, 0, targetWidth, targetHeight);
                        using var paintA = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
                        using var paintB = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High, Color = new SKColor(255, 255, 255, (byte)Math.Clamp(progress * 255f, 0f, 255f)) };
                        canvas.DrawBitmap(frameA, fullRect, paintA);
                        canvas.DrawBitmap(frameB, fullRect, paintB);
                    }
                    UpdateScopesFromClipFrame(frameB, clip2);
                    LastRenderedPrimaryClip = clip2;
                    anyRendered = true;
                    continue;
                }
            }

            if (activeNonTransitionCount > 0)
            {
                if (activeNonTransitionCount <= 2)
                {
                    if (clip1 != null)
                        RenderClipDirect(canvas, document, clip1, currentTimeSeconds, targetWidth, targetHeight, ref anyRendered, t);
                    if (clip2 != null)
                        RenderClipDirect(canvas, document, clip2, currentTimeSeconds, targetWidth, targetHeight, ref anyRendered, t);
                }
                else
                {
                    for (int i = 0; i < clipCount; i++)
                    {
                        var clip = clips[i];
                        if (clip.Kind != ClipKind.Transition &&
                            currentTimeSeconds >= clip.TimelineStart &&
                            currentTimeSeconds < clip.TimelineStart + clip.Duration)
                        {
                            RenderClipDirect(canvas, document, clip, currentTimeSeconds, targetWidth, targetHeight, ref anyRendered, t);
                        }
                    }
                }
            }
        }

        // Render Subtitle / Text tracks on top
        for (int t = 0; t < trackCount; t++)
        {
            var track = tracks[t];
            if (track.Type != TrackType.Text || !track.IsVisible || track.IsMuted || (anySolo && !track.IsSolo))
                continue;

            var clips = track.Clips;
            int clipCount = clips.Count;
            for (int i = 0; i < clipCount; i++)
            {
                var textClip = clips[i];
                if (textClip.Kind != ClipKind.Transition &&
                    currentTimeSeconds >= textClip.TimelineStart &&
                    currentTimeSeconds < textClip.TimelineStart + textClip.Duration)
                {
                    RenderTextOverlay(canvas, textClip, currentTimeSeconds, targetWidth, targetHeight);
                    anyRendered = true;
                }
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

    private void RenderClipDirect(
        SKCanvas canvas,
        ProjectDocument document,
        TimelineClip clip,
        double currentTimeSeconds,
        int targetWidth,
        int targetHeight,
        ref bool anyRendered,
        int trackIndex = 0)
    {
        if (clip.Kind == ClipKind.AdjustmentLayer)
        {
            RenderAdjustmentLayer(canvas, document, trackIndex, clip, currentTimeSeconds, targetWidth, targetHeight);
            LastRenderedPrimaryClip = clip;
            anyRendered = true;
            return;
        }

        if (clip.Kind == ClipKind.CompoundClip)
        {
            var compoundBmp = RenderCompoundClipToBuffer(document, clip, currentTimeSeconds, targetWidth, targetHeight, out bool ownsCompound);
            try
            {
                if (compoundBmp != null)
                {
                    DrawSingleClipToCanvas(canvas, compoundBmp, clip, currentTimeSeconds, targetWidth, targetHeight);
                    LastRenderedPrimaryClip = clip;
                    anyRendered = true;
                }
            }
            finally
            {
                if (ownsCompound) compoundBmp?.Dispose();
            }
            return;
        }

        if (clip.Kind == ClipKind.Text || (clip.TextData != null && clip.MediaAssetId == Guid.Empty))
        {
            RenderTextOverlay(canvas, clip, currentTimeSeconds, targetWidth, targetHeight);
            LastRenderedPrimaryClip = clip;
            anyRendered = true;
            return;
        }

        var asset = document.FindAsset(clip.MediaAssetId);
        if (asset == null) return;

        double srcTime = ComputeClipSourceTime(clip, currentTimeSeconds, asset);
        var frameBmp = PrepareClipBitmap(asset, clip, srcTime, targetWidth, targetHeight, out bool ownsBmp);
        try
        {
            if (frameBmp != null)
            {
                DrawSingleClipToCanvas(canvas, frameBmp, clip, currentTimeSeconds, targetWidth, targetHeight);
                LastRenderedPrimaryClip = clip;
                anyRendered = true;
            }
        }
        finally
        {
            if (ownsBmp) frameBmp?.Dispose();
        }
    }

    /// <summary>
    /// Captures the composited backdrop of all lower tracks (V1..Vn-1) and applies the Adjustment Layer's
    /// Color Grading, 3D LUT, HSL Secondary, Bezier Mask, and Visual Effects stack.
    /// </summary>
    private void RenderAdjustmentLayer(
        SKCanvas canvas,
        ProjectDocument document,
        int adjTrackIndex,
        TimelineClip adjClip,
        double currentTimeSeconds,
        int targetWidth,
        int targetHeight)
    {
        if (_adjustmentBackdropBuffer == null ||
            _adjustmentBackdropBuffer.Width != targetWidth ||
            _adjustmentBackdropBuffer.Height != targetHeight)
        {
            _adjustmentBackdropBuffer?.Dispose();
            _adjustmentBackdropBuffer = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        }

        using (var backdropCanvas = new SKCanvas(_adjustmentBackdropBuffer))
        {
            backdropCanvas.Clear(new SKColor(17, 18, 21));
            var tracks = document.Tracks;
            bool anySolo = tracks.Any(tr => tr.IsSolo);
            bool dummyRendered = false;

            // Composite all visible tracks below adjTrackIndex (i.e. indices adjTrackIndex + 1 .. trackCount - 1 in reverse order)
            for (int t = tracks.Count - 1; t > adjTrackIndex; t--)
            {
                var lowerTrack = tracks[t];
                if (lowerTrack.Type != TrackType.Video || !lowerTrack.IsVisible || lowerTrack.IsMuted || (anySolo && !lowerTrack.IsSolo))
                    continue;

                for (int i = 0; i < lowerTrack.Clips.Count; i++)
                {
                    var lowerClip = lowerTrack.Clips[i];
                    if (lowerClip.Kind != ClipKind.Transition &&
                        lowerClip.Kind != ClipKind.AdjustmentLayer &&
                        currentTimeSeconds >= lowerClip.TimelineStart &&
                        currentTimeSeconds < lowerClip.TimelineStart + lowerClip.Duration)
                    {
                        RenderClipDirect(backdropCanvas, document, lowerClip, currentTimeSeconds, targetWidth, targetHeight, ref dummyRendered, t);
                    }
                }
            }
        }

        var processedBackdrop = ApplyPixelPipelineToBuffer(_adjustmentBackdropBuffer, adjClip, out bool ownsProcessed);
        try
        {
            DrawSingleClipToCanvas(canvas, processedBackdrop, adjClip, currentTimeSeconds, targetWidth, targetHeight);
        }
        finally
        {
            if (ownsProcessed) processedBackdrop.Dispose();
        }
    }

    /// <summary>
    /// Renders a Compound Clip's nested track sequence into a pooled offscreen buffer, then applies pixel-level LUT/HSL/Matte.
    /// </summary>
    private SKBitmap? RenderCompoundClipToBuffer(
        ProjectDocument parentDocument,
        TimelineClip compoundClip,
        double currentTimeSeconds,
        int targetWidth,
        int targetHeight,
        out bool ownsBitmap)
    {
        ownsBitmap = false;
        double nestedTime = ComputeClipSourceTime(compoundClip, currentTimeSeconds);

        if (compoundClip.NestedTracks == null || compoundClip.NestedTracks.Count == 0)
        {
            var fallbackAsset = parentDocument.FindAsset(compoundClip.MediaAssetId);
            if (fallbackAsset != null)
            {
                return PrepareClipBitmap(fallbackAsset, compoundClip, nestedTime, targetWidth, targetHeight, out ownsBitmap);
            }
            return null;
        }

        if (_compoundClipBuffer == null ||
            _compoundClipBuffer.Width != targetWidth ||
            _compoundClipBuffer.Height != targetHeight)
        {
            _compoundClipBuffer?.Dispose();
            _compoundClipBuffer = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        }

        using (var nestedCanvas = new SKCanvas(_compoundClipBuffer))
        {
            nestedCanvas.Clear(SKColors.Transparent);
            var tempDoc = new ProjectDocument
            {
                Width = targetWidth,
                Height = targetHeight,
                Fps = parentDocument.Fps,
                MediaPool = parentDocument.MediaPool,
                Tracks = compoundClip.NestedTracks
            };

            bool anyChildRendered = false;
            for (int t = tempDoc.Tracks.Count - 1; t >= 0; t--)
            {
                var track = tempDoc.Tracks[t];
                if (!track.IsVisible || track.IsMuted) continue;
                foreach (var child in track.Clips)
                {
                    if (child.Kind != ClipKind.Transition &&
                        nestedTime >= child.TimelineStart &&
                        nestedTime < child.TimelineStart + child.Duration)
                    {
                        RenderClipDirect(nestedCanvas, tempDoc, child, nestedTime, targetWidth, targetHeight, ref anyChildRendered);
                    }
                }
            }
        }

        return ApplyPixelPipelineToBuffer(_compoundClipBuffer, compoundClip, out ownsBitmap);
    }

    /// <summary>
    /// Renders a Premiere Pro-style standalone transition block (ClipKind.Transition) placed anywhere on a Video track:
    /// - Straddling a cut between two clips (clipA -> clipB)
    /// - At the start or end of a single clip (intro / outro transition)
    /// - Free-floating anywhere over a single clip or empty track
    /// </summary>
    private bool TryRenderStandaloneTransitionBlock(
        SKCanvas canvas,
        ProjectDocument document,
        TimelineTrack track,
        TimelineClip transBlock,
        double currentTimeSeconds,
        int targetWidth,
        int targetHeight)
    {
        double transStart = transBlock.TimelineStart;
        double transEnd = transBlock.TimelineStart + transBlock.Duration;
        float rawProgress = (float)Math.Clamp((currentTimeSeconds - transStart) / Math.Max(0.05, transBlock.Duration), 0.0, 1.0);
        float progress = TransitionEasingHelper.ApplyEasing(rawProgress, transBlock.TransitionEasing, transBlock.TransitionSpeed);
        string transType = NormalizeTransitionName(
            !string.IsNullOrWhiteSpace(transBlock.TransitionEffectType)
                ? transBlock.TransitionEffectType
                : "CrossDissolve");
        string transDir = string.IsNullOrWhiteSpace(transBlock.TransitionDirection) ? "Left" : transBlock.TransitionDirection;

        // Find media/text clips on this track that overlap with [transStart - 0.05, transEnd + 0.05]
        TimelineClip? clipA = null;
        TimelineClip? clipB = null;
        int overlapCount = 0;

        var clips = track.Clips;
        int count = clips.Count;
        for (int i = 0; i < count; i++)
        {
            var c = clips[i];
            if (c.Kind == ClipKind.Transition) continue;
            if ((c.TimelineStart + c.Duration) > (transStart - 0.05) && c.TimelineStart < (transEnd + 0.05))
            {
                if (overlapCount == 0)
                {
                    clipA = c;
                    overlapCount = 1;
                }
                else if (overlapCount == 1)
                {
                    if (c.TimelineStart < clipA!.TimelineStart)
                    {
                        clipB = clipA;
                        clipA = c;
                    }
                    else
                    {
                        clipB = c;
                    }
                    overlapCount = 2;
                }
                else
                {
                    overlapCount++;
                }
            }
        }

        if (overlapCount >= 2 && clipA != null && clipB != null)
        {
            // Dual-clip transition across a cut or overlap using pre-allocated offscreen buffers (zero heap allocation)
            var bmpA = RenderClipToOffscreenBuffer(document, clipA, currentTimeSeconds, targetWidth, targetHeight, slotIndex: 0);
            var bmpB = RenderClipToOffscreenBuffer(document, clipB, currentTimeSeconds, targetWidth, targetHeight, slotIndex: 1);

            if (bmpA != null && bmpB != null)
            {
                if (!_shaderRenderer.TryRenderDualClipShader(canvas, bmpA, bmpB, targetWidth, targetHeight, transType, progress, transDir))
                {
                    using var paintA = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
                    using var paintB = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High, Color = new SKColor(255, 255, 255, (byte)(progress * 255f)) };
                    canvas.DrawBitmap(bmpA, new SKRect(0, 0, targetWidth, targetHeight), paintA);
                    canvas.DrawBitmap(bmpB, new SKRect(0, 0, targetWidth, targetHeight), paintB);
                }
                UpdateScopesFromClipFrame(bmpB, clipB);
                LastRenderedPrimaryClip = clipB;
                return true;
            }
        }

        if (overlapCount == 1 && clipA != null)
        {
            var clip = clipA;
            double sampleTime = Math.Clamp(currentTimeSeconds, clip.TimelineStart, clip.TimelineStart + Math.Max(0.01, clip.Duration - 0.01));
            var bmp = RenderClipToOffscreenBuffer(document, clip, sampleTime, targetWidth, targetHeight, slotIndex: 0);
            if (bmp != null)
            {
                double distToStart = Math.Abs(transStart - clip.TimelineStart);
                double distToEnd = Math.Abs(transEnd - (clip.TimelineStart + clip.Duration));
                bool isIntro = distToStart <= 0.25 && distToStart <= distToEnd;
                bool isOutro = distToEnd <= 0.25 && distToEnd < distToStart;

                var fullRect = new SKRect(0, 0, targetWidth, targetHeight);
                using var basePaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };

                float startOp = transBlock.TransitionStartOpacity;
                float endOp = transBlock.TransitionEndOpacity;

                if (isIntro)
                {
                    ApplyClipTransition(canvas, bmp, fullRect, transType, progress, isEntering: true, basePaint, transDir, startOp, endOp);
                }
                else if (isOutro)
                {
                    ApplyClipTransition(canvas, bmp, fullRect, transType, progress, isEntering: false, basePaint, transDir, startOp, endOp);
                }
                else
                {
                    // Mid-clip overlay transition block: pulse out then back in smoothly
                    if (progress < 0.5f)
                        ApplyClipTransition(canvas, bmp, fullRect, transType, progress * 2f, isEntering: false, basePaint, transDir, startOp, endOp);
                    else
                        ApplyClipTransition(canvas, bmp, fullRect, transType, (progress - 0.5f) * 2f, isEntering: true, basePaint, transDir, startOp, endOp);
                }

                UpdateScopesFromClipFrame(bmp, clip);
                LastRenderedPrimaryClip = clip;
                return true;
            }
        }

        return false;
    }

    private SKBitmap EnsureOffscreenBuffer(int slotIndex, int targetWidth, int targetHeight)
    {
        ref SKBitmap? slot = ref (slotIndex == 0 ? ref _offscreenBufferA : ref _offscreenBufferB);
        if (slot == null || slot.Width != targetWidth || slot.Height != targetHeight)
        {
            slot?.Dispose();
            slot = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        }
        return slot;
    }

    private SKBitmap? RenderClipToOffscreenBuffer(
        ProjectDocument document,
        TimelineClip clip,
        double sampleTime,
        int targetWidth,
        int targetHeight,
        int slotIndex)
    {
        var offscreen = EnsureOffscreenBuffer(slotIndex, targetWidth, targetHeight);
        using var offCanvas = new SKCanvas(offscreen);
        offCanvas.Clear(SKColors.Transparent);

        if (clip.Kind == ClipKind.Text || (clip.TextData != null && clip.MediaAssetId == Guid.Empty))
        {
            RenderTextOverlay(offCanvas, clip, sampleTime, targetWidth, targetHeight);
            return offscreen;
        }

        if (clip.Kind == ClipKind.CompoundClip)
        {
            var compoundBmp = RenderCompoundClipToBuffer(document, clip, sampleTime, targetWidth, targetHeight, out bool ownsCompound);
            try
            {
                if (compoundBmp == null) return null;
                double clampedCompoundTime = Math.Clamp(sampleTime, clip.TimelineStart, clip.TimelineStart + Math.Max(0.005, clip.Duration - 0.005));
                DrawSingleClipToCanvas(offCanvas, compoundBmp, clip, clampedCompoundTime, targetWidth, targetHeight);
                return offscreen;
            }
            finally
            {
                if (ownsCompound) compoundBmp?.Dispose();
            }
        }

        var asset = document.FindAsset(clip.MediaAssetId);
        if (asset == null)
        {
            return null;
        }

        double srcTime = ComputeClipSourceTime(clip, sampleTime, asset);
        var rawBmp = PrepareClipBitmap(asset, clip, srcTime, targetWidth, targetHeight, out bool ownsRaw);
        try
        {
            if (rawBmp == null)
                return null;

            double clampedClipTime = Math.Clamp(sampleTime, clip.TimelineStart, clip.TimelineStart + Math.Max(0.005, clip.Duration - 0.005));
            DrawSingleClipToCanvas(offCanvas, rawBmp, clip, clampedClipTime, targetWidth, targetHeight);
            return offscreen;
        }
        finally
        {
            if (ownsRaw) rawBmp?.Dispose();
        }
    }

    /// <summary>
    /// Computes the source media timestamp for a clip at currentTimeSeconds.
    /// Includes a Handle Starvation Guard: if a transition overlaps beyond SourceIn/SourceOut or asset duration,
    /// clamps cleanly to hold the last valid frame instead of returning out-of-bounds or flashing black.
    /// </summary>
    private static double ComputeClipSourceTime(TimelineClip clip, double currentTimeSeconds, MediaAsset? asset = null)
    {
        if (clip.IsFreezeFrame)
            return clip.FreezeSourceTimestamp;

        double signedElapsed = (currentTimeSeconds - clip.TimelineStart) * clip.Speed;
        double rawSourceTime = clip.ReversePlayback
            ? (clip.SourceOut - signedElapsed)
            : (clip.SourceIn + signedElapsed);

        double minBound = 0.0;
        double maxBound = asset != null && asset.Duration > 0.05
            ? Math.Max(0.0, asset.Duration - (1.0 / Math.Max(1.0, asset.Fps)))
            : Math.Max(clip.SourceOut, clip.SourceIn + clip.Duration * clip.Speed);

        return Math.Clamp(rawSourceTime, minBound, maxBound);
    }

    private static string NormalizeTransitionName(string? type) => (type ?? string.Empty).Trim() switch
    {
        "CrossDissolve" or "CrossFade" or "Dissolve" or "Cross Dissolve" or "Film Dissolve" => "CrossDissolve",
        "FadeIn" or "VideoFadeIn" or "Fade In" => "FadeIn",
        "FadeOut" or "VideoFadeOut" or "Fade Out" => "FadeOut",
        "DipToBlack" or "Dip to Black" => "DipToBlack",
        "DipToWhite" or "Dip to White" => "DipToWhite",
        "FlashCut" => "FlashCut",
        "Wipe" or "DirectionalWipe" or "WipeLeft" => "Wipe",
        "Slide" or "Push" or "PushLeft" or "SlideLeft" or "PushRight" or "PushUp" or "PushDown" => "Slide",
        "WhipZoom" or "ZoomIn" or "ZoomBlur" or "Whip Pan" => "WhipZoom",
        "Glitch" or "Digital Glitch" => "Glitch",
        "LumaFade" or "Luma Fade" => "LumaFade",
        _ => string.IsNullOrEmpty(type) ? "CrossDissolve" : type
    };

    private SKBitmap? PrepareClipBitmap(MediaAsset asset, TimelineClip clip, double sourceTime, int width, int height, out bool ownsBitmap)
    {
        ownsBitmap = false;
        var cachedFrame = _frameReader.GetFrame(asset, sourceTime, width, height, clip.Id);
        if (cachedFrame == null) return null;

        return ApplyPixelPipelineToBuffer(cachedFrame, clip, out ownsBitmap);
    }

    private SKBitmap ApplyPixelPipelineToBuffer(SKBitmap sourceFrame, TimelineClip clip, out bool ownsBitmap)
    {
        ownsBitmap = false;
        bool needsMatte = clip.SubjectMatteEnabled && _segmentationService != null;
        bool needsHsl = ColorScienceProcessor.HasActiveHslAdjustments(clip);
        bool needs3DLut = ColorScienceProcessor.HasActive3DLut(clip);

        // Zero-copy fast-path: return the cached frame directly when no destructive pixel mutation is required
        if (!needsMatte && !needsHsl && !needs3DLut)
        {
            return sourceFrame;
        }

        SKBitmap workingCopy;
        if (clip.Enable16BitColorPipeline && (needs3DLut || needsHsl) && !needsMatte)
        {
            workingCopy = ColorScienceProcessor.ConvertToRgbaF16(sourceFrame);
        }
        else
        {
            workingCopy = sourceFrame.Copy();
        }
        ownsBitmap = true;

        if (needsMatte)
        {
            IntPtr ptr = workingCopy.GetPixels();
            int byteCount = workingCopy.RowBytes * workingCopy.Height;
            unsafe
            {
                var span = new Span<byte>((void*)ptr, byteCount);
                _segmentationService!.ApplyBackgroundRemoval(span, workingCopy.Width, workingCopy.Height);
            }

            if (clip.Enable16BitColorPipeline && (needs3DLut || needsHsl))
            {
                var f16Copy = ColorScienceProcessor.ConvertToRgbaF16(workingCopy);
                workingCopy.Dispose();
                workingCopy = f16Copy;
            }
        }

        if (needsHsl)
        {
            ColorScienceProcessor.ApplyHslSecondaryInPlace(workingCopy, clip);
        }

        if (needs3DLut)
        {
            ColorScienceProcessor.ApplyCubeLutInPlace(workingCopy, clip);
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
        float cropLeft = clip.EvaluateCropLeft(clipLocalTime);
        float cropRight = clip.EvaluateCropRight(clipLocalTime);
        float cropTop = clip.EvaluateCropTop(clipLocalTime);
        float cropBottom = clip.EvaluateCropBottom(clipLocalTime);

        // Camera Shake effect
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

        // Universal Crop (0% to 100%) and Corner Radius
        ApplyCropAndCornerClip(canvas, destRect, cropLeft, cropRight, cropTop, cropBottom, clip.CornerRadius * scaleFactor);

        // Polygon / Cubic Bezier Masking (with Feather, MaskOpacity & InvertMask)
        bool hasActiveMask = clip.Mask != null && clip.Mask.IsEnabled && clip.Mask.Points != null && clip.Mask.Points.Count >= 3;
        bool useFeatheredMaskLayer = false;
        SKPath? maskPath = null;

        if (hasActiveMask)
        {
            maskPath = BuildMaskSkPath(clip.Mask!, destRect);
            if (clip.Mask!.MaskFeather <= 0.1f && clip.Mask.MaskOpacity >= 99.9f)
            {
                canvas.ClipPath(
                    maskPath,
                    clip.Mask.InvertMask ? SKClipOperation.Difference : SKClipOperation.Intersect,
                    antialias: true);
            }
            else
            {
                useFeatheredMaskLayer = true;
                canvas.SaveLayer(destRect, null);
            }
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
            ImageFilter = imageFilter,
            BlendMode = ParseBlendMode(clip.BlendMode)
        };

        double elapsed = currentTime - clip.TimelineStart;
        double remaining = (clip.TimelineStart + clip.Duration) - currentTime;

        var inTrans = clip.InTransition;
        if (inTrans == null && clip.FadeInDuration > 0.01)
        {
            inTrans = new TransitionSpec { Type = "FadeIn", Duration = clip.FadeInDuration, StartOpacity = 0f, EndOpacity = 1f };
        }

        var outTrans = clip.OutTransition;
        if (outTrans == null && clip.FadeOutDuration > 0.01)
        {
            outTrans = new TransitionSpec { Type = "FadeOut", Duration = clip.FadeOutDuration, StartOpacity = 1f, EndOpacity = 0f };
        }

        bool hasIn = inTrans != null && inTrans.IsEnabled && inTrans.Type != "None" && inTrans.Duration > 0.01;
        bool hasOut = outTrans != null && outTrans.IsEnabled && outTrans.Type != "None" && outTrans.Duration > 0.01;

        double inStart = hasIn ? Math.Max(0, inTrans!.StartOffset) : 0;
        double inDur = hasIn ? Math.Min(clip.Duration, Math.Max(0.01, inTrans!.Duration)) : 0;
        double inEnd = inStart + inDur;

        double outOffset = hasOut ? Math.Max(0, outTrans!.StartOffset) : 0;
        double outDur = hasOut ? Math.Min(clip.Duration, Math.Max(0.01, outTrans!.Duration)) : 0;
        double outStart = Math.Max(0, clip.Duration - outDur - outOffset);
        double outEnd = clip.Duration - outOffset;

        if (hasIn && elapsed >= inStart && elapsed <= inEnd)
        {
            float linearProgress = (float)Math.Clamp((elapsed - inStart) / inDur, 0.0, 1.0);
            float easedProgress = TransitionEasingHelper.ApplyEasing(linearProgress, inTrans!.Easing, inTrans.Speed);
            ApplyClipTransition(canvas, frame, destRect, inTrans.Type, easedProgress, true, paint, inTrans.Direction, inTrans.StartOpacity, inTrans.EndOpacity);
        }
        else if (hasOut && elapsed >= outStart && elapsed <= outEnd)
        {
            float linearProgress = (float)Math.Clamp((elapsed - outStart) / outDur, 0.0, 1.0);
            float easedProgress = TransitionEasingHelper.ApplyEasing(linearProgress, outTrans!.Easing, outTrans.Speed);
            ApplyClipTransition(canvas, frame, destRect, outTrans.Type, easedProgress, false, paint, outTrans.Direction, outTrans.StartOpacity, outTrans.EndOpacity);
        }
        else if (hasIn && elapsed < inStart)
        {
            // Prior to start of in-transition: clamp to start opacity
            paint.Color = paint.Color.WithAlpha((byte)Math.Clamp(paint.Color.Alpha * inTrans!.StartOpacity, 0f, 255f));
            canvas.DrawBitmap(frame, destRect, paint);
        }
        else if (hasOut && elapsed > outEnd)
        {
            // After end of out-transition: clamp to end opacity
            paint.Color = paint.Color.WithAlpha((byte)Math.Clamp(paint.Color.Alpha * outTrans!.EndOpacity, 0f, 255f));
            canvas.DrawBitmap(frame, destRect, paint);
        }
        else
        {
            canvas.DrawBitmap(frame, destRect, paint);
        }

        // Chromatic Aberration (RGB Split) effect
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

        // Film Grain overlay
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

        // Vignette overlay
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

        if (useFeatheredMaskLayer && maskPath != null && clip.Mask != null)
        {
            ApplyFeatheredMaskLayer(canvas, destRect, maskPath, clip.Mask, scaleFactor);
            canvas.Restore();
        }
        maskPath?.Dispose();

        // Update live RGB Parade, Vectorscope, Luma Waveform, and Histograms
        UpdateScopesFromClipFrame(frame, clip, paint.ColorFilter);

        canvas.Restore();
    }

    private static SKPath BuildMaskSkPath(MaskPath mask, SKRect bounds)
    {
        var path = new SKPath();
        var pts = mask.Points;
        int n = pts.Count;
        if (n == 0) return path;

        float MapX(float normX) => bounds.Left + normX * bounds.Width;
        float MapY(float normY) => bounds.Top + normY * bounds.Height;

        path.MoveTo(MapX(pts[0].X), MapY(pts[0].Y));

        for (int i = 0; i < n; i++)
        {
            int nextIdx = (i + 1) % n;
            if (!mask.IsClosed && i == n - 1)
                break;

            var curr = pts[i];
            var next = pts[nextIdx];

            bool hasTangents = MathF.Abs(curr.OutTangentX) > 0.0001f ||
                               MathF.Abs(curr.OutTangentY) > 0.0001f ||
                               MathF.Abs(next.InTangentX) > 0.0001f ||
                               MathF.Abs(next.InTangentY) > 0.0001f;

            if (hasTangents)
            {
                float c1x = MapX(curr.X + curr.OutTangentX);
                float c1y = MapY(curr.Y + curr.OutTangentY);
                float c2x = MapX(next.X + next.InTangentX);
                float c2y = MapY(next.Y + next.InTangentY);
                path.CubicTo(c1x, c1y, c2x, c2y, MapX(next.X), MapY(next.Y));
            }
            else
            {
                path.LineTo(MapX(next.X), MapY(next.Y));
            }
        }

        if (mask.IsClosed)
            path.Close();

        return path;
    }

    private static void ApplyFeatheredMaskLayer(
        SKCanvas canvas,
        SKRect bounds,
        SKPath maskPath,
        MaskPath mask,
        float scaleFactor)
    {
        byte maskAlpha = (byte)Math.Clamp(MathF.Round((mask.MaskOpacity / 100f) * 255f), 0f, 255f);
        float sigma = Math.Max(0f, mask.MaskFeather * 0.5f * scaleFactor);

        using var dstInLayerPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
        canvas.SaveLayer(bounds, dstInLayerPaint);

        using var blurFilter = sigma > 0.1f ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null;

        if (!mask.InvertMask)
        {
            canvas.Clear(SKColors.Transparent);
            using var pathPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(255, 255, 255, maskAlpha),
                MaskFilter = blurFilter
            };
            canvas.DrawPath(maskPath, pathPaint);
        }
        else
        {
            canvas.Clear(new SKColor(255, 255, 255, maskAlpha));
            using var punchPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White,
                BlendMode = SKBlendMode.DstOut,
                MaskFilter = blurFilter
            };
            canvas.DrawPath(maskPath, punchPaint);
        }

        canvas.Restore();
    }

    private void UpdateScopesFromClipFrame(SKBitmap frame, TimelineClip clip, SKColorFilter? existingFilter = null)
    {
        if (!EnableScopeUpdates) return;
        var filter = existingFilter ?? BuildColorGradingFilter(clip);
        var scopeData = ColorScienceProcessor.ComputeScopeData(frame, filter);
        LastScopeData = scopeData;
        LastScopeHistograms = (scopeData.HistR, scopeData.HistG, scopeData.HistB, scopeData.HistLuma);
    }

    private static void ApplyCropAndCornerClip(
        SKCanvas canvas,
        SKRect bounds,
        float cropLeftPct,
        float cropRightPct,
        float cropTopPct,
        float cropBottomPct,
        float cornerRadiusPx)
    {
        bool hasCrop = cropLeftPct > 0.01f || cropRightPct > 0.01f || cropTopPct > 0.01f || cropBottomPct > 0.01f;
        bool hasCorner = cornerRadiusPx > 0.5f;
        if (!hasCrop && !hasCorner)
            return;

        float l = bounds.Left + bounds.Width * Math.Clamp(cropLeftPct / 100f, 0f, 1f);
        float r = bounds.Right - bounds.Width * Math.Clamp(cropRightPct / 100f, 0f, 1f);
        float t = bounds.Top + bounds.Height * Math.Clamp(cropTopPct / 100f, 0f, 1f);
        float b = bounds.Bottom - bounds.Height * Math.Clamp(cropBottomPct / 100f, 0f, 1f);

        if (r < l) r = l;
        if (b < t) b = t;

        var cropRect = new SKRect(l, t, r, b);
        if (hasCorner)
        {
            using var rrect = new SKRoundRect(cropRect, cornerRadiusPx, cornerRadiusPx);
            canvas.ClipRoundRect(rrect, SKClipOperation.Intersect, true);
        }
        else
        {
            canvas.ClipRect(cropRect, SKClipOperation.Intersect, true);
        }
    }

    private static SKBlendMode ParseBlendMode(string? blendMode) => blendMode switch
    {
        "Multiply" => SKBlendMode.Multiply,
        "Screen" => SKBlendMode.Screen,
        "Overlay" => SKBlendMode.Overlay,
        "SoftLight" => SKBlendMode.SoftLight,
        "HardLight" => SKBlendMode.HardLight,
        "Add" => SKBlendMode.Plus,
        _ => SKBlendMode.SrcOver
    };

    private void ApplyClipTransition(
        SKCanvas canvas,
        SKBitmap frame,
        SKRect destRect,
        string transitionType,
        float progress,
        bool isEntering,
        SKPaint paint,
        string direction = "Left",
        float startOpacity = 0.0f,
        float endOpacity = 1.0f)
    {
        _shaderRenderer.ApplySingleClipTransition(
            canvas,
            frame,
            destRect,
            NormalizeTransitionName(transitionType),
            progress,
            isEntering,
            paint,
            direction,
            startOpacity,
            endOpacity);
    }

    /// <summary>
    /// Exact Rec.709 Normalized Color Science Pipeline.
    /// </summary>
    public static SKColorFilter? BuildColorGradingFilter(TimelineClip clip)
    {
        return ColorScienceProcessor.BuildFromClip(clip);
    }

    /// <summary>
    /// Renders a Text/Subtitle clip with:
    /// 1) 100% transparent background by default (only draws background pill when BackgroundBoxEnabled == true)
    /// 2) Character-by-character LetterSpacing (-20px to +100px) and LineHeight (0.5x to 3.0x)
    /// 3) Universal Transform (PosX, PosY, Scale, Rotation, FlipH, FlipV) &amp; Universal Crop (CropLeft/Right/Top/Bottom 0-100%)
    /// </summary>
    private void RenderTextOverlay(
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
        float cropLeft = clip.EvaluateCropLeft(localTime);
        float cropRight = clip.EvaluateCropRight(localTime);
        float cropTop = clip.EvaluateCropTop(localTime);
        float cropBottom = clip.EvaluateCropBottom(localTime);

        string displayText = spec.Text;

        // Text Animations
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
        float fontSize = Math.Max(10f, spec.FontSize * scaleFactor);
        float letterSpacingPx = Math.Clamp(spec.LetterSpacing, -20f, 100f) * scaleFactor;
        float lineHeightMult = Math.Clamp(spec.LineHeight, 0.5f, 3.0f);

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
            TextAlign = SKTextAlign.Left,
            Color = textColor.WithAlpha((byte)Math.Clamp(evalOpacity * 255f, 0f, 255f)),
            IsAntialias = true
        };

        var localTextBounds = TextRenderer.MeasureTextBlockBounds(
            displayText,
            fillPaint,
            letterSpacingPx,
            lineHeightMult,
            align,
            out float maxLineWidth,
            out float lineStep,
            out string[] lines);

        float padX = spec.BackgroundPaddingX * scaleFactor;
        float padY = spec.BackgroundPaddingY * scaleFactor;
        var boxRect = new SKRect(
            localTextBounds.Left - padX,
            localTextBounds.Top - padY * 0.5f,
            localTextBounds.Right + padX,
            localTextBounds.Bottom + padY);

        // Compute world bounds for Gizmo selection and Universal Crop
        float scaledW = boxRect.Width * evalScale;
        float scaledH = boxRect.Height * evalScale;
        LastRenderedClipRect = new SKRect(
            x + boxRect.Left * evalScale,
            y + boxRect.Top * evalScale,
            x + boxRect.Left * evalScale + scaledW,
            y + boxRect.Top * evalScale + scaledH);

        canvas.Save();
        canvas.Translate(x, y);
        if (Math.Abs(evalRot) > 0.01f)
            canvas.RotateDegrees(evalRot);

        float flipX = clip.FlipHorizontal ? -1f : 1f;
        float flipY = clip.FlipVertical ? -1f : 1f;
        canvas.Scale(evalScale * flipX, evalScale * flipY);

        // Apply Universal Crop (CropLeft, CropRight, CropTop, CropBottom 0-100%) to the Text Clip's bounding box!
        ApplyCropAndCornerClip(canvas, boxRect, cropLeft, cropRight, cropTop, cropBottom, clip.CornerRadius * scaleFactor);

        // 1. Optional Background Box (ONLY when BackgroundBoxEnabled is explicitly checked!)
        if (spec.BackgroundBoxEnabled)
        {
            var bgBase = SKColor.TryParse(spec.BackgroundColorHex, out var bc) ? bc : new SKColor(17, 18, 21);
            byte bgAlpha = (byte)Math.Clamp((spec.BackgroundOpacity / 100f) * evalOpacity * 255f, 0f, 255f);
            using var boxPaint = new SKPaint
            {
                Color = bgBase.WithAlpha(bgAlpha),
                IsAntialias = true,
                BlendMode = ParseBlendMode(clip.BlendMode)
            };

            float radius = spec.BackgroundCornerRadius * scaleFactor;
            canvas.DrawRoundRect(boxRect, radius, radius, boxPaint);
        }

        // 2. Render each line with Drop Shadow, Stroke, and Fill using real-time LetterSpacing & LineHeight
        SKPaint? shadowPaint = null;
        if (spec.ShadowEnabled && spec.ShadowBlur > 0.1f)
        {
            var shadowCol = SKColor.TryParse(spec.ShadowColorHex, out var sc) ? sc : new SKColor(0, 0, 0, 180);
            shadowPaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = fontSize,
                TextAlign = SKTextAlign.Left,
                Color = shadowCol.WithAlpha((byte)Math.Clamp(shadowCol.Alpha * evalOpacity, 0f, 255f)),
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(spec.ShadowBlur * 0.5f, spec.ShadowBlur * 0.5f)
            };
        }

        SKPaint? strokePaint = null;
        if (spec.StrokeEnabled && spec.StrokeWidth > 0.1f)
        {
            strokePaint = new SKPaint
            {
                Typeface = typeface,
                TextSize = fontSize,
                TextAlign = SKTextAlign.Left,
                Color = outlineColor.WithAlpha((byte)Math.Clamp(evalOpacity * 255f, 0f, 255f)),
                IsStroke = true,
                StrokeWidth = Math.Max(1.5f, spec.StrokeWidth * scaleFactor * 2f),
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };
        }

        try
        {
            if (shadowPaint != null)
            {
                TextRenderer.DrawTextWithSpacing(
                    canvas,
                    displayText,
                    spec.ShadowOffsetX * scaleFactor,
                    spec.ShadowOffsetY * scaleFactor,
                    shadowPaint,
                    null,
                    letterSpacingPx,
                    lineHeightMult,
                    align);
            }

            TextRenderer.DrawTextWithSpacing(
                canvas,
                displayText,
                0f,
                0f,
                fillPaint,
                strokePaint,
                letterSpacingPx,
                lineHeightMult,
                align);
        }
        finally
        {
            shadowPaint?.Dispose();
            strokePaint?.Dispose();
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
        _offscreenBufferA?.Dispose();
        _offscreenBufferB?.Dispose();
        _adjustmentBackdropBuffer?.Dispose();
        _compoundClipBuffer?.Dispose();
        _offscreenBufferA = null;
        _offscreenBufferB = null;
        _adjustmentBackdropBuffer = null;
        _compoundClipBuffer = null;
        _frameReader.Dispose();
    }
}
