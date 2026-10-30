using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace AiVideoEditor.MediaEngine.Shaders;

public sealed class CubeLutData
{
    public string Title { get; init; } = "Untitled LUT";
    public string FilePath { get; init; } = string.Empty;
    public bool Is3D { get; init; }
    public int Size { get; init; }

    public float DomainMinR { get; init; } = 0.0f;
    public float DomainMinG { get; init; } = 0.0f;
    public float DomainMinB { get; init; } = 0.0f;

    public float DomainMaxR { get; init; } = 1.0f;
    public float DomainMaxG { get; init; } = 1.0f;
    public float DomainMaxB { get; init; } = 1.0f;

    /// <summary>
    /// Flat interleaved float array [R0, G0, B0, R1, G1, B1, ...].
    /// For 3D LUTs: index = ((b * Size + g) * Size + r) * 3 (Red changes fastest per Adobe .cube specification).
    /// For 1D LUTs: index = i * 3.
    /// </summary>
    public float[] Table { get; init; } = Array.Empty<float>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Evaluate(float r, float g, float b, out float outR, out float outG, out float outB)
    {
        if (Is3D)
        {
            Sample3DTrilinear(r, g, b, out outR, out outG, out outB);
        }
        else
        {
            Sample1DLinear(r, g, b, out outR, out outG, out outB);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Sample1DLinear(float r, float g, float b, out float outR, out float outG, out float outB)
    {
        int n = Size;
        if (n < 2 || Table.Length < n * 3)
        {
            outR = r;
            outG = g;
            outB = b;
            return;
        }

        float scaleR = DomainMaxR > DomainMinR ? (r - DomainMinR) / (DomainMaxR - DomainMinR) : r;
        float scaleG = DomainMaxG > DomainMinG ? (g - DomainMinG) / (DomainMaxG - DomainMinG) : g;
        float scaleB = DomainMaxB > DomainMinB ? (b - DomainMinB) / (DomainMaxB - DomainMinB) : b;

        float maxIdx = n - 1;
        float fr = Math.Clamp(scaleR, 0f, 1f) * maxIdx;
        float fg = Math.Clamp(scaleG, 0f, 1f) * maxIdx;
        float fb = Math.Clamp(scaleB, 0f, 1f) * maxIdx;

        int r0 = (int)fr;
        int r1 = Math.Min(r0 + 1, n - 1);
        float tr = fr - r0;

        int g0 = (int)fg;
        int g1 = Math.Min(g0 + 1, n - 1);
        float tg = fg - g0;

        int b0 = (int)fb;
        int b1 = Math.Min(b0 + 1, n - 1);
        float tb = fb - b0;

        outR = Table[r0 * 3] + (Table[r1 * 3] - Table[r0 * 3]) * tr;
        outG = Table[g0 * 3 + 1] + (Table[g1 * 3 + 1] - Table[g0 * 3 + 1]) * tg;
        outB = Table[b0 * 3 + 2] + (Table[b1 * 3 + 2] - Table[b0 * 3 + 2]) * tb;
    }

    /// <summary>
    /// High-precision trilinear interpolation across the N x N x N 3D LUT lattice.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Sample3DTrilinear(float r, float g, float b, out float outR, out float outG, out float outB)
    {
        int n = Size;
        if (n < 2 || Table.Length < n * n * n * 3)
        {
            outR = r;
            outG = g;
            outB = b;
            return;
        }

        float normR = DomainMaxR > DomainMinR ? (r - DomainMinR) / (DomainMaxR - DomainMinR) : r;
        float normG = DomainMaxG > DomainMinG ? (g - DomainMinG) / (DomainMaxG - DomainMinG) : g;
        float normB = DomainMaxB > DomainMinB ? (b - DomainMinB) / (DomainMaxB - DomainMinB) : b;

        float maxIdx = n - 1;
        float fr = Math.Clamp(normR, 0f, 1f) * maxIdx;
        float fg = Math.Clamp(normG, 0f, 1f) * maxIdx;
        float fb = Math.Clamp(normB, 0f, 1f) * maxIdx;

        int r0 = (int)fr;
        int g0 = (int)fg;
        int b0 = (int)fb;

        int r1 = r0 < n - 1 ? r0 + 1 : r0;
        int g1 = g0 < n - 1 ? g0 + 1 : g0;
        int b1 = b0 < n - 1 ? b0 + 1 : b0;

        float dr = fr - r0;
        float dg = fg - g0;
        float db = fb - b0;

        int strideG = n * 3;
        int strideB = n * strideG;

        int base000 = b0 * strideB + g0 * strideG + r0 * 3;
        int base100 = b0 * strideB + g0 * strideG + r1 * 3;
        int base010 = b0 * strideB + g1 * strideG + r0 * 3;
        int base110 = b0 * strideB + g1 * strideG + r1 * 3;

        int base001 = b1 * strideB + g0 * strideG + r0 * 3;
        int base101 = b1 * strideB + g0 * strideG + r1 * 3;
        int base011 = b1 * strideB + g1 * strideG + r0 * 3;
        int base111 = b1 * strideB + g1 * strideG + r1 * 3;

        float[] tbl = Table;

        // Interpolate along R
        float c00R = tbl[base000] + (tbl[base100] - tbl[base000]) * dr;
        float c00G = tbl[base000 + 1] + (tbl[base100 + 1] - tbl[base000 + 1]) * dr;
        float c00B = tbl[base000 + 2] + (tbl[base100 + 2] - tbl[base000 + 2]) * dr;

        float c10R = tbl[base010] + (tbl[base110] - tbl[base010]) * dr;
        float c10G = tbl[base010 + 1] + (tbl[base110 + 1] - tbl[base010 + 1]) * dr;
        float c10B = tbl[base010 + 2] + (tbl[base110 + 2] - tbl[base010 + 2]) * dr;

        float c01R = tbl[base001] + (tbl[base101] - tbl[base001]) * dr;
        float c01G = tbl[base001 + 1] + (tbl[base101 + 1] - tbl[base001 + 1]) * dr;
        float c01B = tbl[base001 + 2] + (tbl[base101 + 2] - tbl[base001 + 2]) * dr;

        float c11R = tbl[base011] + (tbl[base111] - tbl[base011]) * dr;
        float c11G = tbl[base011 + 1] + (tbl[base111 + 1] - tbl[base011 + 1]) * dr;
        float c11B = tbl[base011 + 2] + (tbl[base111 + 2] - tbl[base011 + 2]) * dr;

        // Interpolate along G
        float c0R = c00R + (c10R - c00R) * dg;
        float c0G = c00G + (c10G - c00G) * dg;
        float c0B = c00B + (c10B - c00B) * dg;

        float c1R = c01R + (c11R - c01R) * dg;
        float c1G = c01G + (c11G - c01G) * dg;
        float c1B = c01B + (c11B - c01B) * dg;

        // Interpolate along B
        outR = c0R + (c1R - c0R) * db;
        outG = c0G + (c1G - c0G) * db;
        outB = c0B + (c1B - c0B) * db;
    }
}

public static class CubeLutParser
{
    private static readonly ConcurrentDictionary<string, (DateTime LastWriteUtc, CubeLutData Lut)> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static CubeLutData? GetOrLoad(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            string fullPath = Path.GetFullPath(filePath);
            DateTime lastWrite = File.GetLastWriteTimeUtc(fullPath);

            if (Cache.TryGetValue(fullPath, out var cached) && cached.LastWriteUtc == lastWrite)
            {
                return cached.Lut;
            }

            var parsed = ParseFile(fullPath);
            if (parsed != null)
            {
                Cache[fullPath] = (lastWrite, parsed);
            }
            return parsed;
        }
        catch
        {
            return null;
        }
    }

    public static CubeLutData? ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        string content = File.ReadAllText(filePath);
        return ParseContent(content, filePath);
    }

    public static CubeLutData? ParseContent(string cubeContent, string sourceFilePath = "")
    {
        if (string.IsNullOrWhiteSpace(cubeContent))
            return null;

        string title = !string.IsNullOrWhiteSpace(sourceFilePath)
            ? Path.GetFileNameWithoutExtension(sourceFilePath)
            : "Custom LUT";

        bool is3D = true;
        int size = 0;

        float minR = 0f, minG = 0f, minB = 0f;
        float maxR = 1f, maxG = 1f, maxB = 1f;

        float[]? table = null;
        int writeIdx = 0;

        using var reader = new StringReader(cubeContent);
        string? rawLine;
        while ((rawLine = reader.ReadLine()) != null)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            int commentPos = line.IndexOf('#');
            if (commentPos >= 0)
            {
                line = line[..commentPos].Trim();
                if (line.Length == 0)
                    continue;
            }

            if (line.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
            {
                int firstQuote = line.IndexOf('"');
                int lastQuote = line.LastIndexOf('"');
                if (firstQuote >= 0 && lastQuote > firstQuote)
                {
                    title = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                }
                else if (line.Length > 5)
                {
                    title = line[5..].Trim();
                }
                continue;
            }

            if (line.StartsWith("LUT_3D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 2 && n <= 128)
                {
                    is3D = true;
                    size = n;
                    table = new float[n * n * n * 3];
                    writeIdx = 0;
                }
                continue;
            }

            if (line.StartsWith("LUT_1D_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 2 && n <= 65536)
                {
                    is3D = false;
                    size = n;
                    table = new float[n * 3];
                    writeIdx = 0;
                }
                continue;
            }

            if (line.StartsWith("DOMAIN_MIN", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                {
                    minR = r;
                    minG = g;
                    minB = b;
                }
                continue;
            }

            if (line.StartsWith("DOMAIN_MAX", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                {
                    maxR = r;
                    maxG = g;
                    maxB = b;
                }
                continue;
            }

            if (line.StartsWith("LUT_", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("DOMAIN_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (table != null && writeIdx + 2 < table.Length)
            {
                if (TryParseTriplet(line, out float r, out float g, out float b))
                {
                    table[writeIdx++] = r;
                    table[writeIdx++] = g;
                    table[writeIdx++] = b;
                }
            }
        }

        if (size < 2 || table == null || writeIdx < table.Length)
            return null;

        return new CubeLutData
        {
            Title = title,
            FilePath = sourceFilePath,
            Is3D = is3D,
            Size = size,
            DomainMinR = minR,
            DomainMinG = minG,
            DomainMinB = minB,
            DomainMaxR = maxR,
            DomainMaxG = maxG,
            DomainMaxB = maxB,
            Table = table
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseTriplet(ReadOnlySpan<char> line, out float r, out float g, out float b)
    {
        r = g = b = 0f;
        line = line.Trim();
        int idx1 = line.IndexOfAny(' ', '\t');
        if (idx1 <= 0) return false;

        var first = line[..idx1];
        var rest1 = line[(idx1 + 1)..].TrimStart();
        int idx2 = rest1.IndexOfAny(' ', '\t');
        if (idx2 <= 0) return false;

        var second = rest1[..idx2];
        var third = rest1[(idx2 + 1)..].TrimStart();
        int idx3 = third.IndexOfAny(' ', '\t');
        if (idx3 > 0) third = third[..idx3];

        return float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out r) &&
               float.TryParse(second, NumberStyles.Float, CultureInfo.InvariantCulture, out g) &&
               float.TryParse(third, NumberStyles.Float, CultureInfo.InvariantCulture, out b);
    }
}
