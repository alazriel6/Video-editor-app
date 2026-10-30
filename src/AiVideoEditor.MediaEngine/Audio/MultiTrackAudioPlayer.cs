using System.Collections.Concurrent;
using System.Diagnostics;
using AiVideoEditor.Core.Models;
using AiVideoEditor.MediaEngine.Exporting;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AiVideoEditor.MediaEngine.Audio;

public sealed class MultiTrackAudioPlayer : IDisposable
{
    private WaveOutEvent? _waveOut;
    private readonly List<IDisposable> _activeReaders = new();
    private readonly object _lock = new();
    private static readonly ConcurrentDictionary<string, string> _extractedAudioWavCache = new(StringComparer.OrdinalIgnoreCase);

    public bool IsPlaying { get; private set; }
    public float CurrentLeftDb { get; private set; } = -60.0f;
    public float CurrentRightDb { get; private set; } = -60.0f;

    public void StartPlayback(ProjectDocument document, double startTimeSeconds)
    {
        StopPlayback();

        lock (_lock)
        {
            try
            {
                var mixer = BuildMixerForDocument(document, startTimeSeconds, out var disposables);
                _activeReaders.AddRange(disposables);

                _waveOut = new WaveOutEvent { DesiredLatency = 90 };
                _waveOut.Init(mixer);
                _waveOut.Play();
                IsPlaying = true;
            }
            catch
            {
                IsPlaying = false;
            }
        }
    }

    public void StopPlayback()
    {
        lock (_lock)
        {
            IsPlaying = false;
            CurrentLeftDb = -60.0f;
            CurrentRightDb = -60.0f;

            if (_waveOut != null)
            {
                try
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                }
                catch { }
                _waveOut = null;
            }

            foreach (var d in _activeReaders)
            {
                try { d.Dispose(); } catch { }
            }
            _activeReaders.Clear();
        }
    }

    /// <summary>
    /// Samples instantaneous stereo dB levels at playheadTime from active track waveforms &amp; clip GainDb/Pan for real-time VU metering.
    /// </summary>
    public (float LeftDb, float RightDb) SampleRealtimeVuLevels(ProjectDocument document, double playheadTime)
    {
        bool anySolo = false;
        for (int i = 0; i < document.Tracks.Count; i++)
        {
            if (document.Tracks[i].IsSolo) { anySolo = true; break; }
        }

        float sumLeftLinear = 0f;
        float sumRightLinear = 0f;

        for (int t = 0; t < document.Tracks.Count; t++)
        {
            var track = document.Tracks[t];
            if (track.IsMuted) continue;
            if (anySolo && !track.IsSolo) continue;
            if (track.Type != TrackType.Audio && track.Type != TrackType.Video) continue;

            for (int c = 0; c < track.Clips.Count; c++)
            {
                var clip = track.Clips[c];
                if (clip.Kind == ClipKind.Transition) continue;
                if (playheadTime < clip.TimelineStart || playheadTime >= clip.TimelineStart + clip.Duration)
                    continue;

                var asset = document.FindAsset(clip.MediaAssetId);
                if (asset == null || !asset.HasAudio) continue;

                double clipOffset = playheadTime - clip.TimelineStart;
                float peak = 0.45f;
                if (asset.WaveformPeaks.Length > 0 && asset.Duration > 0.01)
                {
                    double srcTime = clip.ReversePlayback
                        ? Math.Max(0, clip.SourceOut - clipOffset * clip.Speed)
                        : clip.SourceIn + clipOffset * clip.Speed;
                    int idx = (int)((srcTime / asset.Duration) * asset.WaveformPeaks.Length);
                    peak = asset.WaveformPeaks[Math.Clamp(idx, 0, asset.WaveformPeaks.Length - 1)];
                }

                float gainDb = clip.EvaluateGainDb(clipOffset);
                float clipLinear = MathF.Pow(10f, gainDb / 20f);
                float gainLinear = clipLinear * track.Volume;

                // Apply Constant-Power Fade In/Out envelope (including InTransition / OutTransition specs)
                double fadeInDur = clip.FadeInDuration;
                if (clip.InTransition is { IsEnabled: true, Duration: > 0.01 } && clip.InTransition.Type != "None")
                    fadeInDur = Math.Max(fadeInDur, clip.InTransition.Duration);

                if (fadeInDur > 0.01 && clipOffset < fadeInDur)
                {
                    float fadeT = (float)Math.Clamp(clipOffset / fadeInDur, 0.0, 1.0);
                    gainLinear *= MathF.Sin(fadeT * MathF.PI * 0.5f);
                }

                double fadeOutDur = clip.FadeOutDuration;
                if (clip.OutTransition is { IsEnabled: true, Duration: > 0.01 } && clip.OutTransition.Type != "None")
                    fadeOutDur = Math.Max(fadeOutDur, clip.OutTransition.Duration);

                double rem = clip.Duration - clipOffset;
                if (fadeOutDur > 0.01 && rem < fadeOutDur)
                {
                    float fadeT = (float)Math.Clamp(rem / fadeOutDur, 0.0, 1.0);
                    gainLinear *= MathF.Sin(fadeT * MathF.PI * 0.5f);
                }

                // Apply Standalone Audio Transition blocks & overlapping clip crossfades on this track
                gainLinear *= EvaluateAudioTransitionMultiplier(clip, track.Clips, playheadTime);

                float pan = Math.Clamp(clip.Pan, -1f, 1f);
                float leftPan = pan <= 0f ? 1f : (1f - pan);
                float rightPan = pan >= 0f ? 1f : (1f + pan);

                float jitter = (float)(0.94 + 0.12 * Math.Abs(Math.Sin(playheadTime * 31.0)));
                sumLeftLinear += peak * gainLinear * leftPan;
                sumRightLinear += peak * gainLinear * rightPan * jitter;
            }
        }

        float leftDb = sumLeftLinear <= 0.001f ? -60f : Math.Clamp(20f * MathF.Log10(sumLeftLinear), -60f, 3f);
        float rightDb = sumRightLinear <= 0.001f ? -60f : Math.Clamp(20f * MathF.Log10(sumRightLinear), -60f, 3f);

        CurrentLeftDb = leftDb;
        CurrentRightDb = rightDb;
        return (leftDb, rightDb);
    }

    /// <summary>
    /// Evaluates Constant-Power Equal-Power Sine/Cosine crossfade multiplier for:
    /// 1) Standalone Transition blocks (ClipKind.Transition)
    /// 2) Overlapping media clips on the same track
    /// </summary>
    internal static float EvaluateAudioTransitionMultiplier(
        TimelineClip clip,
        IReadOnlyList<TimelineClip> trackClips,
        double timelineSeconds)
    {
        float mult = 1f;
        double clipStart = clip.TimelineStart;
        double clipEnd = clip.TimelineStart + clip.Duration;

        for (int i = 0; i < trackClips.Count; i++)
        {
            var other = trackClips[i];
            if (ReferenceEquals(other, clip) || other.Id == clip.Id)
                continue;

            if (other.Kind == ClipKind.Transition)
            {
                if (timelineSeconds < other.TimelineStart || timelineSeconds > other.TimelineStart + other.Duration)
                    continue;

                float p = (float)Math.Clamp((timelineSeconds - other.TimelineStart) / Math.Max(0.05, other.Duration), 0.0, 1.0);
                string type = other.TransitionEffectType ?? "ConstantPowerCrossfade";

                double distToStart = Math.Abs(other.TimelineStart - clipStart);
                double distToEnd = Math.Abs((other.TimelineStart + other.Duration) - clipEnd);

                if (type.Equals("ExponentialFadeIn", StringComparison.OrdinalIgnoreCase) || distToStart <= distToEnd)
                {
                    // Incoming clip: Constant-Power Sine or Exponential curve
                    mult *= type.Equals("ExponentialFadeIn", StringComparison.OrdinalIgnoreCase)
                        ? (p * p)
                        : MathF.Sin(p * MathF.PI * 0.5f);
                }
                else
                {
                    // Outgoing clip: Constant-Power Cosine or Exponential curve
                    float inv = 1f - p;
                    mult *= type.Equals("ExponentialFadeOut", StringComparison.OrdinalIgnoreCase)
                        ? (inv * inv)
                        : MathF.Cos(p * MathF.PI * 0.5f);
                }
            }
            else
            {
                // Automatic Equal-Power Crossfade when two media clips overlap on the same track
                double otherStart = other.TimelineStart;
                double otherEnd = other.TimelineStart + other.Duration;

                double overlapStart = Math.Max(clipStart, otherStart);
                double overlapEnd = Math.Min(clipEnd, otherEnd);
                double overlapDur = overlapEnd - overlapStart;

                if (overlapDur > 0.02 && timelineSeconds >= overlapStart && timelineSeconds <= overlapEnd)
                {
                    float t = (float)Math.Clamp((timelineSeconds - overlapStart) / overlapDur, 0.0, 1.0);
                    bool isIncoming = clipStart > otherStart || (Math.Abs(clipStart - otherStart) < 0.001 && clip.Id.CompareTo(other.Id) > 0);
                    if (isIncoming)
                    {
                        mult *= MathF.Sin(t * MathF.PI * 0.5f);
                    }
                    else
                    {
                        mult *= MathF.Cos(t * MathF.PI * 0.5f);
                    }
                }
            }
        }
        return mult;
    }

    public async Task<string> MixdownToWavAsync(
        ProjectDocument document,
        string outputWavPath,
        CancellationToken cancellationToken = default,
        double startTimeSeconds = 0.0,
        double? durationSeconds = null)
    {
        await Task.Run(() =>
        {
            var mixer = BuildMixerForDocument(document, startTimeSeconds, out var disposables);
            try
            {
                double totalSeconds = durationSeconds ?? Math.Max(0.5, document.TotalDuration - startTimeSeconds);
                totalSeconds = Math.Max(0.2, totalSeconds);

                var bounded = new OffsetSampleProvider(mixer)
                {
                    Take = TimeSpan.FromSeconds(totalSeconds)
                };
                WaveFileWriter.CreateWaveFile16(outputWavPath, bounded);
            }
            finally
            {
                foreach (var d in disposables)
                {
                    try { d.Dispose(); } catch { }
                }
            }
        }, cancellationToken);

        return outputWavPath;
    }

    private ISampleProvider BuildMixerForDocument(
        ProjectDocument document,
        double playheadSeconds,
        out List<IDisposable> disposables)
    {
        disposables = new List<IDisposable>();
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var mixer = new MixingSampleProvider(targetFormat) { ReadFully = true };

        bool anySolo = document.Tracks.Any(t => t.IsSolo);

        foreach (var track in document.Tracks.Where(t => (t.Type == TrackType.Audio || t.Type == TrackType.Video) && !t.IsMuted))
        {
            if (anySolo && !track.IsSolo) continue;

            var allTrackClips = track.Clips.ToList();

            foreach (var clip in track.Clips.Where(c => c.Kind != ClipKind.Transition))
            {
                double clipEnd = clip.TimelineStart + clip.Duration;
                if (clipEnd <= playheadSeconds) continue;

                var asset = document.MediaPool.FirstOrDefault(m => m.Id == clip.MediaAssetId);
                if (asset == null || string.IsNullOrWhiteSpace(asset.FilePath) || !File.Exists(asset.FilePath))
                    continue;

                // Skip pure image assets
                if (asset.Type == MediaAssetType.Image)
                    continue;

                var rawProvider = OpenResilientAudioSampleProvider(asset.FilePath, out var readerDisposable);
                if (rawProvider == null)
                    continue;

                if (readerDisposable != null)
                    disposables.Add(readerDisposable);

                double speed = Math.Clamp(clip.Speed, 0.1, 8.0);
                double skipIntoClip = Math.Max(0, playheadSeconds - clip.TimelineStart);
                double delayBeforeClip = Math.Max(0, clip.TimelineStart - playheadSeconds);
                double remainingDuration = Math.Max(0.05, clip.Duration - skipIntoClip);
                double sourceSkipSeconds = Math.Max(0, clip.SourceIn + skipIntoClip * speed);

                // 1. Standardize to Stereo 48kHz first
                ISampleProvider chain = rawProvider;
                if (chain.WaveFormat.Channels == 1)
                    chain = new MonoToStereoSampleProvider(chain);
                if (chain.WaveFormat.SampleRate != 48000)
                    chain = new WdlResamplingSampleProvider(chain, 48000);

                // 2. Skip into source media at source rate
                var sourceSkipProvider = new OffsetSampleProvider(chain)
                {
                    SkipOver = TimeSpan.FromSeconds(sourceSkipSeconds)
                };

                // 3. Apply Speed Resampling if clip.Speed != 1.0 so audio duration matches video clip duration
                ISampleProvider speedAdjusted = Math.Abs(speed - 1.0) > 0.005
                    ? new VariableSpeedSampleProvider(sourceSkipProvider, (float)speed)
                    : sourceSkipProvider;

                // 4. Apply timeline delay and exact timeline clip duration Take
                var timelinePositioned = new OffsetSampleProvider(speedAdjusted)
                {
                    DelayBy = TimeSpan.FromSeconds(delayBeforeClip),
                    Take = TimeSpan.FromSeconds(remainingDuration)
                };

                // 5. Per-clip DSP (Keyframes, Pan, Constant-Power Fades, Overlapping Crossfades, EQ, Reverb, Limiter)
                var dspProvider = new ClipAudioDspSampleProvider(timelinePositioned, clip, allTrackClips, track.Volume, skipIntoClip);
                mixer.AddMixerInput(dspProvider);
            }
        }

        return mixer;
    }

    /// <summary>
    /// Opens an audio stream from any media file. If NAudio's MediaFoundation reader fails (e.g. MP4/MKV/MOV
    /// codec issues or file lock contention with OpenCV), automatically extracts a cached 48kHz stereo PCM WAV
    /// via FFmpeg CLI so exported and previewed audio never drops silently.
    /// </summary>
    private static ISampleProvider? OpenResilientAudioSampleProvider(string filePath, out IDisposable? disposable)
    {
        disposable = null;

        // Fast path: if we already extracted a clean WAV for this container, use it directly
        if (_extractedAudioWavCache.TryGetValue(filePath, out var cachedWav) && File.Exists(cachedWav))
        {
            try
            {
                var wavReader = new AudioFileReader(cachedWav);
                disposable = wavReader;
                return wavReader;
            }
            catch { }
        }

        // Primary attempt: NAudio AudioFileReader
        try
        {
            var reader = new AudioFileReader(filePath);
            if (reader.TotalTime.TotalSeconds > 0.01)
            {
                disposable = reader;
                return reader;
            }
            reader.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MultiTrackAudioPlayer] AudioFileReader failed for '{filePath}': {ex.Message}. Falling back to FFmpeg PCM extraction.");
        }

        // Fallback: Extract 48kHz stereo 16-bit PCM WAV via FFmpeg CLI
        string? extractedPath = TryExtractAudioWithFfmpeg(filePath);
        if (!string.IsNullOrEmpty(extractedPath) && File.Exists(extractedPath))
        {
            try
            {
                _extractedAudioWavCache[filePath] = extractedPath;
                var fallbackReader = new AudioFileReader(extractedPath);
                disposable = fallbackReader;
                return fallbackReader;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MultiTrackAudioPlayer] Fallback WaveFileReader failed for '{extractedPath}': {ex.Message}");
            }
        }

        return null;
    }

    private static string? TryExtractAudioWithFfmpeg(string inputMediaPath)
    {
        try
        {
            string? ffmpegExe = FFmpegExportEngine.ResolveFfmpegExecutable();
            if (string.IsNullOrEmpty(ffmpegExe))
                return null;

            string cacheDir = Path.Combine(Path.GetTempPath(), "AiVideoEditor_AudioCache");
            Directory.CreateDirectory(cacheDir);

            // Deterministic hash key based on path and last write time
            long ticks = File.GetLastWriteTimeUtc(inputMediaPath).Ticks;
            string safeHash = $"{ inputMediaPath.GetHashCode():X8}_{ticks:X}";
            string targetWav = Path.Combine(cacheDir, $"extracted_{safeHash}.wav");

            if (File.Exists(targetWav) && new FileInfo(targetWav).Length > 128)
                return targetWav;

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = $"-y -i \"{inputMediaPath}\" -vn -ac 2 -ar 48000 -c:a pcm_s16le \"{targetWav}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            _ = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit(30000);

            if (File.Exists(targetWav) && new FileInfo(targetWav).Length > 128)
                return targetWav;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MultiTrackAudioPlayer] FFmpeg audio extraction failed: {ex.Message}");
        }

        return null;
    }

    public void Dispose()
    {
        StopPlayback();
    }
}

/// <summary>
/// Fractional-index stereo linear resampler for clips with Speed != 1.0.
/// Ensures audio duration and timing stay 100% synchronized with speed-ramped video clips.
/// </summary>
internal sealed class VariableSpeedSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _speed;
    private float[] _sourceBuffer = new float[4096];
    private int _sourceSamplesAvailable;
    private double _sourceFrameCursor;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public VariableSpeedSampleProvider(ISampleProvider source, float speed)
    {
        _source = source;
        _speed = Math.Clamp(speed, 0.1f, 8.0f);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int channels = WaveFormat.Channels;
        int outFramesRequested = count / channels;
        int outFramesWritten = 0;

        while (outFramesWritten < outFramesRequested)
        {
            int idx0 = (int)_sourceFrameCursor;
            int idx1 = idx0 + 1;
            int availFrames = _sourceSamplesAvailable / channels;

            if (idx1 >= availFrames)
            {
                // Shift remaining unconsumed frame to start of buffer and refill
                int keepFrames = Math.Max(0, availFrames - idx0);
                if (keepFrames > 0 && idx0 > 0)
                {
                    Array.Copy(_sourceBuffer, idx0 * channels, _sourceBuffer, 0, keepFrames * channels);
                }
                _sourceFrameCursor -= idx0;
                if (_sourceFrameCursor < 0) _sourceFrameCursor = 0;

                int existingSamples = keepFrames * channels;
                int toRead = _sourceBuffer.Length - existingSamples;
                int read = _source.Read(_sourceBuffer, existingSamples, toRead);
                _sourceSamplesAvailable = existingSamples + read;

                if (read == 0 && (_sourceSamplesAvailable / channels) <= 1)
                    break;

                continue;
            }

            float frac = (float)(_sourceFrameCursor - idx0);
            int outBase = offset + outFramesWritten * channels;
            int srcBase0 = idx0 * channels;
            int srcBase1 = idx1 * channels;

            for (int ch = 0; ch < channels; ch++)
            {
                float s0 = _sourceBuffer[srcBase0 + ch];
                float s1 = _sourceBuffer[srcBase1 + ch];
                buffer[outBase + ch] = s0 + (s1 - s0) * frac;
            }

            outFramesWritten++;
            _sourceFrameCursor += _speed;
        }

        return outFramesWritten * channels;
    }
}

/// <summary>
/// Real-time stereo DSP sample provider for per-clip Volume Keyframes, Pan, Constant-Power Fade In/Out,
/// Standalone Audio Transition Blocks, Overlapping Clip Crossfades, 3-Band EQ Presets, Compressor/Limiter, and Reverb.
/// </summary>
internal sealed class ClipAudioDspSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly TimelineClip _clip;
    private readonly IReadOnlyList<TimelineClip> _trackClips;
    private readonly float _trackVolume;
    private long _samplesProcessed;
    private readonly double _startSkipSeconds;

    // Simple 1-pole low-pass state for EQ & Reverb comb delay buffer
    private float _lpLeft;
    private float _lpRight;
    private readonly float[] _reverbDelayL = new float[4800];
    private readonly float[] _reverbDelayR = new float[5600];
    private int _revIdxL;
    private int _revIdxR;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public ClipAudioDspSampleProvider(
        ISampleProvider source,
        TimelineClip clip,
        IReadOnlyList<TimelineClip> trackClips,
        float trackVolume,
        double startSkipSeconds)
    {
        _source = source;
        _clip = clip;
        _trackClips = trackClips;
        _trackVolume = trackVolume;
        _startSkipSeconds = startSkipSeconds;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read <= 0) return read;

        int sampleRate = WaveFormat.SampleRate;
        float pan = Math.Clamp(_clip.Pan, -1f, 1f);
        float leftPan = pan <= 0f ? 1f : (1f - pan);
        float rightPan = pan >= 0f ? 1f : (1f + pan);

        bool hasReverb = !string.IsNullOrEmpty(_clip.ReverbPreset) &&
                         !_clip.ReverbPreset.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                         _clip.ReverbDryWet > 0.01f;
        float wet = hasReverb ? Math.Clamp(_clip.ReverbDryWet * 0.45f, 0f, 0.6f) : 0f;

        double fadeInDur = _clip.FadeInDuration;
        if (_clip.InTransition is { IsEnabled: true, Duration: > 0.01 } && _clip.InTransition.Type != "None")
            fadeInDur = Math.Max(fadeInDur, _clip.InTransition.Duration);

        double fadeOutDur = _clip.FadeOutDuration;
        if (_clip.OutTransition is { IsEnabled: true, Duration: > 0.01 } && _clip.OutTransition.Type != "None")
            fadeOutDur = Math.Max(fadeOutDur, _clip.OutTransition.Duration);

        for (int i = 0; i < read; i += 2)
        {
            double clipTime = _startSkipSeconds + (_samplesProcessed / (double)sampleRate);
            double timelineTime = _clip.TimelineStart + clipTime;
            _samplesProcessed++;

            float gainDb = _clip.EvaluateGainDb(clipTime);
            float gain = MathF.Pow(10f, gainDb / 20f) * _trackVolume;

            if (_clip.DialogueIsolateEnabled)
                gain *= 1.15f;

            // Constant-Power Equal-Power Sine/Cosine Fade In & Out
            if (fadeInDur > 0.01 && clipTime < fadeInDur)
            {
                float t = (float)Math.Clamp(clipTime / fadeInDur, 0.0, 1.0);
                gain *= MathF.Sin(t * MathF.PI * 0.5f);
            }
            double rem = _clip.Duration - clipTime;
            if (fadeOutDur > 0.01 && rem < fadeOutDur)
            {
                float t = (float)Math.Clamp(rem / fadeOutDur, 0.0, 1.0);
                gain *= MathF.Sin(t * MathF.PI * 0.5f);
            }

            if (_trackClips.Count > 1)
            {
                gain *= MultiTrackAudioPlayer.EvaluateAudioTransitionMultiplier(_clip, _trackClips, timelineTime);
            }

            float l = buffer[offset + i] * gain * leftPan;
            float r = (i + 1 < read ? buffer[offset + i + 1] : l) * gain * rightPan;

            // Simple 1-pole low/high shelf EQ approximation
            _lpLeft += 0.08f * (l - _lpLeft);
            _lpRight += 0.08f * (r - _lpRight);
            float hpLeft = l - _lpLeft;
            float hpRight = r - _lpRight;

            if (_clip.EqPreset.Equals("BassBoost", StringComparison.OrdinalIgnoreCase))
            {
                l = l + _lpLeft * 0.55f;
                r = r + _lpRight * 0.55f;
            }
            else if (_clip.EqPreset.Equals("TrebleSparkle", StringComparison.OrdinalIgnoreCase))
            {
                l = l + hpLeft * 0.45f;
                r = r + hpRight * 0.45f;
            }
            else if (_clip.EqPreset.Equals("PodcastVoice", StringComparison.OrdinalIgnoreCase) || _clip.DialogueIsolateEnabled)
            {
                l = _lpLeft * 0.65f + hpLeft * 1.25f;
                r = _lpRight * 0.65f + hpRight * 1.25f;
            }

            // Reverb tail
            if (hasReverb)
            {
                float dl = _reverbDelayL[_revIdxL];
                float dr = _reverbDelayR[_revIdxR];
                _reverbDelayL[_revIdxL] = l + dl * 0.42f;
                _reverbDelayR[_revIdxR] = r + dr * 0.42f;
                _revIdxL = (_revIdxL + 1) % _reverbDelayL.Length;
                _revIdxR = (_revIdxR + 1) % _reverbDelayR.Length;
                l = l * (1f - wet) + dl * wet;
                r = r * (1f - wet) + dr * wet;
            }

            // Soft knee compressor & brickwall limiter at -1.0 dB (0.891f)
            if (_clip.CompressorEnabled)
            {
                l = MathF.Tanh(l * 1.35f) * 0.89f;
                r = MathF.Tanh(r * 1.35f) * 0.89f;
            }
            else
            {
                l = Math.Clamp(l, -0.98f, 0.98f);
                r = Math.Clamp(r, -0.98f, 0.98f);
            }

            buffer[offset + i] = l;
            if (i + 1 < read)
                buffer[offset + i + 1] = r;
        }

        return read;
    }
}
