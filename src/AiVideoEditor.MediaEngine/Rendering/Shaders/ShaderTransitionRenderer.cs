using System;
using System.Collections.Generic;
using SkiaSharp;
using AiVideoEditor.Core.Models;

namespace AiVideoEditor.MediaEngine.Rendering.Shaders;

public sealed class ShaderTransitionRenderer
{
    private readonly Dictionary<string, SKRuntimeEffect?> _compiledEffects = new();

    private const string GlitchSksl = @"
uniform shader uTextureA;
uniform shader uTextureB;
uniform float uProgress;
uniform float2 uResolution;
half4 main(float2 coord) {
    float intensity = sin(uProgress * 3.1415926) * 28.0;
    float slice = floor(coord.y / 24.0);
    float shift = sin(slice * 12.9898 + uProgress * 45.0) * intensity;
    float2 sc = float2(coord.x + shift, coord.y);
    half4 aR = uTextureA.eval(sc + float2(intensity * 0.4, 0.0));
    half4 aG = uTextureA.eval(sc);
    half4 aB = uTextureA.eval(sc - float2(intensity * 0.4, 0.0));
    half4 bR = uTextureB.eval(sc + float2(intensity * 0.4, 0.0));
    half4 bG = uTextureB.eval(sc);
    half4 bB = uTextureB.eval(sc - float2(intensity * 0.4, 0.0));
    return mix(half4(aR.r, aG.g, aB.b, aG.a), half4(bR.r, bG.g, bB.b, bG.a), half(uProgress));
}";

    private const string LumaFadeSksl = @"
uniform shader uTextureA;
uniform shader uTextureB;
uniform float uProgress;
half4 main(float2 coord) {
    half4 a = uTextureA.eval(coord);
    half4 b = uTextureB.eval(coord);
    float lumaA = dot(a.rgb, half3(0.2126, 0.7152, 0.0722));
    float mask = smoothstep(uProgress * 1.3 - 0.3, uProgress * 1.3, lumaA);
    return mix(b, a, half(mask));
}";

    public ShaderTransitionRenderer()
    {
        TryCompile("Glitch", GlitchSksl);
        TryCompile("LumaFade", LumaFadeSksl);
    }

    private void TryCompile(string key, string sksl)
    {
        try
        {
            var effect = SKRuntimeEffect.Create(sksl, out _);
            _compiledEffects[key] = effect;
        }
        catch
        {
            _compiledEffects[key] = null;
        }
    }

    /// <summary>
    /// Smooth Cubic Hermite (SmoothStep) S-curve interpolation: 3t^2 - 2t^3.
    /// Ensures zero velocity at transition start (t=0) and end (t=1).
    /// </summary>
    private static float SmoothCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public void ApplySingleClipTransition(
        SKCanvas canvas,
        SKBitmap frame,
        SKRect destRect,
        string transitionType,
        float progress,
        bool isIncoming,
        SKPaint basePaint,
        string direction = "Left",
        float startOpacity = 0.0f,
        float endOpacity = 1.0f)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        float eased = SmoothCubic(progress);
        float t = isIncoming ? eased : (1f - eased);

        canvas.Save();

        string normType = NormalizeName(transitionType);

        switch (normType)
        {
            case "FadeIn":
            {
                float op = startOpacity + (endOpacity - startOpacity) * eased;
                basePaint.Color = basePaint.Color.WithAlpha((byte)Math.Clamp(MathF.Round(basePaint.Color.Alpha * op), 0f, 255f));
                basePaint.FilterQuality = SKFilterQuality.High;
                canvas.DrawBitmap(frame, destRect, basePaint);
                break;
            }
            case "FadeOut":
            {
                float op = endOpacity + (startOpacity - endOpacity) * (1f - eased);
                basePaint.Color = basePaint.Color.WithAlpha((byte)Math.Clamp(MathF.Round(basePaint.Color.Alpha * op), 0f, 255f));
                basePaint.FilterQuality = SKFilterQuality.High;
                canvas.DrawBitmap(frame, destRect, basePaint);
                break;
            }
            case "DipToBlack":
            {
                basePaint.FilterQuality = SKFilterQuality.High;
                canvas.DrawBitmap(frame, destRect, basePaint);
                byte blackAlpha = (byte)Math.Clamp(MathF.Round((1f - t) * 255f), 0f, 255f);
                using var dipPaint = new SKPaint { Color = new SKColor(0, 0, 0, blackAlpha), IsAntialias = true };
                canvas.DrawRect(destRect, dipPaint);
                break;
            }
            case "DipToWhite":
            case "FlashCut":
            {
                basePaint.FilterQuality = SKFilterQuality.High;
                canvas.DrawBitmap(frame, destRect, basePaint);
                byte whiteAlpha = (byte)Math.Clamp(MathF.Round((1f - t) * 255f), 0f, 255f);
                using var flashPaint = new SKPaint { Color = new SKColor(255, 255, 255, whiteAlpha), IsAntialias = true };
                canvas.DrawRect(destRect, flashPaint);
                break;
            }
            case "ZoomIn":
            case "ZoomBlur":
            case "WhipZoom":
            {
                float scale = 0.72f + 0.28f * t;
                canvas.Translate(destRect.MidX, destRect.MidY);
                canvas.Scale(scale, scale);
                canvas.Translate(-destRect.MidX, -destRect.MidY);
                basePaint.Color = basePaint.Color.WithAlpha((byte)Math.Clamp(MathF.Round(basePaint.Color.Alpha * t), 0f, 255f));
                basePaint.FilterQuality = SKFilterQuality.High;
                canvas.DrawBitmap(frame, destRect, basePaint);
                break;
            }
            case "Slide":
            case "Push":
            case "PushLeft":
            case "SlideLeft":
            case "PushRight":
            case "PushUp":
            case "PushDown":
            {
                string dir = ResolveDirection(normType, direction);
                float dx = 0, dy = 0;
                switch (dir)
                {
                    case "Right":
                        dx = (1f - t) * destRect.Width * (isIncoming ? -1f : 1f);
                        break;
                    case "Up":
                        dy = (1f - t) * destRect.Height * (isIncoming ? 1f : -1f);
                        break;
                    case "Down":
                        dy = (1f - t) * destRect.Height * (isIncoming ? -1f : 1f);
                        break;
                    default: // Left
                        dx = (1f - t) * destRect.Width * (isIncoming ? 1f : -1f);
                        break;
                }
                canvas.Translate(dx, dy);
                basePaint.FilterQuality = SKFilterQuality.High;
                canvas.DrawBitmap(frame, destRect, basePaint);
                break;
            }
            case "Wipe":
            case "DirectionalWipe":
            case "WipeLeft":
            {
                string dir = ResolveDirection(normType, direction);
                RenderFeatheredSingleWipe(canvas, frame, destRect, t, dir, basePaint);
                break;
            }
            default: // CrossDissolve / Dissolve
            {
                basePaint.Color = basePaint.Color.WithAlpha((byte)Math.Clamp(MathF.Round(basePaint.Color.Alpha * t), 0f, 255f));
                basePaint.FilterQuality = SKFilterQuality.High;
                canvas.DrawBitmap(frame, destRect, basePaint);
                break;
            }
        }

        canvas.Restore();
    }

    private static void RenderFeatheredSingleWipe(
        SKCanvas canvas,
        SKBitmap frame,
        SKRect destRect,
        float t,
        string dir,
        SKPaint basePaint)
    {
        canvas.SaveLayer(destRect, null);
        basePaint.FilterQuality = SKFilterQuality.High;
        canvas.DrawBitmap(frame, destRect, basePaint);

        var (startPt, endPt) = GetWipeGradientPoints(destRect.Width, destRect.Height, dir, destRect.Left, destRect.Top);
        float feather = 0.06f;
        float p = t * (1f + feather * 2f) - feather;
        float stop0 = Math.Clamp(p - feather, 0f, 1f);
        float stop1 = Math.Clamp(p + feather, 0f, 1f);

        using var maskShader = SKShader.CreateLinearGradient(
            startPt,
            endPt,
            new[] { SKColors.White, SKColors.White, SKColors.Transparent, SKColors.Transparent },
            new[] { 0f, stop0, stop1, 1f },
            SKShaderTileMode.Clamp);
        using var maskPaint = new SKPaint
        {
            Shader = maskShader,
            BlendMode = SKBlendMode.DstIn,
            IsAntialias = true
        };
        canvas.DrawRect(destRect, maskPaint);
        canvas.Restore();
    }

    public bool TryRenderDualClipShader(
        SKCanvas canvas,
        SKBitmap frameA,
        SKBitmap frameB,
        int width,
        int height,
        string transitionType,
        float progress,
        string direction = "Left")
    {
        progress = Math.Clamp(progress, 0f, 1f);
        float eased = SmoothCubic(progress);
        string normType = NormalizeName(transitionType);
        var fullRect = new SKRect(0, 0, width, height);

        // 1. Cross Dissolve / Film Dissolve (High-Speed Native Alpha Fast-Path)
        if (normType is "CrossDissolve" or "FadeIn" or "FadeOut")
        {
            using var paintA = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };
            canvas.DrawBitmap(frameA, fullRect, paintA);

            byte alphaB = (byte)Math.Clamp(MathF.Round(eased * 255f), 0f, 255f);
            using var paintB = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High,
                Color = new SKColor(255, 255, 255, alphaB),
                BlendMode = SKBlendMode.SrcOver
            };
            canvas.DrawBitmap(frameB, fullRect, paintB);
            return true;
        }

        // 2. Dip to Black / Dip to White / Flash Cut (Two-Stage Eased Luminance Fast-Path)
        if (normType is "DipToBlack" or "DipToWhite" or "FlashCut")
        {
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            if (progress < 0.5f)
            {
                canvas.DrawBitmap(frameA, fullRect, paint);
                float stageT = SmoothCubic(progress * 2f);
                byte alpha = (byte)Math.Clamp(MathF.Round(stageT * 255f), 0f, 255f);
                var col = normType == "DipToBlack" ? new SKColor(0, 0, 0, alpha) : new SKColor(255, 255, 255, alpha);
                using var overlay = new SKPaint { Color = col, IsAntialias = true };
                canvas.DrawRect(fullRect, overlay);
            }
            else
            {
                canvas.DrawBitmap(frameB, fullRect, paint);
                float stageT = SmoothCubic((1f - progress) * 2f);
                byte alpha = (byte)Math.Clamp(MathF.Round(stageT * 255f), 0f, 255f);
                var col = normType == "DipToBlack" ? new SKColor(0, 0, 0, alpha) : new SKColor(255, 255, 255, alpha);
                using var overlay = new SKPaint { Color = col, IsAntialias = true };
                canvas.DrawRect(fullRect, overlay);
            }
            return true;
        }

        // 3. Slide / Push (Left, Right, Up, Down) with Motion Blur & Seam Shadow
        if (normType is "Slide" or "Push" or "PushLeft" or "SlideLeft" or "PushRight" or "PushUp" or "PushDown")
        {
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            string dir = ResolveDirection(normType, direction);

            // Subtle directional motion blur velocity peaks at t = 0.5
            float velocity = MathF.Sin(progress * MathF.PI);
            float blurX = dir is "Left" or "Right" ? velocity * 6.0f : 0f;
            float blurY = dir is "Up" or "Down" ? velocity * 6.0f : 0f;
            using var blurFilter = (blurX > 0.4f || blurY > 0.4f)
                ? SKImageFilter.CreateBlur(blurX, blurY, SKShaderTileMode.Clamp)
                : null;
            paint.ImageFilter = blurFilter;

            canvas.Save();
            switch (dir)
            {
                case "Right":
                {
                    float dx = eased * width;
                    canvas.DrawBitmap(frameA, new SKRect(dx, 0, dx + width, height), paint);
                    canvas.DrawBitmap(frameB, new SKRect(dx - width, 0, dx, height), paint);
                    DrawSeamShadow(canvas, dx, 0, dx, height, isVertical: true);
                    break;
                }
                case "Up":
                {
                    float dy = -eased * height;
                    canvas.DrawBitmap(frameA, new SKRect(0, dy, width, dy + height), paint);
                    canvas.DrawBitmap(frameB, new SKRect(0, dy + height, width, dy + height * 2), paint);
                    DrawSeamShadow(canvas, 0, dy + height, width, dy + height, isVertical: false);
                    break;
                }
                case "Down":
                {
                    float dy = eased * height;
                    canvas.DrawBitmap(frameA, new SKRect(0, dy, width, dy + height), paint);
                    canvas.DrawBitmap(frameB, new SKRect(0, dy - height, width, dy), paint);
                    DrawSeamShadow(canvas, 0, dy, width, dy, isVertical: false);
                    break;
                }
                default: // Left
                {
                    float dx = -eased * width;
                    canvas.DrawBitmap(frameA, new SKRect(dx, 0, dx + width, height), paint);
                    canvas.DrawBitmap(frameB, new SKRect(dx + width, 0, dx + width * 2, height), paint);
                    DrawSeamShadow(canvas, dx + width, 0, dx + width, height, isVertical: true);
                    break;
                }
            }
            canvas.Restore();
            return true;
        }

        // 4. Directional Wipe (Left, Right, Up, Down, Diagonal) with Soft Feathered Gradient Border
        if (normType is "Wipe" or "DirectionalWipe" or "WipeLeft")
        {
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };

            // Draw outgoing frameA underneath
            canvas.DrawBitmap(frameA, fullRect, paint);

            // Draw incoming frameB masked by a soft feathered linear gradient
            string dir = ResolveDirection(normType, direction);
            var (startPt, endPt) = GetWipeGradientPoints(width, height, dir, 0f, 0f);

            float feather = 0.055f;
            float p = eased * (1f + feather * 2f) - feather;
            float stop0 = Math.Clamp(p - feather, 0f, 1f);
            float stop1 = Math.Clamp(p + feather, 0f, 1f);

            canvas.SaveLayer(fullRect, null);
            canvas.DrawBitmap(frameB, fullRect, paint);

            using var maskShader = SKShader.CreateLinearGradient(
                startPt,
                endPt,
                new[] { SKColors.White, SKColors.White, SKColors.Transparent, SKColors.Transparent },
                new[] { 0f, stop0, stop1, 1f },
                SKShaderTileMode.Clamp);
            using var maskPaint = new SKPaint
            {
                Shader = maskShader,
                BlendMode = SKBlendMode.DstIn,
                IsAntialias = true
            };
            canvas.DrawRect(fullRect, maskPaint);
            canvas.Restore();
            return true;
        }

        // 5. Whip Pan / Smooth Zoom Transition with Mirror Tile Mode & Multi-Sample Radial Blur
        if (normType is "WhipZoom")
        {
            RenderSmoothZoomTransition(canvas, frameA, frameB, width, height, eased);
            return true;
        }

        // 6. Artistic SKSL Runtime Shaders (Glitch, LumaFade)
        if (!_compiledEffects.TryGetValue(normType, out var effect) || effect == null)
        {
            // Fallback to high-quality eased cross dissolve
            using var pA = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            using var pB = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High,
                Color = new SKColor(255, 255, 255, (byte)Math.Clamp(MathF.Round(eased * 255f), 0f, 255f))
            };
            canvas.DrawBitmap(frameA, fullRect, pA);
            canvas.DrawBitmap(frameB, fullRect, pB);
            return true;
        }

        try
        {
            using var shaderA = SKShader.CreateBitmap(frameA, SKShaderTileMode.Mirror, SKShaderTileMode.Mirror,
                SKMatrix.CreateScale((float)width / Math.Max(1, frameA.Width), (float)height / Math.Max(1, frameA.Height)));
            using var shaderB = SKShader.CreateBitmap(frameB, SKShaderTileMode.Mirror, SKShaderTileMode.Mirror,
                SKMatrix.CreateScale((float)width / Math.Max(1, frameB.Width), (float)height / Math.Max(1, frameB.Height)));

            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["uProgress"] = eased
            };
            if (effect.Uniforms.Contains("uResolution"))
            {
                uniforms["uResolution"] = new[] { (float)width, (float)height };
            }

            var children = new SKRuntimeEffectChildren(effect)
            {
                ["uTextureA"] = shaderA,
                ["uTextureB"] = shaderB
            };

            using var runtimeShader = effect.ToShader(false, uniforms, children);
            if (runtimeShader == null) return false;

            using var paint = new SKPaint { Shader = runtimeShader, IsAntialias = true, FilterQuality = SKFilterQuality.High };
            canvas.DrawRect(0, 0, width, height, paint);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RenderSmoothZoomTransition(
        SKCanvas canvas,
        SKBitmap frameA,
        SKBitmap frameB,
        int width,
        int height,
        float easedProgress)
    {
        var fullRect = new SKRect(0, 0, width, height);
        float cx = width * 0.5f;
        float cy = height * 0.5f;

        // Outgoing Clip A zooms in from 1.0x -> 1.85x; Incoming Clip B zooms in from 0.55x -> 1.0x with mirrored edges
        float scaleA = 1.0f + easedProgress * 0.85f;
        float scaleB = 0.55f + easedProgress * 0.45f;
        float blurStrength = MathF.Sin(easedProgress * MathF.PI); // Peaks at center of transition

        using var shaderA = SKShader.CreateBitmap(
            frameA,
            SKShaderTileMode.Mirror,
            SKShaderTileMode.Mirror,
            SKMatrix.CreateScale((float)width / Math.Max(1, frameA.Width), (float)height / Math.Max(1, frameA.Height)));

        using var shaderB = SKShader.CreateBitmap(
            frameB,
            SKShaderTileMode.Mirror,
            SKShaderTileMode.Mirror,
            SKMatrix.CreateScale((float)width / Math.Max(1, frameB.Width), (float)height / Math.Max(1, frameB.Height)));

        // Multi-sample radial zoom passes (3 concentric layers for studio motion blur)
        const int samples = 3;
        for (int i = 0; i < samples; i++)
        {
            float sampleOffset = (i - 1) * 0.035f * blurStrength;
            byte passAlphaA = (byte)Math.Clamp(MathF.Round(((1f - easedProgress) / samples) * 255f), 0f, 255f);
            if (passAlphaA > 1)
            {
                canvas.Save();
                canvas.Translate(cx, cy);
                canvas.Scale(scaleA + sampleOffset, scaleA + sampleOffset);
                canvas.Translate(-cx, -cy);
                using var paintA = new SKPaint
                {
                    Shader = shaderA,
                    Color = new SKColor(255, 255, 255, passAlphaA),
                    IsAntialias = true,
                    FilterQuality = SKFilterQuality.High
                };
                canvas.DrawRect(fullRect, paintA);
                canvas.Restore();
            }

            byte passAlphaB = (byte)Math.Clamp(MathF.Round((easedProgress / samples) * 255f), 0f, 255f);
            if (passAlphaB > 1)
            {
                canvas.Save();
                canvas.Translate(cx, cy);
                canvas.Scale(scaleB + sampleOffset, scaleB + sampleOffset);
                canvas.Translate(-cx, -cy);
                using var paintB = new SKPaint
                {
                    Shader = shaderB,
                    Color = new SKColor(255, 255, 255, passAlphaB),
                    IsAntialias = true,
                    FilterQuality = SKFilterQuality.High
                };
                canvas.DrawRect(fullRect, paintB);
                canvas.Restore();
            }
        }
    }

    private static (SKPoint Start, SKPoint End) GetWipeGradientPoints(
        float width,
        float height,
        string dir,
        float offsetX,
        float offsetY)
    {
        return dir switch
        {
            "Right" => (new SKPoint(offsetX + width, offsetY), new SKPoint(offsetX, offsetY)),
            "Up" => (new SKPoint(offsetX, offsetY + height), new SKPoint(offsetX, offsetY)),
            "Down" => (new SKPoint(offsetX, offsetY), new SKPoint(offsetX, offsetY + height)),
            "Diagonal" or "TopLeft" or "BottomRight" => (new SKPoint(offsetX, offsetY), new SKPoint(offsetX + width, offsetY + height)),
            _ => (new SKPoint(offsetX, offsetY), new SKPoint(offsetX + width, offsetY)) // Left-to-Right wipe
        };
    }

    private static void DrawSeamShadow(SKCanvas canvas, float x0, float y0, float x1, float y1, bool isVertical)
    {
        using var seamPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 95),
            StrokeWidth = isVertical ? 6f : 6f,
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f)
        };
        canvas.DrawLine(x0, y0, x1, y1, seamPaint);
    }

    private static string NormalizeName(string? type) => (type ?? string.Empty).Trim() switch
    {
        "CrossDissolve" or "CrossFade" or "Dissolve" or "Cross Dissolve" or "Film Dissolve" => "CrossDissolve",
        "FadeIn" or "VideoFadeIn" or "Fade In" => "FadeIn",
        "FadeOut" or "VideoFadeOut" or "Fade Out" => "FadeOut",
        "DipToBlack" or "Dip to Black" => "DipToBlack",
        "DipToWhite" or "Dip to White" => "DipToWhite",
        "FlashCut" => "FlashCut",
        "Wipe" or "DirectionalWipe" or "WipeLeft" or "Wipe Right" or "Wipe Up" or "Wipe Down" => "Wipe",
        "Slide" or "Push" or "PushLeft" or "SlideLeft" or "PushRight" or "PushUp" or "PushDown" => "Slide",
        "WhipZoom" or "ZoomIn" or "ZoomBlur" or "Whip Pan" or "Cross Zoom" => "WhipZoom",
        "Glitch" or "Digital Glitch" => "Glitch",
        "LumaFade" or "Luma Fade" => "LumaFade",
        _ => string.IsNullOrEmpty(type) ? "CrossDissolve" : type
    };

    private static string ResolveDirection(string normType, string explicitDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
            return explicitDir;

        return normType switch
        {
            "PushRight" => "Right",
            "PushUp" => "Up",
            "PushDown" => "Down",
            _ => "Left"
        };
    }
}
