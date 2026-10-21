using AiVideoEditor.Core.Models;
using SkiaSharp;

namespace AiVideoEditor.MediaEngine.Shaders;

public struct ColorGradingParameters
{
    public string PresetName;
    public float Exposure;        // -4.0 to +4.0 EV
    public float Contrast;        // -100 to +100 (0 = 1.0x neutral)
    public float Brightness;      // -100 to +100
    public float Highlights;      // -100 to +100
    public float Shadows;         // -100 to +100
    public float Temperature;     // -100 to +100
    public float Tint;            // -100 to +100
    public float Saturation;      // 0% to 200% (100 = 1.0x neutral)
    public float Vibrance;        // -100 to +100

    public float LiftWheelRed;
    public float LiftWheelGreen;
    public float LiftWheelBlue;
    public float LiftWheelLum;

    public float GammaWheelRed;
    public float GammaWheelGreen;
    public float GammaWheelBlue;
    public float GammaWheelLum;

    public float GainWheelRed;
    public float GainWheelGreen;
    public float GainWheelBlue;
    public float GainWheelLum;

    public static ColorGradingParameters FromClip(TimelineClip clip)
    {
        return new ColorGradingParameters
        {
            PresetName = clip.ColorFilterPreset ?? "Rec.709 Reset",
            Exposure = clip.Exposure,
            Contrast = clip.Contrast,
            Brightness = clip.Brightness,
            Highlights = clip.Highlights,
            Shadows = clip.Shadows,
            Temperature = clip.Temperature,
            Tint = clip.Tint,
            Saturation = clip.Saturation,
            Vibrance = clip.Vibrance,
            LiftWheelRed = clip.LiftWheelRed,
            LiftWheelGreen = clip.LiftWheelGreen,
            LiftWheelBlue = clip.LiftWheelBlue,
            LiftWheelLum = clip.LiftWheelLum,
            GammaWheelRed = clip.GammaWheelRed,
            GammaWheelGreen = clip.GammaWheelGreen,
            GammaWheelBlue = clip.GammaWheelBlue,
            GammaWheelLum = clip.GammaWheelLum,
            GainWheelRed = clip.GainWheelRed,
            GainWheelGreen = clip.GainWheelGreen,
            GainWheelBlue = clip.GainWheelBlue,
            GainWheelLum = clip.GainWheelLum
        };
    }
}

/// <summary>
/// Rec.709 Color Science Matrix &amp; 3-Way Color Wheel LUT Processor (Section 3.1).
/// Uses normalized [0..1] SkiaSharp translation offsets and a 0.5 mid-gray pivot for Contrast,
/// eliminating white/black blowouts while supporting 256-entry Gamma LUTs.
/// </summary>
public static class ColorScienceProcessor
{
    private static readonly byte[] IdentityLut = BuildIdentityTable();

    public static bool IsNeutral(ColorGradingParameters p)
    {
        string preset = p.PresetName ?? string.Empty;
        bool hasPreset = !string.IsNullOrWhiteSpace(preset) &&
                         !preset.Equals("Rec.709 Reset", StringComparison.OrdinalIgnoreCase) &&
                         !preset.Equals("Standard (Rec.709)", StringComparison.OrdinalIgnoreCase) &&
                         !preset.Equals("Rec709Standard", StringComparison.OrdinalIgnoreCase) &&
                         !preset.Equals("None", StringComparison.OrdinalIgnoreCase);

        if (hasPreset) return false;

        return MathF.Abs(p.Exposure) < 0.005f &&
               MathF.Abs(p.Contrast) < 0.05f &&
               MathF.Abs(p.Brightness) < 0.05f &&
               MathF.Abs(p.Highlights) < 0.05f &&
               MathF.Abs(p.Shadows) < 0.05f &&
               MathF.Abs(p.Temperature) < 0.05f &&
               MathF.Abs(p.Tint) < 0.05f &&
               MathF.Abs(p.Saturation - 100f) < 0.05f &&
               MathF.Abs(p.Vibrance) < 0.05f &&
               MathF.Abs(p.LiftWheelRed) < 0.002f &&
               MathF.Abs(p.LiftWheelGreen) < 0.002f &&
               MathF.Abs(p.LiftWheelBlue) < 0.002f &&
               MathF.Abs(p.LiftWheelLum) < 0.002f &&
               MathF.Abs(p.GammaWheelRed) < 0.002f &&
               MathF.Abs(p.GammaWheelGreen) < 0.002f &&
               MathF.Abs(p.GammaWheelBlue) < 0.002f &&
               MathF.Abs(p.GammaWheelLum) < 0.002f &&
               MathF.Abs(p.GainWheelRed) < 0.002f &&
               MathF.Abs(p.GainWheelGreen) < 0.002f &&
               MathF.Abs(p.GainWheelBlue) < 0.002f &&
               MathF.Abs(p.GainWheelLum) < 0.002f;
    }

    public static SKColorFilter? BuildFromClip(TimelineClip clip)
    {
        var p = ColorGradingParameters.FromClip(clip);
        if (IsNeutral(p)) return null;
        return BuildColorPipeline(p);
    }

    public static SKColorFilter BuildColorPipeline(ColorGradingParameters p)
    {
        // Apply Creative Look Preset offsets on top of user sliders if a preset is active
        ApplyPresetOffsets(ref p);

        // 1. Exposure: smooth stops (-4.0 to +4.0 EV)
        float expGain = MathF.Pow(2.0f, Math.Clamp(p.Exposure, -4f, 4f) * 0.65f);

        // 2. Contrast: centered strictly at 0.5 mid-gray pivot (-100 to +100)
        // Maps -100 -> 0.25x slope, 0 -> 1.0x slope, +100 -> 2.25x slope
        float normContrast = Math.Clamp(p.Contrast, -100f, 100f) / 100f;
        float contrastSlope = normContrast >= 0f
            ? 1.0f + (normContrast * 1.25f)
            : 1.0f + (normContrast * 0.75f);
        float contrastOffset = 0.5f * (1.0f - contrastSlope);

        // 3. Brightness / Lift (-100 to +100) + Shadows/Highlights approximation
        float liftOffset = (Math.Clamp(p.Brightness, -100f, 100f) / 100f) * 0.20f
                         + (Math.Clamp(p.Shadows, -100f, 100f) / 100f) * 0.12f
                         + (p.LiftWheelLum * 0.15f);
        float highlightGain = 1.0f + (Math.Clamp(p.Highlights, -100f, 100f) / 100f) * 0.25f;

        // 4. White Balance: Temperature (-100 Cool to +100 Warm) & Tint (-100 Green to +100 Magenta)
        float temp = (Math.Clamp(p.Temperature, -100f, 100f) / 100f) * 0.18f;
        float tint = (Math.Clamp(p.Tint, -100f, 100f) / 100f) * 0.14f;

        float rScale = expGain * highlightGain * (1.0f + temp + tint * 0.5f) * contrastSlope;
        float gScale = expGain * highlightGain * (1.0f - tint) * contrastSlope;
        float bScale = expGain * highlightGain * (1.0f - temp + tint * 0.5f) * contrastSlope;

        float totalOffsetR = contrastOffset + liftOffset + p.LiftWheelRed * 0.15f;
        float totalOffsetG = contrastOffset + liftOffset + p.LiftWheelGreen * 0.15f;
        float totalOffsetB = contrastOffset + liftOffset + p.LiftWheelBlue * 0.15f;

        // 5. Saturation (0% to 200%, 100% = 1.0) + Vibrance
        float sat = Math.Clamp(p.Saturation, 0f, 200f) / 100f;
        float vib = (Math.Clamp(p.Vibrance, -100f, 100f) / 100f) * 0.5f;
        float effectiveSat = Math.Clamp(sat + vib, 0f, 2.5f);

        const float lumR = 0.2126f, lumG = 0.7152f, lumB = 0.0722f;
        float sr = (1.0f - effectiveSat) * lumR;
        float sg = (1.0f - effectiveSat) * lumG;
        float sb = (1.0f - effectiveSat) * lumB;

        // Apply Gain Wheel multipliers
        rScale *= Math.Max(0.05f, 1.0f + (p.GainWheelLum + p.GainWheelRed) * 0.5f);
        gScale *= Math.Max(0.05f, 1.0f + (p.GainWheelLum + p.GainWheelGreen) * 0.5f);
        bScale *= Math.Max(0.05f, 1.0f + (p.GainWheelLum + p.GainWheelBlue) * 0.5f);

        float[] matrix = new float[20]
        {
            rScale * (sr + effectiveSat), rScale * sg,                  rScale * sb,                  0f, totalOffsetR,
            gScale * sr,                  gScale * (sg + effectiveSat), gScale * sb,                  0f, totalOffsetG,
            bScale * sr,                  bScale * sg,                  bScale * (sb + effectiveSat), 0f, totalOffsetB,
            0f,                           0f,                           0f,                           1f, 0f
        };

        var baseFilter = SKColorFilter.CreateColorMatrix(matrix);

        // 6. Optional Gamma Wheel via 256-entry LUT table so midtones bend smoothly without clipping black/white!
        if (MathF.Abs(p.GammaWheelLum) > 0.001f || MathF.Abs(p.GammaWheelRed) > 0.001f ||
            MathF.Abs(p.GammaWheelGreen) > 0.001f || MathF.Abs(p.GammaWheelBlue) > 0.001f)
        {
            byte[] rLut = BuildGammaTable(1.0f - (p.GammaWheelLum + p.GammaWheelRed) * 0.45f);
            byte[] gLut = BuildGammaTable(1.0f - (p.GammaWheelLum + p.GammaWheelGreen) * 0.45f);
            byte[] bLut = BuildGammaTable(1.0f - (p.GammaWheelLum + p.GammaWheelBlue) * 0.45f);
            var gammaFilter = SKColorFilter.CreateTable(IdentityLut, rLut, gLut, bLut);
            return SKColorFilter.CreateCompose(gammaFilter, baseFilter);
        }

        return baseFilter;
    }

    private static void ApplyPresetOffsets(ref ColorGradingParameters p)
    {
        string preset = p.PresetName ?? string.Empty;
        if (preset.Contains("Teal", StringComparison.OrdinalIgnoreCase))
        {
            p.Contrast += 15f;
            p.Saturation += 12f;
            p.LiftWheelRed -= 0.12f;
            p.LiftWheelBlue += 0.14f;
            p.GainWheelRed += 0.14f;
            p.GainWheelBlue -= 0.08f;
        }
        else if (preset.Contains("Moody", StringComparison.OrdinalIgnoreCase) || preset.Contains("Bleach", StringComparison.OrdinalIgnoreCase))
        {
            p.Contrast -= 18f;
            p.Shadows += 22f;
            p.Saturation -= 28f;
            p.LiftWheelLum += 0.06f;
        }
        else if (preset.Contains("Golden", StringComparison.OrdinalIgnoreCase) || preset.Contains("Warm", StringComparison.OrdinalIgnoreCase))
        {
            p.Temperature += 32f;
            p.Tint += 8f;
            p.Saturation += 14f;
            p.Highlights += 8f;
        }
        else if (preset.Contains("Cyberpunk", StringComparison.OrdinalIgnoreCase))
        {
            p.Temperature -= 24f;
            p.Tint += 38f;
            p.Contrast += 20f;
            p.Vibrance += 35f;
        }
        else if (preset.Contains("Kodak", StringComparison.OrdinalIgnoreCase) || preset.Contains("Vintage", StringComparison.OrdinalIgnoreCase))
        {
            p.Temperature += 18f;
            p.Contrast += 22f;
            p.Shadows += 10f;
            p.GammaWheelRed += 0.06f;
            p.LiftWheelBlue += 0.05f;
        }
        else if (preset.Contains("Noir", StringComparison.OrdinalIgnoreCase) || preset.Contains("Black & White", StringComparison.OrdinalIgnoreCase))
        {
            p.Saturation = 0f;
            p.Vibrance = -100f;
            p.Contrast += 28f;
        }
    }

    private static byte[] BuildGammaTable(float gamma)
    {
        gamma = Math.Clamp(gamma, 0.25f, 3.0f);
        byte[] table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            float normalized = i / 255f;
            float corrected = MathF.Pow(normalized, gamma);
            table[i] = (byte)Math.Clamp((int)MathF.Round(corrected * 255f), 0, 255);
        }
        return table;
    }

    private static byte[] BuildIdentityTable()
    {
        byte[] table = new byte[256];
        for (int i = 0; i < 256; i++)
            table[i] = (byte)i;
        return table;
    }

    /// <summary>
    /// Fast (< 0.4ms) 160x90 subsampled RGB &amp; Luma 64-bin histogram calculation for VideoScopeControl.
    /// Applies the active colorFilter to a tiny 160x90 buffer so scopes respond live to color wheels &amp; sliders.
    /// </summary>
    public static (int[] R, int[] G, int[] B, int[] Luma) SampleScopeHistograms(
        SKBitmap? bitmap,
        SKColorFilter? colorFilter = null)
    {
        int[] r = new int[64];
        int[] g = new int[64];
        int[] b = new int[64];
        int[] luma = new int[64];

        if (bitmap == null || bitmap.Width <= 4 || bitmap.Height <= 4)
            return (r, g, b, luma);

        if (colorFilter != null)
        {
            using var tiny = new SKBitmap(160, 90, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var tinyCanvas = new SKCanvas(tiny);
            using var paint = new SKPaint { ColorFilter = colorFilter };
            tinyCanvas.DrawBitmap(bitmap, new SKRect(0, 0, 160, 90), paint);
            SampleScopeHistograms(tiny, r, g, b, luma);
            return (r, g, b, luma);
        }

        SampleScopeHistograms(bitmap, r, g, b, luma);
        return (r, g, b, luma);
    }

    public static unsafe void SampleScopeHistograms(
        SKBitmap? bitmap,
        int[] rBins64,
        int[] gBins64,
        int[] bBins64,
        int[] lumaBins64)
    {
        Array.Clear(rBins64, 0, rBins64.Length);
        Array.Clear(gBins64, 0, gBins64.Length);
        Array.Clear(bBins64, 0, bBins64.Length);
        Array.Clear(lumaBins64, 0, lumaBins64.Length);

        if (bitmap == null || bitmap.Width <= 4 || bitmap.Height <= 4) return;
        IntPtr pixels = bitmap.GetPixels();
        if (pixels == IntPtr.Zero) return;

        int w = bitmap.Width;
        int h = bitmap.Height;
        int rowBytes = bitmap.RowBytes;
        int stepX = Math.Max(1, w / 160);
        int stepY = Math.Max(1, h / 90);
        byte* basePtr = (byte*)pixels;

        for (int y = 0; y < h; y += stepY)
        {
            byte* row = basePtr + (y * rowBytes);
            for (int x = 0; x < w; x += stepX)
            {
                int offset = x * 4;
                byte b = row[offset];
                byte g = row[offset + 1];
                byte r = row[offset + 2];

                rBins64[r >> 2]++;
                gBins64[g >> 2]++;
                bBins64[b >> 2]++;

                int luma = (r * 54 + g * 183 + b * 19) >> 8;
                lumaBins64[Math.Clamp(luma >> 2, 0, 63)]++;
            }
        }
    }
}
