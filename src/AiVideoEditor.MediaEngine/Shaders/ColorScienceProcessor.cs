using System.Runtime.CompilerServices;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using SkiaSharp;

namespace AiVideoEditor.MediaEngine.Shaders;

public sealed class ScopeRenderData
{
    public int[] R { get; } = new int[64];
    public int[] G { get; } = new int[64];
    public int[] B { get; } = new int[64];
    public int[] Luma { get; } = new int[64];

    public int[] HistR => R;
    public int[] HistG => G;
    public int[] HistB => B;
    public int[] HistLuma => Luma;

    // 48 Horizontal Columns x 48 Vertical IRE Bins for RGB Parade & Luma Waveform
    public int[,] ParadeR { get; } = new int[48, 48];
    public int[,] ParadeG { get; } = new int[48, 48];
    public int[,] ParadeB { get; } = new int[48, 48];
    public int[,] WaveformLuma { get; } = new int[48, 48];

    // 64 x 64 UV Chrominance Plane for Circular Vectorscope
    public int[,] Vectorscope { get; } = new int[64, 64];
}

public struct ColorGradingParameters
{
    public string PresetName;
    public float Exposure;        // -4.0 to +4.0 EV
    public float Contrast;        // -100 to +100 (0 = 1.0x neutral around 0.5 mid-gray)
    public float Brightness;      // -100 to +100
    public float Highlights;      // -100 to +100
    public float Shadows;         // -100 to +100
    public float Whites;          // -100 to +100
    public float Blacks;          // -100 to +100
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

    public string? LutFilePath;
    public float LutIntensity;    // 0% to 100%

    public List<CurveControlPoint>? MasterCurvePoints;
    public List<CurveControlPoint>? RedCurvePoints;
    public List<CurveControlPoint>? GreenCurvePoints;
    public List<CurveControlPoint>? BlueCurvePoints;

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
            Whites = clip.Whites,
            Blacks = clip.Blacks,
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
            GainWheelLum = clip.GainWheelLum,
            LutFilePath = clip.LutFilePath,
            LutIntensity = clip.LutIntensity,
            MasterCurvePoints = clip.MasterCurvePoints,
            RedCurvePoints = clip.RedCurvePoints,
            GreenCurvePoints = clip.GreenCurvePoints,
            BlueCurvePoints = clip.BlueCurvePoints
        };
    }
}

/// <summary>
/// Rec.709 Color Science Matrix, 16-bit Half-Float (SKColorType.RgbaF16) HDR Pipeline,
/// 3D/1D .cube LUT Trilinear Evaluator, 3-Way Color Wheel LUTs, Cubic Bezier Curves,
/// 8-Hue HSL Secondary Mixer, and Multi-View Broadcast Scopes.
/// </summary>
public static class ColorScienceProcessor
{
    private static readonly byte[] IdentityLut = BuildIdentityTable();

    // 8 Hue Centers in Degrees: Red(0), Orange/Skin(30), Yellow(60), Green(120), Aqua(180), Blue(240), Purple(280), Magenta(320)
    private static readonly float[] HueCentersDeg = { 0f, 30f, 60f, 120f, 180f, 240f, 280f, 320f };

    public static bool HasActiveHslAdjustments(TimelineClip clip)
    {
        if (clip.HslHueShift == null || clip.HslSatShift == null || clip.HslLumShift == null)
            return false;

        for (int i = 0; i < 8; i++)
        {
            if (i < clip.HslHueShift.Length && MathF.Abs(clip.HslHueShift[i]) > 0.1f) return true;
            if (i < clip.HslSatShift.Length && MathF.Abs(clip.HslSatShift[i]) > 0.1f) return true;
            if (i < clip.HslLumShift.Length && MathF.Abs(clip.HslLumShift[i]) > 0.1f) return true;
        }
        return false;
    }

    public static bool HasActive3DLut(TimelineClip clip)
    {
        if (string.IsNullOrWhiteSpace(clip.LutFilePath) || clip.LutIntensity <= 0.1f)
            return false;

        var lut = CubeLutParser.GetOrLoad(clip.LutFilePath);
        return lut != null && lut.Is3D;
    }

    public static bool HasActiveLut(ColorGradingParameters p)
    {
        if (string.IsNullOrWhiteSpace(p.LutFilePath) || p.LutIntensity <= 0.1f)
            return false;

        return CubeLutParser.GetOrLoad(p.LutFilePath) != null;
    }

    public static bool IsCurveIdentity(List<CurveControlPoint>? pts)
    {
        if (pts == null || pts.Count < 2) return true;
        if (pts.Count == 2)
        {
            return MathF.Abs(pts[0].X - 0f) < 0.005f &&
                   MathF.Abs(pts[0].Y - 0f) < 0.005f &&
                   MathF.Abs(pts[1].X - 1f) < 0.005f &&
                   MathF.Abs(pts[1].Y - 1f) < 0.005f;
        }
        for (int i = 0; i < pts.Count; i++)
        {
            if (MathF.Abs(pts[i].X - pts[i].Y) > 0.005f)
                return false;
        }
        return true;
    }

    public static bool HasActiveCurves(ColorGradingParameters p)
    {
        return !IsCurveIdentity(p.MasterCurvePoints) ||
               !IsCurveIdentity(p.RedCurvePoints) ||
               !IsCurveIdentity(p.GreenCurvePoints) ||
               !IsCurveIdentity(p.BlueCurvePoints);
    }

    public static bool IsNeutral(ColorGradingParameters p)
    {
        string preset = p.PresetName ?? string.Empty;
        bool hasPreset = !string.IsNullOrWhiteSpace(preset) &&
                         !preset.Equals("Rec.709 Reset", StringComparison.OrdinalIgnoreCase) &&
                         !preset.Equals("Standard (Rec.709)", StringComparison.OrdinalIgnoreCase) &&
                         !preset.Equals("Rec709Standard", StringComparison.OrdinalIgnoreCase) &&
                         !preset.Equals("None", StringComparison.OrdinalIgnoreCase);

        if (hasPreset || HasActiveCurves(p) || HasActiveLut(p)) return false;

        return MathF.Abs(p.Exposure) < 0.005f &&
               MathF.Abs(p.Contrast) < 0.05f &&
               MathF.Abs(p.Brightness) < 0.05f &&
               MathF.Abs(p.Highlights) < 0.05f &&
               MathF.Abs(p.Shadows) < 0.05f &&
               MathF.Abs(p.Whites) < 0.05f &&
               MathF.Abs(p.Blacks) < 0.05f &&
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
        ApplyPresetOffsets(ref p);

        // 1. Exposure: smooth stops (-4.0 to +4.0 EV)
        float expGain = MathF.Pow(2.0f, Math.Clamp(p.Exposure, -4f, 4f) * 0.65f);

        // 2. Contrast: centered strictly at 0.5 mid-gray pivot (-100 to +100)
        float normContrast = Math.Clamp(p.Contrast, -100f, 100f) / 100f;
        float contrastSlope = normContrast >= 0f
            ? 1.0f + (normContrast * 1.25f)
            : 1.0f + (normContrast * 0.75f);
        float contrastOffset = 0.5f * (1.0f - contrastSlope);

        // 3. Brightness / Lift / Blacks / Shadows + Highlights / Whites
        float liftOffset = (Math.Clamp(p.Brightness, -100f, 100f) / 100f) * 0.20f
                         + (Math.Clamp(p.Shadows, -100f, 100f) / 100f) * 0.12f
                         + (Math.Clamp(p.Blacks, -100f, 100f) / 100f) * 0.14f
                         + (p.LiftWheelLum * 0.15f);
        float highlightGain = 1.0f
                            + (Math.Clamp(p.Highlights, -100f, 100f) / 100f) * 0.25f
                            + (Math.Clamp(p.Whites, -100f, 100f) / 100f) * 0.22f;

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

        // 6. Combine Gamma Wheel, RGB/Luma Bezier Curves, and 1D .cube LUT into a single 256-entry LUT table
        bool hasGamma = MathF.Abs(p.GammaWheelLum) > 0.001f || MathF.Abs(p.GammaWheelRed) > 0.001f ||
                        MathF.Abs(p.GammaWheelGreen) > 0.001f || MathF.Abs(p.GammaWheelBlue) > 0.001f;
        bool hasCurves = HasActiveCurves(p);

        var loadedLut = !string.IsNullOrWhiteSpace(p.LutFilePath) && p.LutIntensity > 0.1f
            ? CubeLutParser.GetOrLoad(p.LutFilePath)
            : null;
        bool has1DLut = loadedLut != null && !loadedLut.Is3D;

        if (hasGamma || hasCurves || has1DLut)
        {
            float gammaR = Math.Clamp(1.0f - (p.GammaWheelLum + p.GammaWheelRed) * 0.45f, 0.25f, 3.0f);
            float gammaG = Math.Clamp(1.0f - (p.GammaWheelLum + p.GammaWheelGreen) * 0.45f, 0.25f, 3.0f);
            float gammaB = Math.Clamp(1.0f - (p.GammaWheelLum + p.GammaWheelBlue) * 0.45f, 0.25f, 3.0f);

            var (finalR, finalG, finalB) = MonotonicCubicCurveSolver.BuildCompositeRgbLut(
                p.MasterCurvePoints,
                p.RedCurvePoints,
                p.GreenCurvePoints,
                p.BlueCurvePoints);

            float lutMix = Math.Clamp(p.LutIntensity / 100f, 0f, 1f);

            byte[] rTable = new byte[256];
            byte[] gTable = new byte[256];
            byte[] bTable = new byte[256];

            for (int i = 0; i < 256; i++)
            {
                float norm = i / 255f;

                float vr = hasGamma ? MathF.Pow(norm, gammaR) : norm;
                float vg = hasGamma ? MathF.Pow(norm, gammaG) : norm;
                float vb = hasGamma ? MathF.Pow(norm, gammaB) : norm;

                if (hasCurves)
                {
                    int ir = Math.Clamp((int)MathF.Round(vr * 255f), 0, 255);
                    int ig = Math.Clamp((int)MathF.Round(vg * 255f), 0, 255);
                    int ib = Math.Clamp((int)MathF.Round(vb * 255f), 0, 255);

                    vr = finalR[ir] / 255f;
                    vg = finalG[ig] / 255f;
                    vb = finalB[ib] / 255f;
                }

                if (has1DLut && loadedLut != null)
                {
                    loadedLut.Sample1DLinear(vr, vg, vb, out float lr, out float lg, out float lb);
                    vr = vr + (lr - vr) * lutMix;
                    vg = vg + (lg - vg) * lutMix;
                    vb = vb + (lb - vb) * lutMix;
                }

                rTable[i] = (byte)Math.Clamp((int)MathF.Round(vr * 255f), 0, 255);
                gTable[i] = (byte)Math.Clamp((int)MathF.Round(vg * 255f), 0, 255);
                bTable[i] = (byte)Math.Clamp((int)MathF.Round(vb * 255f), 0, 255);
            }

            var lutFilter = SKColorFilter.CreateTable(IdentityLut, rTable, gTable, bTable);
            return SKColorFilter.CreateCompose(lutFilter, baseFilter);
        }

        return baseFilter;
    }

    /// <summary>
    /// Converts an 8-bit BGRA bitmap into a 16-bit Half-Float (SKColorType.RgbaF16) HDR surface to eliminate quantization banding on Log footage.
    /// </summary>
    public static unsafe SKBitmap ConvertToRgbaF16(SKBitmap source)
    {
        if (source.ColorType == SKColorType.RgbaF16)
            return source.Copy();

        var f16Bmp = new SKBitmap(source.Width, source.Height, SKColorType.RgbaF16, SKAlphaType.Premul);
        IntPtr srcPtr = source.GetPixels();
        IntPtr dstPtr = f16Bmp.GetPixels();

        if (srcPtr == IntPtr.Zero || dstPtr == IntPtr.Zero)
            return f16Bmp;

        int w = source.Width;
        int h = source.Height;
        int srcRowBytes = source.RowBytes;
        int dstRowBytes = f16Bmp.RowBytes;
        byte* srcBase = (byte*)srcPtr;
        byte* dstBase = (byte*)dstPtr;

        Parallel.For(0, h, y =>
        {
            byte* srcRow = srcBase + (y * srcRowBytes);
            Half* dstRow = (Half*)(dstBase + (y * dstRowBytes));

            for (int x = 0; x < w; x++)
            {
                int srcOff = x * 4;
                int dstOff = x * 4;

                float b = srcRow[srcOff] / 255f;
                float g = srcRow[srcOff + 1] / 255f;
                float r = srcRow[srcOff + 2] / 255f;
                float a = srcRow[srcOff + 3] / 255f;

                dstRow[dstOff] = (Half)r;
                dstRow[dstOff + 1] = (Half)g;
                dstRow[dstOff + 2] = (Half)b;
                dstRow[dstOff + 3] = (Half)a;
            }
        });

        return f16Bmp;
    }

    /// <summary>
    /// Evaluates a 3D .cube LUT (17^3, 33^3, 64^3) using trilinear interpolation in 16-bit float precision (RgbaF16) or 32-bit float intermediate precision.
    /// </summary>
    public static unsafe void ApplyCubeLutInPlace(SKBitmap bitmap, TimelineClip clip)
    {
        if (string.IsNullOrWhiteSpace(clip.LutFilePath) || clip.LutIntensity <= 0.1f)
            return;

        var lut = CubeLutParser.GetOrLoad(clip.LutFilePath);
        if (lut == null)
            return;

        IntPtr ptr = bitmap.GetPixels();
        if (ptr == IntPtr.Zero)
            return;

        float intensity = Math.Clamp(clip.LutIntensity / 100f, 0f, 1f);
        int w = bitmap.Width;
        int h = bitmap.Height;
        int rowBytes = bitmap.RowBytes;
        byte* basePtr = (byte*)ptr;

        if (bitmap.ColorType == SKColorType.RgbaF16)
        {
            Parallel.For(0, h, y =>
            {
                Half* row = (Half*)(basePtr + (y * rowBytes));
                for (int x = 0; x < w; x++)
                {
                    int off = x * 4;
                    float r = (float)row[off];
                    float g = (float)row[off + 1];
                    float b = (float)row[off + 2];

                    lut.Evaluate(r, g, b, out float lr, out float lg, out float lb);

                    row[off] = (Half)Math.Clamp(r + (lr - r) * intensity, 0f, 1f);
                    row[off + 1] = (Half)Math.Clamp(g + (lg - g) * intensity, 0f, 1f);
                    row[off + 2] = (Half)Math.Clamp(b + (lb - b) * intensity, 0f, 1f);
                }
            });
        }
        else
        {
            Parallel.For(0, h, y =>
            {
                byte* row = basePtr + (y * rowBytes);
                for (int x = 0; x < w; x++)
                {
                    int off = x * 4;
                    float b = row[off] / 255f;
                    float g = row[off + 1] / 255f;
                    float r = row[off + 2] / 255f;

                    lut.Evaluate(r, g, b, out float lr, out float lg, out float lb);

                    float finalR = Math.Clamp(r + (lr - r) * intensity, 0f, 1f);
                    float finalG = Math.Clamp(g + (lg - g) * intensity, 0f, 1f);
                    float finalB = Math.Clamp(b + (lb - b) * intensity, 0f, 1f);

                    row[off] = (byte)MathF.Round(finalB * 255f);
                    row[off + 1] = (byte)MathF.Round(finalG * 255f);
                    row[off + 2] = (byte)MathF.Round(finalR * 255f);
                }
            });
        }
    }

    /// <summary>
    /// Evaluates a Monotonic Cubic Hermite Spline over 256 entries in [0..1].
    /// </summary>
    public static float[] EvaluateSplineLut01(List<CurveControlPoint>? rawPoints)
    {
        return MonotonicCubicCurveSolver.SolveLut01(rawPoints);
    }

    /// <summary>
    /// Applies the 8-Hue HSL Secondary Color Mixer (Hue vs Hue, Hue vs Sat, Hue vs Luma) in-place.
    /// Supports both 16-bit Half-Float (SKColorType.RgbaF16) and 8-bit (SKColorType.Bgra8888) bitmaps.
    /// </summary>
    public static unsafe void ApplyHslSecondaryInPlace(SKBitmap bitmap, TimelineClip clip)
    {
        if (!HasActiveHslAdjustments(clip)) return;
        IntPtr ptr = bitmap.GetPixels();
        if (ptr == IntPtr.Zero) return;

        int w = bitmap.Width;
        int h = bitmap.Height;
        int rowBytes = bitmap.RowBytes;
        byte* basePtr = (byte*)ptr;

        float[] hueShift = clip.HslHueShift;
        float[] satShift = clip.HslSatShift;
        float[] lumShift = clip.HslLumShift;

        if (bitmap.ColorType == SKColorType.RgbaF16)
        {
            Parallel.For(0, h, y =>
            {
                Half* row = (Half*)(basePtr + (y * rowBytes));
                for (int x = 0; x < w; x++)
                {
                    int off = x * 4;
                    float r = Math.Clamp((float)row[off], 0f, 1f);
                    float g = Math.Clamp((float)row[off + 1], 0f, 1f);
                    float b = Math.Clamp((float)row[off + 2], 0f, 1f);

                    if (EvaluateHslPixel(r, g, b, hueShift, satShift, lumShift, out float nr, out float ng, out float nb))
                    {
                        row[off] = (Half)nr;
                        row[off + 1] = (Half)ng;
                        row[off + 2] = (Half)nb;
                    }
                }
            });
            return;
        }

        Parallel.For(0, h, y =>
        {
            byte* row = basePtr + (y * rowBytes);
            for (int x = 0; x < w; x++)
            {
                int off = x * 4;
                float b = row[off] / 255f;
                float g = row[off + 1] / 255f;
                float r = row[off + 2] / 255f;

                if (EvaluateHslPixel(r, g, b, hueShift, satShift, lumShift, out float nr, out float ng, out float nb))
                {
                    row[off] = (byte)Math.Clamp((int)(nb * 255f), 0, 255);
                    row[off + 1] = (byte)Math.Clamp((int)(ng * 255f), 0, 255);
                    row[off + 2] = (byte)Math.Clamp((int)(nr * 255f), 0, 255);
                }
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EvaluateHslPixel(
        float r,
        float g,
        float b,
        float[] hueShift,
        float[] satShift,
        float[] lumShift,
        out float nr,
        out float ng,
        out float nb)
    {
        nr = r;
        ng = g;
        nb = b;

        RgbToHsl(r, g, b, out float hue, out float sat, out float lum);
        if (sat < 0.02f) return false;

        float totalHueDelta = 0f;
        float totalSatDelta = 0f;
        float totalLumDelta = 0f;

        for (int band = 0; band < 8; band++)
        {
            float dist = ShortestAngleDist(hue, HueCentersDeg[band]);
            if (dist < 42f)
            {
                float weight = 0.5f * (1f + MathF.Cos((dist / 42f) * MathF.PI));
                totalHueDelta += (hueShift[band] * 0.45f) * weight;
                totalSatDelta += (satShift[band] / 100f) * weight;
                totalLumDelta += (lumShift[band] / 100f) * 0.35f * weight;
            }
        }

        if (MathF.Abs(totalHueDelta) > 0.01f || MathF.Abs(totalSatDelta) > 0.002f || MathF.Abs(totalLumDelta) > 0.002f)
        {
            hue = (hue + totalHueDelta) % 360f;
            if (hue < 0f) hue += 360f;
            sat = Math.Clamp(sat * (1f + totalSatDelta), 0f, 1f);
            lum = Math.Clamp(lum + totalLumDelta, 0f, 1f);

            HslToRgb(hue, sat, lum, out nr, out ng, out nb);
            return true;
        }

        return false;
    }

    private static float ShortestAngleDist(float a, float b)
    {
        float d = MathF.Abs(a - b) % 360f;
        return d > 180f ? 360f - d : d;
    }

    private static void RgbToHsl(float r, float g, float b, out float h, out float s, out float l)
    {
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        l = (max + min) * 0.5f;

        if (MathF.Abs(max - min) < 0.0001f)
        {
            h = 0f;
            s = 0f;
            return;
        }

        float d = max - min;
        s = l > 0.5f ? d / (2f - max - min) : d / (max + min);

        if (max == r)
            h = ((g - b) / d + (g < b ? 6f : 0f)) * 60f;
        else if (max == g)
            h = ((b - r) / d + 2f) * 60f;
        else
            h = ((r - g) / d + 4f) * 60f;
    }

    private static void HslToRgb(float h, float s, float l, out float r, out float g, out float b)
    {
        if (s <= 0.0001f)
        {
            r = g = b = l;
            return;
        }

        float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
        float p = 2f * l - q;
        float hk = h / 360f;

        r = HueToRgb(p, q, hk + 1f / 3f);
        g = HueToRgb(p, q, hk);
        b = HueToRgb(p, q, hk - 1f / 3f);
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0f) t += 1f;
        if (t > 1f) t -= 1f;
        if (t < 1f / 6f) return p + (q - p) * 6f * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
        return p;
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

    private static byte[] BuildIdentityTable()
    {
        byte[] table = new byte[256];
        for (int i = 0; i < 256; i++)
            table[i] = (byte)i;
        return table;
    }

    /// <summary>
    /// Fast (&lt; 0.45ms) 160x90 subsampled scope calculation for RGB Parade, Vectorscope, Luma Waveform, and RGB Histogram.
    /// </summary>
    public static ScopeRenderData SampleScopeData(
        SKBitmap? bitmap,
        SKColorFilter? colorFilter = null)
    {
        var data = new ScopeRenderData();
        if (bitmap == null || bitmap.Width <= 4 || bitmap.Height <= 4)
            return data;

        if (colorFilter != null || bitmap.ColorType == SKColorType.RgbaF16)
        {
            using var tiny = new SKBitmap(160, 90, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var tinyCanvas = new SKCanvas(tiny);
            using var paint = new SKPaint { ColorFilter = colorFilter };
            tinyCanvas.DrawBitmap(bitmap, new SKRect(0, 0, 160, 90), paint);
            PopulateScopeDataUnsafe(tiny, data);
            return data;
        }

        PopulateScopeDataUnsafe(bitmap, data);
        return data;
    }

    public static ScopeRenderData ComputeScopeData(
        SKBitmap? bitmap,
        SKColorFilter? colorFilter = null)
        => SampleScopeData(bitmap, colorFilter);

    public static (int[] R, int[] G, int[] B, int[] Luma) SampleScopeHistograms(
        SKBitmap? bitmap,
        SKColorFilter? colorFilter = null)
    {
        var d = SampleScopeData(bitmap, colorFilter);
        return (d.R, d.G, d.B, d.Luma);
    }

    private static unsafe void PopulateScopeDataUnsafe(SKBitmap bitmap, ScopeRenderData data)
    {
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

                data.R[r >> 2]++;
                data.G[g >> 2]++;
                data.B[b >> 2]++;

                int luma = (r * 54 + g * 183 + b * 19) >> 8;
                int lumaBin64 = Math.Clamp(luma >> 2, 0, 63);
                data.Luma[lumaBin64]++;

                // Parade & Luma Waveform column (0..47) and vertical IRE bin (0..47)
                int col48 = Math.Clamp((x * 48) / Math.Max(1, w), 0, 47);
                int rIre = Math.Clamp((r * 47) / 255, 0, 47);
                int gIre = Math.Clamp((g * 47) / 255, 0, 47);
                int bIre = Math.Clamp((b * 47) / 255, 0, 47);
                int lIre = Math.Clamp((luma * 47) / 255, 0, 47);

                data.ParadeR[col48, rIre]++;
                data.ParadeG[col48, gIre]++;
                data.ParadeB[col48, bIre]++;
                data.WaveformLuma[col48, lIre]++;

                // Rec.709 Chrominance Cb / Cr for Vectorscope (64x64)
                float rf = r / 255f;
                float gf = g / 255f;
                float bf = b / 255f;
                float cb = -0.1146f * rf - 0.3854f * gf + 0.5000f * bf; // [-0.5 .. +0.5]
                float cr =  0.5000f * rf - 0.4542f * gf - 0.0458f * bf; // [-0.5 .. +0.5]

                int vx = Math.Clamp((int)(32f + cb * 58f), 0, 63);
                int vy = Math.Clamp((int)(32f - cr * 58f), 0, 63);
                data.Vectorscope[vx, vy]++;
            }
        }
    }
}
