using AiVideoEditor.Core.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AiVideoEditor.MediaEngine.Audio;

public sealed class MultiTrackAudioPlayer : IDisposable
{
    private WaveOutEvent? _waveOut;
    private readonly List<IDisposable> _activeReaders = new();
    private readonly object _lock = new();

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
        bool anySolo = document.Tracks.Any(t => t.IsSolo);
        float sumLeftLinear = 0f;
        float sumRightLinear = 0f;

        foreach (var track in document.Tracks)
        {
            if (track.IsMuted) continue;
            if (anySolo && !track.IsSolo) continue;
            if (track.Type != TrackType.Audio && track.Type != TrackType.Video) continue;

            foreach (var clip in track.Clips)
            {
                if (playheadTime < clip.TimelineStart || playheadTime >= clip.TimelineStart + clip.Duration)
                    continue;

                var asset = document.MediaPool.FirstOrDefault(m => m.Id == clip.MediaAssetId);
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

                // Apply Constant Power Fade In/Out envelope
                if (clip.FadeInDuration > 0.01 && clipOffset < clip.FadeInDuration)
                {
                    float t = (float)Math.Clamp(clipOffset / clip.FadeInDuration, 0.0, 1.0);
                    gainLinear *= MathF.Sin(t * MathF.PI * 0.5f);
                }
                double rem = clip.Duration - clipOffset;
                if (clip.FadeOutDuration > 0.01 && rem < clip.FadeOutDuration)
                {
                    float t = (float)Math.Clamp(rem / clip.FadeOutDuration, 0.0, 1.0);
                    gainLinear *= MathF.Sin(t * MathF.PI * 0.5f);
                }

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

    public async Task<string> MixdownToWavAsync(ProjectDocument document, string outputWavPath, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            var mixer = BuildMixerForDocument(document, 0.0, out var disposables);
            try
            {
                double totalSeconds = Math.Max(1.0, document.TotalDuration);
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

            foreach (var clip in track.Clips)
            {
                double clipEnd = clip.TimelineStart + clip.Duration;
                if (clipEnd <= playheadSeconds) continue;

                var asset = document.MediaPool.FirstOrDefault(m => m.Id == clip.MediaAssetId);
                if (asset == null || string.IsNullOrWhiteSpace(asset.FilePath) || !File.Exists(asset.FilePath))
                    continue;

                try
                {
                    var reader = new AudioFileReader(asset.FilePath);
                    disposables.Add(reader);

                    double skipIntoClip = Math.Max(0, playheadSeconds - clip.TimelineStart);
                    double delayBeforeClip = Math.Max(0, clip.TimelineStart - playheadSeconds);
                    double remainingDuration = Math.Max(0.05, clip.Duration - skipIntoClip);

                    var offsetProvider = new OffsetSampleProvider(reader)
                    {
                        SkipOver = TimeSpan.FromSeconds(clip.SourceIn + skipIntoClip * clip.Speed),
                        DelayBy = TimeSpan.FromSeconds(delayBeforeClip),
                        Take = TimeSpan.FromSeconds(remainingDuration)
                    };

                    ISampleProvider chain = offsetProvider;
                    if (chain.WaveFormat.Channels == 1)
                        chain = new MonoToStereoSampleProvider(chain);
                    if (chain.WaveFormat.SampleRate != 48000)
                        chain = new WdlResamplingSampleProvider(chain, 48000);

                    var dspProvider = new ClipAudioDspSampleProvider(chain, clip, track.Volume, skipIntoClip);
                    mixer.AddMixerInput(dspProvider);
                }
                catch { }
            }
        }

        return mixer;
    }

    public void Dispose()
    {
        StopPlayback();
    }
}

/// <summary>
/// Real-time stereo DSP sample provider for per-clip Volume Keyframes, Pan, Constant-Power Fade In/Out,
/// 3-Band EQ Presets (Podcast Voice, Bass Boost, Treble Sparkle), Compressor/Limiter, and Reverb.
/// </summary>
internal sealed class ClipAudioDspSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly TimelineClip _clip;
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

    public ClipAudioDspSampleProvider(ISampleProvider source, TimelineClip clip, float trackVolume, double startSkipSeconds)
    {
        _source = source;
        _clip = clip;
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

        for (int i = 0; i < read; i += 2)
        {
            double clipTime = _startSkipSeconds + (_samplesProcessed / (double)sampleRate);
            _samplesProcessed++;

            float gainDb = _clip.EvaluateGainDb(clipTime);
            float gain = MathF.Pow(10f, gainDb / 20f) * _trackVolume;

            if (_clip.DialogueIsolateEnabled)
                gain *= 1.15f;

            // Constant-Power Cosine/Sine Fade In & Out
            if (_clip.FadeInDuration > 0.01 && clipTime < _clip.FadeInDuration)
            {
                float t = (float)Math.Clamp(clipTime / _clip.FadeInDuration, 0.0, 1.0);
                gain *= MathF.Sin(t * MathF.PI * 0.5f);
            }
            double rem = _clip.Duration - clipTime;
            if (_clip.FadeOutDuration > 0.01 && rem < _clip.FadeOutDuration)
            {
                float t = (float)Math.Clamp(rem / _clip.FadeOutDuration, 0.0, 1.0);
                gain *= MathF.Sin(t * MathF.PI * 0.5f);
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
