using System.Diagnostics;
using System.Runtime.InteropServices;
using AiVideoEditor.Core.Interfaces;
using AiVideoEditor.Core.Models;
using AiVideoEditor.MediaEngine.Audio;
using AiVideoEditor.MediaEngine.Decoding;
using AiVideoEditor.MediaEngine.Rendering;
using FFMediaToolkit.Encoding;
using FFMediaToolkit.Graphics;
using OpenCvSharp;
using SkiaSharp;

namespace AiVideoEditor.MediaEngine.Exporting;

/// <summary>
/// Production Hardware-Accelerated FFmpeg &amp; Native Export Engine (Section 4).
/// Supports NVENC, Intel QSV, AMD AMF, libx264, libx265, Apple ProRes 422, and Audio-Only WAV/MP3.
/// </summary>
public sealed class FFmpegExportEngine : IVideoRenderEngine
{
    private readonly CompositorEngine _compositor;
    private readonly MultiTrackAudioPlayer _audioPlayer;
    private static string? _cachedFfmpegExe;
    private static List<string>? _cachedSupportedEncoders;

    public FFmpegExportEngine(CompositorEngine compositor, MultiTrackAudioPlayer audioPlayer)
    {
        _compositor = compositor;
        _audioPlayer = audioPlayer;
    }

    public static string? ResolveFfmpegExecutable()
    {
        if (!string.IsNullOrEmpty(_cachedFfmpegExe) && File.Exists(_cachedFfmpegExe))
            return _cachedFfmpegExe;

        string baseDir = AppContext.BaseDirectory;
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var candidates = new List<string>
        {
            Path.Combine(baseDir, "ffmpeg.exe"),
            Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe"),
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\ProgramData\chocolatey\bin\ffmpeg.exe",
            Path.Combine(userProfile, "scoop", "shims", "ffmpeg.exe")
        };

        // Check Winget packages folder for Gyan.FFmpeg / BtbN.FFmpeg
        string wingetPkgs = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetPkgs))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(wingetPkgs, "ffmpeg.exe", SearchOption.AllDirectories))
                {
                    candidates.Add(f);
                }
            }
            catch { }
        }

        // Check PATH environment variable directories directly
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string p = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    candidates.Add(p);
                }
                catch { }
            }
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                _cachedFfmpegExe = candidate;
                return candidate;
            }
        }

        // Finally test bare "ffmpeg" on shell PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null && p.WaitForExit(2500) && p.ExitCode == 0)
            {
                _cachedFfmpegExe = "ffmpeg";
                return "ffmpeg";
            }
        }
        catch { }

        return null;
    }

    public static IReadOnlyList<string> ProbeAvailableEncoders()
    {
        if (_cachedSupportedEncoders != null)
            return _cachedSupportedEncoders;

        var list = new List<string>();
        string? ffmpeg = ResolveFfmpegExecutable();
        if (!string.IsNullOrEmpty(ffmpeg))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-hide_banner -encoders",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);

                    string[] checkCodes = { "h264_nvenc", "hevc_nvenc", "h264_qsv", "h264_amf", "libx264", "libx265", "prores_ks" };
                    foreach (var code in checkCodes)
                    {
                        if (output.Contains(code, StringComparison.OrdinalIgnoreCase))
                            list.Add(code);
                    }
                }
            }
            catch { }
        }

        if (list.Count == 0)
        {
            list.AddRange(new[] { "libx264", "h264_nvenc", "h264_qsv", "h264_amf", "libx265", "prores_ks" });
        }

        _cachedSupportedEncoders = list;
        return list;
    }

    public async Task ExportProjectAsync(
        ProjectDocument document,
        ExportSettings settings,
        IProgress<(double percent, string status, TimeSpan eta)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.OutputPath))
            throw new ArgumentException("Output path must be specified.", nameof(settings));

        string? outDir = Path.GetDirectoryName(settings.OutputPath);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        await Task.Run(async () =>
        {
            double startSec = 0.0;
            double endSec = Math.Max(1.0, document.TotalDuration);

            if (settings.UseMarkInOutRange)
            {
                startSec = Math.Max(0.0, settings.RangeStartSeconds ?? document.TimelineMarkIn ?? 0.0);
                double candidateEnd = settings.RangeEndSeconds ?? document.TimelineMarkOut ?? endSec;
                if (candidateEnd > startSec + 0.1)
                    endSec = candidateEnd;
            }

            double renderDuration = Math.Max(0.2, endSec - startSec);
            double fps = settings.Fps > 0 ? settings.Fps : (document.Fps > 0 ? document.Fps : 30.0);
            int totalFrames = Math.Max(1, (int)Math.Ceiling(renderDuration * fps));
            int width = settings.Width > 0 ? settings.Width : document.Width;
            int height = settings.Height > 0 ? settings.Height : document.Height;

            // Ensure even dimensions for YUV420p hardware encoders
            if (width % 2 != 0) width++;
            if (height % 2 != 0) height++;

            var sw = Stopwatch.StartNew();
            progress?.Report((2.0, "Stage 1/2: Mixing multi-track audio & DSP...", TimeSpan.FromSeconds(4)));

            string tempWav = Path.Combine(Path.GetTempPath(), $"vortex_master_{Guid.NewGuid():N}.wav");
            string tempVideoOnly = Path.Combine(Path.GetTempPath(), $"vortex_video_{Guid.NewGuid():N}.mp4");

            try
            {
                await _audioPlayer.MixdownToWavAsync(document, tempWav, cancellationToken);

                // Audio-Only Export Preset (.wav or .mp3)
                bool isAudioOnly = settings.AudioOnly ||
                                   settings.OutputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                   settings.OutputPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);

                if (isAudioOnly)
                {
                    string? ffmpegExe = ResolveFfmpegExecutable();
                    if (settings.OutputPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ffmpegExe))
                    {
                        RunFfmpegSimple($"-y -ss {startSec:0.###} -t {renderDuration:0.###} -i \"{tempWav}\" -c:a libmp3lame -b:a 320k \"{settings.OutputPath}\"");
                    }
                    if (!File.Exists(settings.OutputPath))
                    {
                        File.Copy(tempWav, settings.OutputPath, overwrite: true);
                    }
                    progress?.Report((100.0, $"Audio Export Complete: {Path.GetFileName(settings.OutputPath)}", TimeSpan.Zero));
                    return;
                }

                string requestedEncoder = NormalizeEncoderId(settings.HardwareEncoder);
                bool encodedSuccess = false;

                // Stage 2A: Primary Hardware/CPU FFmpeg RawVideo Pipe
                string? ffmpegBin = ResolveFfmpegExecutable();
                if (!string.IsNullOrEmpty(ffmpegBin))
                {
                    encodedSuccess = TryEncodeViaFfmpegRawPipe(
                        ffmpegBin, document, settings, tempWav,
                        startSec, width, height, fps, totalFrames,
                        requestedEncoder, sw, progress, cancellationToken);

                    // If GPU hardware encoder failed on this machine, fall back automatically to libx264 pipe
                    if (!encodedSuccess && requestedEncoder != "libx264")
                    {
                        encodedSuccess = TryEncodeViaFfmpegRawPipe(
                            ffmpegBin, document, settings, tempWav,
                            startSec, width, height, fps, totalFrames,
                            "libx264", sw, progress, cancellationToken);
                    }
                }

                // Stage 2B: Fallback to FFMediaToolkit or OpenCvSharp VideoWriter + Audio Mux
                if (!encodedSuccess)
                {
                    bool videoCreated = false;
                    if (FfmpegBootstrapper.IsAvailable)
                    {
                        videoCreated = TryEncodeWithFFMediaToolkit(
                            document, settings, tempVideoOnly, startSec,
                            width, height, (int)Math.Round(fps), totalFrames,
                            sw, progress, cancellationToken);
                    }

                    if (!videoCreated)
                    {
                        videoCreated = TryEncodeWithOpenCvVideoWriter(
                            document, settings, tempVideoOnly, startSec,
                            width, height, fps, totalFrames,
                            sw, progress, cancellationToken);
                    }

                    if (videoCreated && File.Exists(tempVideoOnly) && new FileInfo(tempVideoOnly).Length > 0)
                    {
                        progress?.Report((96.0, "Muxing video & master audio stream...", TimeSpan.FromSeconds(1)));
                        if (!string.IsNullOrEmpty(ffmpegBin) &&
                            RunFfmpegSimple($"-y -i \"{tempVideoOnly}\" -ss {startSec:0.###} -t {renderDuration:0.###} -i \"{tempWav}\" -c:v copy -c:a aac -b:a 256k -shortest \"{settings.OutputPath}\"") &&
                            File.Exists(settings.OutputPath))
                        {
                            encodedSuccess = true;
                        }
                        else
                        {
                            File.Copy(tempVideoOnly, settings.OutputPath, overwrite: true);
                            encodedSuccess = true;
                        }
                    }
                }

                if (!encodedSuccess || !File.Exists(settings.OutputPath))
                {
                    throw new InvalidOperationException("Video export pipeline could not initialize an encoder for the target path.");
                }

                progress?.Report((100.0, $"Export Complete ({totalFrames} frames) -> {Path.GetFileName(settings.OutputPath)}", TimeSpan.Zero));
            }
            finally
            {
                try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
                try { if (File.Exists(tempVideoOnly)) File.Delete(tempVideoOnly); } catch { }
            }
        }, cancellationToken);
    }

    private static string NormalizeEncoderId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "libx264";
        if (raw.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase)) return "h264_nvenc";
        if (raw.Contains("hevc_nvenc", StringComparison.OrdinalIgnoreCase)) return "hevc_nvenc";
        if (raw.Contains("h264_qsv", StringComparison.OrdinalIgnoreCase)) return "h264_qsv";
        if (raw.Contains("h264_amf", StringComparison.OrdinalIgnoreCase)) return "h264_amf";
        if (raw.Contains("libx265", StringComparison.OrdinalIgnoreCase)) return "libx265";
        if (raw.Contains("prores", StringComparison.OrdinalIgnoreCase)) return "prores_ks";
        return "libx264";
    }

    private bool TryEncodeViaFfmpegRawPipe(
        string ffmpegBin,
        ProjectDocument document,
        ExportSettings settings,
        string audioWavPath,
        double startSec,
        int width,
        int height,
        double fps,
        int totalFrames,
        string encoder,
        Stopwatch sw,
        IProgress<(double percent, string status, TimeSpan eta)>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            int bitrateKbps = settings.BitrateKbps > 500 ? settings.BitrateKbps : 16000;
            double durationSec = totalFrames / fps;

            string codecFlags = encoder switch
            {
                "h264_nvenc" or "hevc_nvenc" => $"-c:v {encoder} -preset p4 -b:v {bitrateKbps}k -pix_fmt yuv420p",
                "h264_qsv" => $"-c:v h264_qsv -b:v {bitrateKbps}k -pix_fmt yuv420p",
                "h264_amf" => $"-c:v h264_amf -b:v {bitrateKbps}k -pix_fmt yuv420p",
                "prores_ks" => "-c:v prores_ks -profile:v 2 -pix_fmt yuv422p10le",
                "libx265" => $"-c:v libx265 -preset fast -crf 20 -b:v {bitrateKbps}k -pix_fmt yuv420p",
                _ => $"-c:v libx264 -preset fast -crf 18 -b:v {bitrateKbps}k -pix_fmt yuv420p"
            };

            string audioFlags = encoder == "prores_ks"
                ? "-c:a pcm_s16le"
                : "-c:a aac -b:a 320k";

            string args =
                $"-y -f rawvideo -pix_fmt bgra -s {width}x{height} -r {fps:0.###} -i pipe:0 " +
                $"-ss {startSec:0.###} -t {durationSec:0.###} -i \"{audioWavPath}\" " +
                $"{codecFlags} {audioFlags} -shortest -movflags +faststart \"{settings.OutputPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegBin,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            // Drain stderr asynchronously so FFmpeg never blocks on stderr buffer full
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!proc.StandardError.EndOfStream)
                    {
                        await proc.StandardError.ReadLineAsync();
                    }
                }
                catch { }
            });

            using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            byte[] frameBuffer = new byte[bitmap.RowBytes * height];
            var stdin = proc.StandardInput.BaseStream;

            for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    try { proc.Kill(true); } catch { }
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (proc.HasExited)
                    return false;

                double currentTime = startSec + (frameIdx / fps);
                _compositor.RenderCompositeFrame(canvas, document, currentTime, width, height);

                Marshal.Copy(bitmap.GetPixels(), frameBuffer, 0, frameBuffer.Length);
                stdin.Write(frameBuffer, 0, frameBuffer.Length);

                ReportFrameTelemetry(frameIdx + 1, totalFrames, sw.Elapsed, progress, encoder);
            }

            stdin.Flush();
            stdin.Close();
            proc.WaitForExit(20000);

            return proc.ExitCode == 0 && File.Exists(settings.OutputPath) && new FileInfo(settings.OutputPath).Length > 512;
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(settings.OutputPath)) File.Delete(settings.OutputPath); } catch { }
            throw;
        }
        catch
        {
            return false;
        }
    }

    private bool TryEncodeWithOpenCvVideoWriter(
        ProjectDocument document,
        ExportSettings settings,
        string outputPath,
        double startSec,
        int width,
        int height,
        double fps,
        int totalFrames,
        Stopwatch sw,
        IProgress<(double percent, string status, TimeSpan eta)>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var writer = new VideoWriter(outputPath, FourCC.MP4V, fps, new Size(width, height), isColor: true);
            if (!writer.IsOpened())
                return false;

            using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            using var bgrMat = new Mat(height, width, MatType.CV_8UC3);

            for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double currentTime = startSec + (frameIdx / fps);

                _compositor.RenderCompositeFrame(canvas, document, currentTime, width, height);

                using var bgraMat = Mat.FromPixelData(height, width, MatType.CV_8UC4, bitmap.GetPixels(), bitmap.RowBytes);
                Cv2.CvtColor(bgraMat, bgrMat, ColorConversionCodes.BGRA2BGR);
                writer.Write(bgrMat);

                ReportFrameTelemetry(frameIdx + 1, totalFrames, sw.Elapsed, progress, "OpenCV MP4V");
            }

            writer.Release();
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private bool TryEncodeWithFFMediaToolkit(
        ProjectDocument document,
        ExportSettings settings,
        string outputPath,
        double startSec,
        int width,
        int height,
        int fps,
        int totalFrames,
        Stopwatch sw,
        IProgress<(double percent, string status, TimeSpan eta)>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var videoSettings = new VideoEncoderSettings(width, height, fps, VideoCodec.H264)
            {
                EncoderPreset = EncoderPreset.Fast,
                CRF = 19
            };

            using var mediaOutput = MediaBuilder.CreateContainer(outputPath)
                .WithVideo(videoSettings)
                .Create();

            using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);

            for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double currentTime = startSec + (frameIdx / (double)fps);

                _compositor.RenderCompositeFrame(canvas, document, currentTime, width, height);

                IntPtr pixelsPtr = bitmap.GetPixels();
                unsafe
                {
                    var span = new Span<byte>((void*)pixelsPtr, bitmap.RowBytes * height);
                    var imgData = new ImageData(span, ImagePixelFormat.Bgra32, width, height);
                    mediaOutput.Video.AddFrame(imgData);
                }

                ReportFrameTelemetry(frameIdx + 1, totalFrames, sw.Elapsed, progress, "FFMediaToolkit H.264");
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static bool RunFfmpegSimple(string arguments)
    {
        string? exe = ResolveFfmpegExecutable();
        if (string.IsNullOrEmpty(exe)) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            _ = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit(20000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void ReportFrameTelemetry(
        int currentFrame,
        int totalFrames,
        TimeSpan elapsed,
        IProgress<(double percent, string status, TimeSpan eta)>? progress,
        string encoderLabel)
    {
        if (progress == null) return;

        double ratio = Math.Clamp((double)currentFrame / Math.Max(1, totalFrames), 0.01, 0.99);
        double pct = Math.Round(5.0 + ratio * 90.0, 1);
        double fpsSpeed = currentFrame / Math.Max(0.05, elapsed.TotalSeconds);
        double remainingSec = (totalFrames - currentFrame) / Math.Max(0.5, fpsSpeed);
        var eta = TimeSpan.FromSeconds(Math.Clamp(remainingSec, 0, 36000));

        string status = $"Frame {currentFrame} / {totalFrames} ({pct:0}%) • {fpsSpeed:0.0} fps [{encoderLabel}]";
        progress.Report((pct, status, eta));
    }
}
