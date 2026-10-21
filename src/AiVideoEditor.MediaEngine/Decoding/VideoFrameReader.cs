using AiVideoEditor.Core.Models;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;
using OpenCvSharp;
using SkiaSharp;

namespace AiVideoEditor.MediaEngine.Decoding;

public sealed class VideoFrameReader : IDisposable
{
    private readonly int _maxCacheFrames;
    private readonly Dictionary<string, VideoCapture> _cvCaptures = new();
    private readonly Dictionary<string, int> _cvLastFramePos = new();
    private readonly Dictionary<string, MediaFile> _ffmpegFiles = new();
    private readonly Dictionary<string, SKBitmap> _imageCache = new();
    private readonly Dictionary<(string Path, int FrameIndex), LinkedListNode<(string Path, int FrameIndex, SKBitmap Bitmap)>> _lruMap = new();
    private readonly LinkedList<(string Path, int FrameIndex, SKBitmap Bitmap)> _lruList = new();
    private readonly object _syncLock = new();
    private int _isPrefetching;

    public VideoFrameReader(int maxCacheFrames = 96)
    {
        _maxCacheFrames = maxCacheFrames;
        FfmpegBootstrapper.Initialize();
    }

    public (double Duration, int Width, int Height, double Fps, bool HasVideo, bool HasAudio) ProbeMediaDetailed(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return (0.0, 1920, 1080, 30.0, false, false);

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp")
        {
            using var bmp = SKBitmap.Decode(filePath);
            if (bmp != null)
                return (5.0, bmp.Width, bmp.Height, 30.0, true, false);
        }

        bool hasAudio = false;
        double audioDuration = 0.0;
        try
        {
            using var audioReader = new NAudio.Wave.AudioFileReader(filePath);
            if (audioReader.TotalTime.TotalSeconds > 0)
            {
                hasAudio = true;
                audioDuration = audioReader.TotalTime.TotalSeconds;
            }
        }
        catch { }

        if (ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg")
        {
            return (Math.Max(0.5, audioDuration), 0, 0, 30.0, false, true);
        }

        try
        {
            using var cap = new VideoCapture(filePath);
            if (cap.IsOpened())
            {
                int w = Math.Max(1, cap.FrameWidth);
                int h = Math.Max(1, cap.FrameHeight);
                double fps = cap.Fps > 1.0 && cap.Fps < 240.0 ? cap.Fps : 30.0;
                int frameCount = cap.FrameCount;
                double cvDur = frameCount > 0 ? frameCount / fps : 0.0;
                double finalDur = cvDur > 0.1 ? cvDur : (audioDuration > 0.1 ? audioDuration : 5.0);
                return (finalDur, w, h, Math.Round(fps, 3), true, hasAudio);
            }
        }
        catch { }

        if (FfmpegBootstrapper.IsAvailable)
        {
            try
            {
                using var file = MediaFile.Open(filePath);
                if (file.HasVideo)
                {
                    var info = file.Video.Info;
                    double fps = info.AvgFrameRate > 0 ? info.AvgFrameRate : 30.0;
                    double dur = info.Duration.TotalSeconds > 0.1 ? info.Duration.TotalSeconds : audioDuration;
                    return (Math.Max(0.5, dur), info.FrameSize.Width, info.FrameSize.Height, fps, true, file.HasAudio || hasAudio);
                }
            }
            catch { }
        }

        return (Math.Max(1.0, audioDuration), 1920, 1080, 30.0, true, hasAudio);
    }

    public (double Duration, int Width, int Height, double Fps) ProbeMedia(string filePath)
    {
        var (dur, w, h, fps, _, _) = ProbeMediaDetailed(filePath);
        return (dur, w, h, fps);
    }

    public SKBitmap? GetFrame(MediaAsset asset, double sourceTimeSeconds, int targetWidth = 960, int targetHeight = 540)
    {
        if (string.IsNullOrWhiteSpace(asset.FilePath) || !File.Exists(asset.FilePath))
            return null;

        double fps = asset.Fps > 0.1 ? asset.Fps : 30.0;
        double clampedTime = Math.Clamp(sourceTimeSeconds, 0.0, Math.Max(0.0, asset.Duration - (1.0 / fps)));
        int frameIndex = Math.Max(0, (int)Math.Round(clampedTime * fps));
        var cacheKey = (asset.FilePath, frameIndex);

        lock (_syncLock)
        {
            if (asset.Type == MediaAssetType.Image)
            {
                if (!_imageCache.TryGetValue(asset.FilePath, out var imgBmp))
                {
                    imgBmp = SKBitmap.Decode(asset.FilePath);
                    if (imgBmp != null)
                        _imageCache[asset.FilePath] = imgBmp;
                }
                return imgBmp;
            }

            if (_lruMap.TryGetValue(cacheKey, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _lruList.AddFirst(existingNode);
                TriggerAsyncPrefetch(asset, frameIndex, targetWidth, targetHeight);
                return existingNode.Value.Bitmap;
            }

            SKBitmap? decodedBitmap = TryDecodeWithOpenCv(asset.FilePath, frameIndex, clampedTime, targetWidth, targetHeight)
                                      ?? TryDecodeWithFFMediaToolkit(asset.FilePath, clampedTime);

            if (decodedBitmap == null)
                return null;

            StoreInCacheLocked(cacheKey, decodedBitmap);
            TriggerAsyncPrefetch(asset, frameIndex, targetWidth, targetHeight);
            return decodedBitmap;
        }
    }

    private void StoreInCacheLocked((string Path, int FrameIndex) cacheKey, SKBitmap decodedBitmap)
    {
        if (_lruMap.ContainsKey(cacheKey)) return;

        var newNode = new LinkedListNode<(string Path, int FrameIndex, SKBitmap Bitmap)>((cacheKey.Path, cacheKey.FrameIndex, decodedBitmap));
        _lruList.AddFirst(newNode);
        _lruMap[cacheKey] = newNode;

        while (_lruList.Count > _maxCacheFrames && _lruList.Last != null)
        {
            var oldest = _lruList.Last.Value;
            _lruList.RemoveLast();
            _lruMap.Remove((oldest.Path, oldest.FrameIndex));
            oldest.Bitmap.Dispose();
        }
    }

    /// <summary>
    /// Background prefetching ahead of the playhead so 60 FPS playback hits the in-memory ring cache.
    /// </summary>
    private void TriggerAsyncPrefetch(MediaAsset asset, int currentFrameIdx, int targetW, int targetH)
    {
        if (Interlocked.CompareExchange(ref _isPrefetching, 1, 0) != 0)
            return;

        double fps = asset.Fps > 0.1 ? asset.Fps : 30.0;
        int maxFrame = Math.Max(1, (int)(asset.Duration * fps) - 1);
        string path = asset.FilePath;

        Task.Run(() =>
        {
            try
            {
                for (int step = 1; step <= 3; step++)
                {
                    int nextFrame = currentFrameIdx + step;
                    if (nextFrame > maxFrame) break;

                    lock (_syncLock)
                    {
                        if (_lruMap.ContainsKey((path, nextFrame)))
                            continue;

                        double tSec = nextFrame / fps;
                        var bmp = TryDecodeWithOpenCv(path, nextFrame, tSec, targetW, targetH);
                        if (bmp != null)
                        {
                            StoreInCacheLocked((path, nextFrame), bmp);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                Interlocked.Exchange(ref _isPrefetching, 0);
            }
        });
    }

    private unsafe SKBitmap? TryDecodeWithOpenCv(string filePath, int targetFrameIndex, double timestampSeconds, int targetWidth, int targetHeight)
    {
        try
        {
            if (!_cvCaptures.TryGetValue(filePath, out var capture) || !capture.IsOpened())
            {
                capture?.Dispose();
                capture = new VideoCapture(filePath);
                if (!capture.IsOpened())
                {
                    capture.Dispose();
                    return null;
                }
                _cvCaptures[filePath] = capture;
                _cvLastFramePos[filePath] = -999;
            }

            int lastPos = _cvLastFramePos.GetValueOrDefault(filePath, -999);
            int delta = targetFrameIndex - lastPos;

            if (delta < 0 || delta > 12)
            {
                capture.Set(VideoCaptureProperties.PosFrames, targetFrameIndex);
            }
            else if (delta > 1)
            {
                for (int skip = 0; skip < delta - 1; skip++)
                {
                    capture.Grab();
                }
            }

            using var rawMat = new Mat();
            if (!capture.Read(rawMat) || rawMat.Empty())
            {
                capture.Set(VideoCaptureProperties.PosMsec, timestampSeconds * 1000.0);
                if (!capture.Read(rawMat) || rawMat.Empty())
                    return null;
            }

            _cvLastFramePos[filePath] = targetFrameIndex;

            int srcW = rawMat.Width;
            int srcH = rawMat.Height;
            if (srcW <= 0 || srcH <= 0) return null;

            Mat sourceForColor = rawMat;
            Mat? resizedMat = null;

            if (targetWidth > 0 && targetHeight > 0 && (srcW > targetWidth * 1.35 || srcH > targetHeight * 1.35))
            {
                double scale = Math.Min((double)targetWidth / srcW, (double)targetHeight / srcH);
                int outW = Math.Max(2, (int)Math.Round(srcW * scale));
                int outH = Math.Max(2, (int)Math.Round(srcH * scale));
                resizedMat = new Mat();
                Cv2.Resize(rawMat, resizedMat, new Size(outW, outH), 0, 0, InterpolationFlags.Linear);
                sourceForColor = resizedMat;
            }

            try
            {
                int finalW = sourceForColor.Width;
                int finalH = sourceForColor.Height;
                var skBitmap = new SKBitmap(finalW, finalH, SKColorType.Bgra8888, SKAlphaType.Premul);

                using var bgraMat = Mat.FromPixelData(finalH, finalW, MatType.CV_8UC4, skBitmap.GetPixels(), skBitmap.RowBytes);
                Cv2.CvtColor(sourceForColor, bgraMat, ColorConversionCodes.BGR2BGRA);

                return skBitmap;
            }
            finally
            {
                resizedMat?.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    private unsafe SKBitmap? TryDecodeWithFFMediaToolkit(string filePath, double timestampSeconds)
    {
        if (!FfmpegBootstrapper.IsAvailable)
            return null;

        try
        {
            if (!_ffmpegFiles.TryGetValue(filePath, out var mediaFile))
            {
                mediaFile = MediaFile.Open(filePath, new MediaOptions
                {
                    StreamsToLoad = MediaMode.Video,
                    VideoPixelFormat = ImagePixelFormat.Bgra32
                });
                _ffmpegFiles[filePath] = mediaFile;
            }

            if (!mediaFile.HasVideo)
                return null;

            var targetTs = TimeSpan.FromSeconds(Math.Max(0, timestampSeconds));
            var imageData = mediaFile.Video.GetFrame(targetTs);

            int w = imageData.ImageSize.Width;
            int h = imageData.ImageSize.Height;
            var bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);

            IntPtr dstPtr = bitmap.GetPixels();
            fixed (byte* srcPtr = imageData.Data)
            {
                for (int y = 0; y < h; y++)
                {
                    Buffer.MemoryCopy(
                        srcPtr + (y * imageData.Stride),
                        (byte*)dstPtr + (y * bitmap.RowBytes),
                        bitmap.RowBytes,
                        Math.Min(imageData.Stride, bitmap.RowBytes));
                }
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            foreach (var cap in _cvCaptures.Values)
            {
                try { cap.Dispose(); } catch { }
            }
            _cvCaptures.Clear();
            _cvLastFramePos.Clear();

            foreach (var mf in _ffmpegFiles.Values)
            {
                try { mf.Dispose(); } catch { }
            }
            _ffmpegFiles.Clear();

            foreach (var img in _imageCache.Values)
            {
                img.Dispose();
            }
            _imageCache.Clear();

            foreach (var node in _lruList)
            {
                node.Bitmap.Dispose();
            }
            _lruList.Clear();
            _lruMap.Clear();
        }
    }
}
