using AiVideoEditor.Core.Models;
using SkiaSharp;

namespace AiVideoEditor.MediaEngine.Decoding;

public static class ThumbnailStripService
{
    private static readonly Dictionary<Guid, List<SKBitmap>> FilmstripCache = new();
    private static readonly object CacheLock = new();

    public static string GetThumbnailsFolder()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VortexCutStudio", "Cache", "Thumbnails");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string EnsurePosterThumbnail(MediaAsset asset, VideoFrameReader frameReader)
    {
        try
        {
            string outPath = Path.Combine(GetThumbnailsFolder(), $"{asset.AssetId}_poster.jpg");
            if (File.Exists(outPath))
            {
                asset.PosterThumbnailPath = outPath;
                return outPath;
            }

            var bmp = frameReader.GetFrame(asset, Math.Min(1.0, asset.Duration * 0.15), 320, 180);
            if (bmp == null) return string.Empty;

            using var image = SKImage.FromBitmap(bmp);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 80);
            using var fs = File.Create(outPath);
            encoded.SaveTo(fs);
            asset.PosterThumbnailPath = outPath;
            return outPath;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static IReadOnlyList<SKBitmap> GetOrBuildFilmstrip(MediaAsset asset, VideoFrameReader frameReader, int count = 6)
    {
        lock (CacheLock)
        {
            if (FilmstripCache.TryGetValue(asset.Id, out var existing) && existing.Count > 0)
                return existing;

            var list = new List<SKBitmap>();
            if (!asset.HasVideo || string.IsNullOrWhiteSpace(asset.FilePath) || !File.Exists(asset.FilePath))
                return list;

            double step = Math.Max(0.5, asset.Duration / Math.Max(1, count));
            for (int i = 0; i < count; i++)
            {
                double t = Math.Min(asset.Duration - 0.1, i * step + 0.1);
                var frame = frameReader.GetFrame(asset, Math.Max(0, t), 160, 90);
                if (frame != null)
                {
                    var thumb = new SKBitmap(96, 54, SKColorType.Bgra8888, SKAlphaType.Premul);
                    using var canvas = new SKCanvas(thumb);
                    canvas.DrawBitmap(frame, new SKRect(0, 0, 96, 54));
                    list.Add(thumb);
                }
            }

            if (list.Count > 0)
                FilmstripCache[asset.Id] = list;

            return list;
        }
    }
}
