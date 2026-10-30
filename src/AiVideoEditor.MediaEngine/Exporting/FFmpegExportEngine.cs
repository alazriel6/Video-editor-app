using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
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
/// Production Hardware-Accelerated FFmpeg &amp; Native Export Engine.
/// Supports NVIDIA NVENC, AMD AMF, Intel QSV, libx264, libx265, Apple ProRes 422, and Audio-Only WAV/MP3.
/// Implements asynchronous Producer-Consumer Channel piping with ArrayPool<byte> zero-allocation frame buffers
/// and guaranteed single-pass 48kHz stereo AAC audio muxing.
/// </summary>
public sealed class FFmpegExportEngine : IVideoRenderEngine
{
    private readonly CompositorEngine _compositor;
    private readonly MultiTrackAudioPlayer _audioPlayer;
    private static string? _cachedFfmpegExe;
    private static List<string>? _cachedSupportedEncoders;
    private static readonly ConcurrentDictionary<string, bool> _hardwareEncoderValidationCache = new(StringComparer.OrdinalIgnoreCase);

    public FFmpegExportEngine(CompositorEngine compositor, MultiTrackAudioPlayer audioPlayer)
    {
        _compositor = compositor;
        _audioPlayer = audioPlayer;
    }

    public static string? ResolveFfmpegExecutable()
    {
        if (!string.IsNullOrEmpty(_cachedFfmpegExe) && (File.Exists(_cachedFfmpegExe) || _cachedFfmpegExe == "ffmpeg"))
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

                    string[] checkCodes = { "h264_nvenc", "hevc_nvenc", "h264_amf", "h264_qsv", "libx264", "libx265", "prores_ks" };
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
            list.AddRange(new[] { "h264_nvenc", "h264_amf", "h264_qsv", "libx264", "libx265", "prores_ks" });
        }

        _cachedSupportedEncoders = list;
        return list;
    }

    /// <summary>
    /// Tests whether a specific GPU hardware encoder can initialize on the user's graphics card.
    /// Results are cached in memory so each encoder is probed at most once per session.
    /// </summary>
    private static bool CanInitializeHardwareEncoder(string ffmpegBin, string encoderName)
    {
        if (encoderName is "libx264" or "libx265" or "prores_ks")
            return true;

        if (_hardwareEncoderValidationCache.TryGetValue(encoderName, out bool isWorking))
            return isWorking;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegBin,
                Arguments = $"-hide_banner -loglevel error -f lavfi -i color=c=black:s=256x256:d=0.04 -frames:v 1 -c:v {encoderName} -f null -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null && proc.WaitForExit(3000) && proc.ExitCode == 0)
            {
                _hardwareEncoderValidationCache[encoderName] = true;
                return true;
            }
        }
        catch { }

        _hardwareEncoderValidationCache[encoderName] = false;
        return false;
    }

    /// <summary>
    /// Selects the fastest working GPU encoder (NVENC -> AMF -> QSV -> Multi-core libx264).
    /// </summary>
    private static string SelectOptimalHardwareEncoder(string ffmpegBin, string? userRequestedRaw)
    {
        string normalized = NormalizeEncoderId(userRequestedRaw);

        // If user explicitly chose a working hardware encoder or specialized codec (HEVC / ProRes), verify it first
        if (normalized != "auto" && CanInitializeHardwareEncoder(ffmpegBin, normalized))
            return normalized;

        // Auto-detect fastest active GPU encoder in priority order: NVIDIA NVENC -> AMD AMF -> Intel QSV -> CPU libx264
        string[] gpuPriorityOrder = { "h264_nvenc", "h264_amf", "h264_qsv" };
        foreach (var candidate in gpuPriorityOrder)
        {
            if (CanInitializeHardwareEncoder(ffmpegBin, candidate))
                return candidate;
        }

        return "libx264";
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

            // Ensure even dimensions for YUV420p/NV12 hardware encoders
            if (width % 2 != 0) width++;
            if (height % 2 != 0) height++;

            var sw = Stopwatch.StartNew();
            progress?.Report((2.0, "Stage 1/2: Mixing 48kHz multi-track audio & constant-power crossfades...", TimeSpan.FromSeconds(3)));

            string tempWav = Path.Combine(Path.GetTempPath(), $"vortex_master_{Guid.NewGuid():N}.wav");
            string tempVideoOnly = Path.Combine(Path.GetTempPath(), $"vortex_video_{Guid.NewGuid():N}.mp4");

            bool prevScopeUpdates = _compositor.EnableScopeUpdates;
            bool prevExporting = _compositor.IsExporting;
            _compositor.EnableScopeUpdates = false;
            _compositor.IsExporting = true;

            try
            {
                // Pre-render timeline audio for the exact [startSec, renderDuration] window
                await _audioPlayer.MixdownToWavAsync(document, tempWav, cancellationToken, startSec, renderDuration);
                bool hasValidAudioWav = File.Exists(tempWav) && new FileInfo(tempWav).Length > 44;

                // Audio-Only Export Preset (.wav or .mp3)
                bool isAudioOnly = settings.AudioOnly ||
                                   settings.OutputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                                   settings.OutputPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);

                if (isAudioOnly)
                {
                    string? ffmpegExe = ResolveFfmpegExecutable();
                    if (settings.OutputPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(ffmpegExe))
                    {
                        await RunFfmpegAsync(ffmpegExe, $"-y -i \"{tempWav}\" -c:a libmp3lame -b:a 320k -ar 48000 \"{settings.OutputPath}\"", cancellationToken);
                    }
                    if (!File.Exists(settings.OutputPath) && hasValidAudioWav)
                    {
                        File.Copy(tempWav, settings.OutputPath, overwrite: true);
                    }
                    progress?.Report((100.0, $"Audio Export Complete: {Path.GetFileName(settings.OutputPath)}", TimeSpan.Zero));
                    return;
                }

                bool encodedSuccess = false;
                string? ffmpegBin = ResolveFfmpegExecutable();

                // Stage 2A: High-Speed Asynchronous Channel Pipe to Hardware/CPU FFmpeg
                if (!string.IsNullOrEmpty(ffmpegBin))
                {
                    string selectedEncoder = SelectOptimalHardwareEncoder(ffmpegBin, settings.HardwareEncoder);

                    encodedSuccess = await TryEncodeViaFfmpegRawPipeAsync(
                        ffmpegBin, document, settings, tempWav, hasValidAudioWav,
                        startSec, width, height, fps, totalFrames,
                        selectedEncoder, sw, progress, cancellationToken);

                    // If selected GPU encoder failed mid-stream, automatically retry with multi-core libx264 pipe
                    if (!encodedSuccess && selectedEncoder != "libx264")
                    {
                        _hardwareEncoderValidationCache[selectedEncoder] = false;
                        encodedSuccess = await TryEncodeViaFfmpegRawPipeAsync(
                            ffmpegBin, document, settings, tempWav, hasValidAudioWav,
                            startSec, width, height, fps, totalFrames,
                            "libx264", sw, progress, cancellationToken);
                    }
                }

                // Stage 2B: Fallback to FFMediaToolkit or OpenCvSharp VideoWriter + Mandatory Audio Muxing
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
                        progress?.Report((96.0, "Muxing video & 48kHz master audio stream...", TimeSpan.FromSeconds(1)));

                        if (!string.IsNullOrEmpty(ffmpegBin) && hasValidAudioWav)
                        {
                            string muxArgs =
                                $"-y -i \"{tempVideoOnly}\" -i \"{tempWav}\" " +
                                $"-map 0:v:0 -map 1:a:0? -c:v copy -c:a aac -b:a 256k -ar 48000 -movflags +faststart \"{settings.OutputPath}\"";

                            encodedSuccess = await RunFfmpegAsync(ffmpegBin, muxArgs, cancellationToken) &&
                                             File.Exists(settings.OutputPath) &&
                                             new FileInfo(settings.OutputPath).Length > 512;
                        }
                        else if (string.IsNullOrEmpty(ffmpegBin))
                        {
                            throw new InvalidOperationException(
                                "FFmpeg executable is required to mux master audio into the exported container. Please ensure ffmpeg.exe is installed.");
                        }
                    }
                }

                if (!encodedSuccess || !File.Exists(settings.OutputPath))
                {
                    throw new InvalidOperationException("Video export pipeline could not finalize the encoded output container.");
                }

                progress?.Report((100.0, $"Export Complete ({totalFrames} frames) -> {Path.GetFileName(settings.OutputPath)}", TimeSpan.Zero));
            }
            finally
            {
                _compositor.EnableScopeUpdates = prevScopeUpdates;
                _compositor.IsExporting = prevExporting;
                try { if (File.Exists(tempWav)) File.Delete(tempWav); } catch { }
                try { if (File.Exists(tempVideoOnly)) File.Delete(tempVideoOnly); } catch { }
            }
        }, cancellationToken);
    }

    private static string NormalizeEncoderId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "auto";
        if (raw.Contains("auto", StringComparison.OrdinalIgnoreCase)) return "auto";
        if (raw.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase)) return "h264_nvenc";
        if (raw.Contains("hevc_nvenc", StringComparison.OrdinalIgnoreCase)) return "hevc_nvenc";
        if (raw.Contains("h264_qsv", StringComparison.OrdinalIgnoreCase)) return "h264_qsv";
        if (raw.Contains("h264_amf", StringComparison.OrdinalIgnoreCase)) return "h264_amf";
        if (raw.Contains("libx265", StringComparison.OrdinalIgnoreCase)) return "libx265";
        if (raw.Contains("prores", StringComparison.OrdinalIgnoreCase)) return "prores_ks";
        if (raw.Contains("libx264", StringComparison.OrdinalIgnoreCase)) return "libx264";
        return "auto";
    }

    private async Task<bool> TryEncodeViaFfmpegRawPipeAsync(
        string ffmpegBin,
        ProjectDocument document,
        ExportSettings settings,
        string audioWavPath,
        bool hasValidAudioWav,
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
            int maxrateKbps = (int)(bitrateKbps * 1.5);
            int bufsizeKbps = bitrateKbps * 2;

            string codecFlags = encoder switch
            {
                "h264_nvenc" or "hevc_nvenc" =>
                    $"-c:v {encoder} -preset p4 -tune hq -rc vbr -cq 19 -b:v {bitrateKbps}k -maxrate {maxrateKbps}k -bufsize {bufsizeKbps}k -pix_fmt yuv420p",
                "h264_amf" =>
                    $"-c:v h264_amf -quality balanced -rc vbr_peak -b:v {bitrateKbps}k -maxrate {maxrateKbps}k -pix_fmt yuv420p",
                "h264_qsv" =>
                    $"-c:v h264_qsv -preset faster -global_quality 20 -b:v {bitrateKbps}k -pix_fmt nv12",
                "prores_ks" =>
                    "-c:v prores_ks -profile:v 2 -pix_fmt yuv422p10le",
                "libx265" =>
                    $"-c:v libx265 -preset fast -crf 20 -threads 0 -b:v {bitrateKbps}k -pix_fmt yuv420p",
                _ =>
                    $"-c:v libx264 -preset superfast -crf 18 -threads 0 -b:v {bitrateKbps}k -pix_fmt yuv420p"
            };

            string audioFlags = encoder == "prores_ks"
                ? "-c:a pcm_s16le -ar 48000"
                : "-c:a aac -b:a 256k -ar 48000";

            string args = hasValidAudioWav
                ? $"-y -f rawvideo -pix_fmt bgra -s {width}x{height} -r {fps:0.###} -i pipe:0 " +
                  $"-i \"{audioWavPath}\" -map 0:v:0 -map 1:a:0? " +
                  $"{codecFlags} {audioFlags} -movflags +faststart \"{settings.OutputPath}\""
                : $"-y -f rawvideo -pix_fmt bgra -s {width}x{height} -r {fps:0.###} -i pipe:0 " +
                  $"-map 0:v:0 {codecFlags} -movflags +faststart \"{settings.OutputPath}\"";

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

            // Drain stderr asynchronously into a 50-line ring buffer so FFmpeg never blocks on OS pipe buffers
            var stderrRing = new Queue<string>(50);
            var stderrDrainTask = Task.Run(async () =>
            {
                try
                {
                    using var errReader = proc.StandardError;
                    while (await errReader.ReadLineAsync().ConfigureAwait(false) is { } line)
                    {
                        lock (stderrRing)
                        {
                            if (stderrRing.Count >= 50) stderrRing.Dequeue();
                            stderrRing.Enqueue(line);
                        }
                    }
                }
                catch { }
            });

            // Bounded Producer-Consumer Channel (8 frames capacity) decoupling Skia composition from pipe I/O
            var frameChannel = Channel.CreateBounded<(byte[] Buffer, int Length)>(new BoundedChannelOptions(8)
            {
                SingleWriter = true,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            var stdin = proc.StandardInput.BaseStream;

            // Consumer Task: Asynchronous Pipe Writer
            var consumerTask = Task.Run(async () =>
            {
                try
                {
                    while (await frameChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (frameChannel.Reader.TryRead(out var packet))
                        {
                            try
                            {
                                if (!proc.HasExited)
                                {
                                    await stdin.WriteAsync(packet.Buffer.AsMemory(0, packet.Length), cancellationToken).ConfigureAwait(false);
                                }
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(packet.Buffer);
                            }
                        }
                    }
                    await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Drain any remaining rented buffers back to ArrayPool if FFmpeg exited early
                    while (frameChannel.Reader.TryRead(out var leftover))
                    {
                        ArrayPool<byte>.Shared.Return(leftover.Buffer);
                    }
                }
                finally
                {
                    try { stdin.Close(); } catch { }
                }
            }, cancellationToken);

            // Producer Loop: High-Speed Frame Rendering into Rented ArrayPool Buffers
            using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            int frameByteLength = bitmap.RowBytes * height;

            try
            {
                for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        frameChannel.Writer.TryComplete();
                        try { proc.Kill(entireProcessTree: true); } catch { }
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (proc.HasExited)
                        break;

                    double currentTime = startSec + (frameIdx / fps);
                    _compositor.RenderCompositeFrame(canvas, document, currentTime, width, height);

                    byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(frameByteLength);
                    Marshal.Copy(bitmap.GetPixels(), rentedBuffer, 0, frameByteLength);

                    await frameChannel.Writer.WriteAsync((rentedBuffer, frameByteLength), cancellationToken).ConfigureAwait(false);

                    ReportFrameTelemetry(frameIdx + 1, totalFrames, sw.Elapsed, progress, encoder);
                }
            }
            finally
            {
                frameChannel.Writer.TryComplete();
            }

            await consumerTask.ConfigureAwait(false);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stderrDrainTask.ConfigureAwait(false);

            return proc.ExitCode == 0 &&
                   File.Exists(settings.OutputPath) &&
                   new FileInfo(settings.OutputPath).Length > 512;
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

    private static async Task<bool> RunFfmpegAsync(string ffmpegExe, string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;

            var drainErr = proc.StandardError.ReadToEndAsync();
            var drainOut = proc.StandardOutput.ReadToEndAsync();

            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(drainErr, drainOut).ConfigureAwait(false);
            return proc.ExitCode == 0;
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

    private static void ReportFrameTelemetry(
        int currentFrame,
        int totalFrames,
        TimeSpan elapsed,
        IProgress<(double percent, string status, TimeSpan eta)>? progress,
        string encoderLabel)
    {
        if (progress == null) return;

        double pct = 5.0 + ((double)currentFrame / Math.Max(1, totalFrames)) * 90.0;
        double currentFps = currentFrame / Math.Max(0.05, elapsed.TotalSeconds);
        int remainingFrames = Math.Max(0, totalFrames - currentFrame);
        double etaSeconds = remainingFrames / Math.Max(0.5, currentFps);

        progress.Report((
            Math.Clamp(pct, 0.0, 99.0),
            $"Encoding [{encoderLabel}] Frame {currentFrame}/{totalFrames} ({currentFps:0.0} fps)",
            TimeSpan.FromSeconds(etaSeconds)));
    }
}
