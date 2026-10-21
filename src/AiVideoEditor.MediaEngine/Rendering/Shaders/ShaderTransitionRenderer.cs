using SkiaSharp;

namespace AiVideoEditor.MediaEngine.Rendering.Shaders;

public sealed class ShaderTransitionRenderer
{
    private readonly Dictionary<string, SKRuntimeEffect?> _compiledEffects = new();

    private const string CrossFadeSksl = @"
uniform shader uTextureA;
uniform shader uTextureB;
uniform float uProgress;
half4 main(float2 coord) {
    return mix(uTextureA.eval(coord), uTextureB.eval(coord), half(uProgress));
}";

    private const string ZoomBlurSksl = @"
uniform shader uTextureA;
uniform shader uTextureB;
uniform float uProgress;
uniform float2 uResolution;
half4 main(float2 coord) {
    float2 center = uResolution * 0.5;
    float zoomA = 1.0 + uProgress * 0.65;
    float zoomB = 1.65 - uProgress * 0.65;
    float2 coordA = (coord - center) / zoomA + center;
    float2 coordB = (coord - center) / zoomB + center;
    return mix(uTextureA.eval(coordA), uTextureB.eval(coordB), half(uProgress));
}";

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

    private const string DirectionalWipeSksl = @"
uniform shader uTextureA;
uniform shader uTextureB;
uniform float uProgress;
uniform float2 uResolution;
half4 main(float2 coord) {
    float normX = coord.x / max(1.0, uResolution.x);
    float edge = smoothstep(uProgress - 0.06, uProgress + 0.06, normX);
    return mix(uTextureB.eval(coord), uTextureA.eval(coord), half(edge));
}";

    public ShaderTransitionRenderer()
    {
        TryCompile("CrossFade", CrossFadeSksl);
        TryCompile("ZoomBlur", ZoomBlurSksl);
        TryCompile("ZoomIn", ZoomBlurSksl);
        TryCompile("Glitch", GlitchSksl);
        TryCompile("DirectionalWipe", DirectionalWipeSksl);
        TryCompile("WipeLeft", DirectionalWipeSksl);
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
    /// Renders single-clip transition envelope (In/Out transition on a single layer) or two-clip SKSL transition.
    /// </summary>
    public void ApplySingleClipTransition(
        SKCanvas canvas,
        SKBitmap frame,
        SKRect destRect,
        string transitionType,
        float progress,
        bool isIncoming,
        SKPaint basePaint)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        float t = isIncoming ? progress : (1f - progress);

        canvas.Save();

        switch (transitionType)
        {
            case "ZoomIn":
            case "ZoomBlur":
            {
                float scale = 0.65f + 0.35f * t;
                canvas.Translate(destRect.MidX, destRect.MidY);
                canvas.Scale(scale, scale);
                canvas.Translate(-destRect.MidX, -destRect.MidY);
                basePaint.Color = basePaint.Color.WithAlpha((byte)(basePaint.Color.Alpha * t));
                canvas.DrawBitmap(frame, destRect, basePaint);
                break;
            }
            case "WipeLeft":
            case "DirectionalWipe":
            {
                var clipRect = new SKRect(destRect.Left, destRect.Top, destRect.Left + destRect.Width * t, destRect.Bottom);
                canvas.ClipRect(clipRect);
                canvas.DrawBitmap(frame, destRect, basePaint);
                break;
            }
            case "Glitch":
            {
                float jitter = (1f - t) * 32f;
                int slices = 8;
                float sliceHeight = destRect.Height / slices;
                for (int i = 0; i < slices; i++)
                {
                    float offset = (float)Math.Sin(i * 9.2 + progress * 25.0) * jitter;
                    var sliceRect = new SKRect(destRect.Left, destRect.Top + i * sliceHeight, destRect.Right, destRect.Top + (i + 1) * sliceHeight);
                    canvas.Save();
                    canvas.ClipRect(sliceRect);
                    var shifted = sliceRect;
                    shifted.Offset(offset, 0);
                    canvas.DrawBitmap(frame, destRect, basePaint);
                    canvas.Restore();
                }
                break;
            }
            default: // CrossFade / Dissolve
            {
                basePaint.Color = basePaint.Color.WithAlpha((byte)(basePaint.Color.Alpha * t));
                canvas.DrawBitmap(frame, destRect, basePaint);
                break;
            }
        }

        canvas.Restore();
    }

    public bool TryRenderDualClipShader(
        SKCanvas canvas,
        SKBitmap frameA,
        SKBitmap frameB,
        int width,
        int height,
        string transitionType,
        float progress)
    {
        if (!_compiledEffects.TryGetValue(transitionType, out var effect) || effect == null)
            return false;

        try
        {
            using var shaderA = SKShader.CreateBitmap(frameA, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                SKMatrix.CreateScale((float)width / frameA.Width, (float)height / frameA.Height));
            using var shaderB = SKShader.CreateBitmap(frameB, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                SKMatrix.CreateScale((float)width / frameB.Width, (float)height / frameB.Height));

            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["uProgress"] = Math.Clamp(progress, 0f, 1f)
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

            using var paint = new SKPaint { Shader = runtimeShader, IsAntialias = true };
            canvas.DrawRect(0, 0, width, height, paint);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
