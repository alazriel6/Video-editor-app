using AiVideoEditor.AiEngine.Configuration;
using AiVideoEditor.Core.Interfaces;
using NAudio.Wave;

namespace AiVideoEditor.AiEngine.LocalProviders;

public sealed class LocalTtsAndSfxService : IAiSfxService
{
    public async Task<string> GenerateVoiceoverAsync(
        string text,
        string voicePreset = "Narrator",
        CancellationToken cancellationToken = default)
    {
        string outDir = Path.Combine(AiProviderConfig.GetAppDataFolder(), "generated_audio");
        Directory.CreateDirectory(outDir);
        string wavPath = Path.Combine(outDir, $"tts_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.wav");

        await Task.Run(() =>
        {
            int sampleRate = 44100;
            int wordCount = Math.Max(1, text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
            double duration = Math.Clamp(wordCount * 0.38, 1.2, 15.0);
            int totalSamples = (int)(sampleRate * duration);

            double basePitch = voicePreset.Contains("Female", StringComparison.OrdinalIgnoreCase) ? 215.0 : 125.0;
            var format = new WaveFormat(sampleRate, 16, 1);
            using var writer = new WaveFileWriter(wavPath, format);
            short[] buffer = new short[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                // Prosody & syllable envelope modulation based on character cadence
                int charIdx = (int)((t / duration) * Math.Max(1, text.Length));
                char c = text[Math.Clamp(charIdx, 0, Math.Max(0, text.Length - 1))];
                double syllableEnv = c == ' ' ? 0.08 : 0.85 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 5.2 * t));
                double pitchMod = basePitch * (1.0 + 0.08 * Math.Sin(2 * Math.PI * 1.7 * t));

                // Multi-harmonic formant synthesis
                double f1 = Math.Sin(2 * Math.PI * pitchMod * t);
                double f2 = 0.55 * Math.Sin(2 * Math.PI * (pitchMod * 2.0) * t);
                double f3 = 0.30 * Math.Sin(2 * Math.PI * (pitchMod * 3.0) * t);

                double sample = (f1 + f2 + f3) * 0.45 * syllableEnv;
                buffer[i] = (short)Math.Clamp(sample * 24000, short.MinValue, short.MaxValue);
            }

            writer.WriteSamples(buffer, 0, buffer.Length);
        }, cancellationToken);

        return wavPath;
    }

    public async Task<string> GenerateSoundEffectAsync(
        string prompt,
        double durationSeconds = 2.0,
        CancellationToken cancellationToken = default)
    {
        string outDir = Path.Combine(AiProviderConfig.GetAppDataFolder(), "generated_audio");
        Directory.CreateDirectory(outDir);
        string safeName = string.Concat(prompt.Where(char.IsLetterOrDigit));
        if (string.IsNullOrEmpty(safeName)) safeName = "sfx";
        string wavPath = Path.Combine(outDir, $"sfx_{safeName}_{Guid.NewGuid().ToString("N")[..6]}.wav");

        await Task.Run(() =>
        {
            int sampleRate = 44100;
            double duration = Math.Clamp(durationSeconds, 0.4, 8.0);
            int totalSamples = (int)(sampleRate * duration);
            var format = new WaveFormat(sampleRate, 16, 1);
            using var writer = new WaveFileWriter(wavPath, format);

            short[] buffer = new short[totalSamples];
            var rng = new Random(prompt.GetHashCode());
            string p = prompt.ToLowerInvariant();

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double progress = t / duration;
                double sample;

                if (p.Contains("whoosh") || p.Contains("swoosh") || p.Contains("wind"))
                {
                    // Filtered noise + swept frequency band
                    double env = Math.Sin(progress * Math.PI);
                    double sweepFreq = 120.0 + 980.0 * env;
                    double noise = (rng.NextDouble() * 2.0 - 1.0) * 0.45;
                    double tone = Math.Sin(2 * Math.PI * sweepFreq * t) * 0.55;
                    sample = (noise + tone) * Math.Pow(env, 1.5);
                }
                else if (p.Contains("glitch") || p.Contains("cyber") || p.Contains("digital"))
                {
                    int step = (int)(t * 36.0);
                    double steppedFreq = 180.0 + ((step * 271) % 1400);
                    sample = Math.Sign(Math.Sin(2 * Math.PI * steppedFreq * t)) * 0.4 * (1.0 - progress * 0.5);
                }
                else if (p.Contains("boom") || p.Contains("impact") || p.Contains("bass"))
                {
                    double env = Math.Exp(-t * 3.2);
                    double subFreq = 95.0 * Math.Exp(-t * 1.4) + 32.0;
                    sample = Math.Sin(2 * Math.PI * subFreq * t) * env * 0.9;
                }
                else
                {
                    // Bright notification / cinematic chime
                    double env = Math.Exp(-t * 2.8);
                    sample = (Math.Sin(2 * Math.PI * 523.25 * t) + 0.6 * Math.Sin(2 * Math.PI * 659.25 * t) + 0.4 * Math.Sin(2 * Math.PI * 783.99 * t)) * 0.35 * env;
                }

                buffer[i] = (short)Math.Clamp(sample * 26000, short.MinValue, short.MaxValue);
            }

            writer.WriteSamples(buffer, 0, buffer.Length);
        }, cancellationToken);

        return wavPath;
    }
}
