using System;
using System.Collections.Generic;
using System.Linq;
using AiVideoEditor.Core.Models;

namespace AiVideoEditor.Core.Services;

/// <summary>
/// Fritsch-Carlson Monotonic Cubic Hermite Spline Solver.
/// Eliminates looping, overshoot, ringing, and solarization artifacts by guaranteeing
/// strict monotonicity between interpolation nodes and continuous C1 smooth tangents.
/// </summary>
public static class MonotonicCubicCurveSolver
{
    /// <summary>
    /// Solves the normalized [0..1] curve at a given x in [0..1].
    /// </summary>
    public static float Evaluate(List<CurveControlPoint>? rawPoints, float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        if (rawPoints == null || rawPoints.Count == 0)
            return x;

        var pts = SanitizePoints(rawPoints);
        if (pts.Count == 1)
            return pts[0].Y;

        int n = pts.Count;
        float[] xs = new float[n];
        float[] ys = new float[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = pts[i].X;
            ys[i] = pts[i].Y;
        }

        float[] d = ComputeMonotonicTangents(xs, ys);

        if (x <= xs[0]) return ys[0];
        if (x >= xs[n - 1]) return ys[n - 1];

        int seg = 0;
        for (int k = 0; k < n - 1; k++)
        {
            if (x >= xs[k] && x <= xs[k + 1])
            {
                seg = k;
                break;
            }
        }

        return HermiteInterpolate(xs, ys, d, seg, x);
    }

    /// <summary>
    /// Computes a 256-entry float Lookup Table (LUT) with values normalized to [0..1].
    /// </summary>
    public static float[] SolveLut01(List<CurveControlPoint>? rawPoints)
    {
        float[] lut = new float[256];
        if (rawPoints == null || rawPoints.Count == 0)
        {
            for (int i = 0; i < 256; i++) lut[i] = i / 255f;
            return lut;
        }

        var pts = SanitizePoints(rawPoints);
        if (pts.Count == 1)
        {
            float v = pts[0].Y;
            for (int i = 0; i < 256; i++) lut[i] = v;
            return lut;
        }

        int n = pts.Count;
        float[] xs = new float[n];
        float[] ys = new float[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = pts[i].X;
            ys[i] = pts[i].Y;
        }

        float[] d = ComputeMonotonicTangents(xs, ys);

        int seg = 0;
        for (int i = 0; i < 256; i++)
        {
            float x = i / 255f;
            if (x <= xs[0])
            {
                lut[i] = ys[0];
                continue;
            }
            if (x >= xs[n - 1])
            {
                lut[i] = ys[n - 1];
                continue;
            }

            while (seg < n - 2 && x > xs[seg + 1])
            {
                seg++;
            }

            lut[i] = HermiteInterpolate(xs, ys, d, seg, x);
        }

        return lut;
    }

    /// <summary>
    /// Computes a 256-entry byte Lookup Table (LUT) with values mapped to [0..255].
    /// </summary>
    public static byte[] SolveLut256(List<CurveControlPoint>? rawPoints)
    {
        float[] lut01 = SolveLut01(rawPoints);
        byte[] bytes = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            bytes[i] = (byte)Math.Clamp((int)MathF.Round(lut01[i] * 255f), 0, 255);
        }
        return bytes;
    }

    /// <summary>
    /// Combines Master and Individual Channel curves using proper DaVinci/Premiere curve chaining:
    /// FinalR[i] = MasterLut[RedLut[i]], FinalG[i] = MasterLut[GreenLut[i]], FinalB[i] = MasterLut[BlueLut[i]].
    /// </summary>
    public static (byte[] R, byte[] G, byte[] B) BuildCompositeRgbLut(
        List<CurveControlPoint>? master,
        List<CurveControlPoint>? red,
        List<CurveControlPoint>? green,
        List<CurveControlPoint>? blue)
    {
        byte[] masterLut = SolveLut256(master);
        byte[] redLut = SolveLut256(red);
        byte[] greenLut = SolveLut256(green);
        byte[] blueLut = SolveLut256(blue);

        byte[] finalR = new byte[256];
        byte[] finalG = new byte[256];
        byte[] finalB = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            finalR[i] = masterLut[redLut[i]];
            finalG[i] = masterLut[greenLut[i]];
            finalB[i] = masterLut[blueLut[i]];
        }

        return (finalR, finalG, finalB);
    }

    private static List<CurveControlPoint> SanitizePoints(List<CurveControlPoint> raw)
    {
        var sorted = raw
            .Select(p => new CurveControlPoint(Math.Clamp(p.X, 0f, 1f), Math.Clamp(p.Y, 0f, 1f)))
            .OrderBy(p => p.X)
            .ToList();

        var clean = new List<CurveControlPoint>(sorted.Count);
        foreach (var pt in sorted)
        {
            if (clean.Count > 0 && Math.Abs(clean[^1].X - pt.X) < 0.0001f)
            {
                // If two points share virtually the same X, keep the latest one
                clean[^1] = pt;
            }
            else
            {
                clean.Add(pt);
            }
        }

        if (clean.Count == 0)
        {
            clean.Add(new CurveControlPoint(0f, 0f));
            clean.Add(new CurveControlPoint(1f, 1f));
        }
        else if (clean.Count == 1)
        {
            clean.Add(new CurveControlPoint(1f, 1f));
        }

        return clean;
    }

    private static float[] ComputeMonotonicTangents(float[] xs, float[] ys)
    {
        int n = xs.Length;
        float[] delta = new float[n - 1];
        for (int k = 0; k < n - 1; k++)
        {
            float dx = Math.Max(0.0001f, xs[k + 1] - xs[k]);
            delta[k] = (ys[k + 1] - ys[k]) / dx;
        }

        float[] d = new float[n];
        d[0] = delta[0];
        d[n - 1] = delta[n - 2];

        for (int k = 1; k < n - 1; k++)
        {
            d[k] = (delta[k - 1] + delta[k]) * 0.5f;
        }

        // Fritsch-Carlson pass to enforce strict monotonicity
        for (int k = 0; k < n - 1; k++)
        {
            if (Math.Abs(delta[k]) < 1e-6f)
            {
                d[k] = 0f;
                d[k + 1] = 0f;
            }
            else
            {
                float alpha = d[k] / delta[k];
                float beta = d[k + 1] / delta[k];

                if (alpha < 0f) d[k] = 0f;
                if (beta < 0f) d[k + 1] = 0f;

                float hypSq = alpha * alpha + beta * beta;
                if (hypSq > 9f)
                {
                    float tau = 3f / MathF.Sqrt(hypSq);
                    d[k] = tau * alpha * delta[k];
                    d[k + 1] = tau * beta * delta[k];
                }
            }
        }

        return d;
    }

    private static float HermiteInterpolate(float[] xs, float[] ys, float[] d, int seg, float x)
    {
        float h = Math.Max(0.0001f, xs[seg + 1] - xs[seg]);
        float t = Math.Clamp((x - xs[seg]) / h, 0f, 1f);
        float t2 = t * t;
        float t3 = t2 * t;

        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        float y = h00 * ys[seg] + h10 * h * d[seg] + h01 * ys[seg + 1] + h11 * h * d[seg + 1];
        return Math.Clamp(y, 0f, 1f);
    }
}
