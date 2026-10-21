using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiVideoEditor.AiEngine.Configuration;
using AiVideoEditor.AiEngine.LocalProviders;
using AiVideoEditor.Core.Interfaces;

namespace AiVideoEditor.AiEngine.CloudProviders;

public sealed class ElevenLabsAudioService : IAiSfxService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly AiProviderConfig _config;
    private readonly LocalTtsAndSfxService _localFallback;

    public ElevenLabsAudioService(AiProviderConfig config, LocalTtsAndSfxService localFallback)
    {
        _config = config;
        _localFallback = localFallback;
    }

    public async Task<string> GenerateVoiceoverAsync(
        string text,
        string voicePreset = "Narrator",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.ElevenLabsApiKey))
        {
            return await _localFallback.GenerateVoiceoverAsync(text, voicePreset, cancellationToken);
        }

        try
        {
            // Default ElevenLabs Rachel / Adam voice ID
            string voiceId = voicePreset.Contains("Female", StringComparison.OrdinalIgnoreCase)
                ? "21m00Tcm4TlvDq8ikWAM"
                : "pNInz6obpgDQGcFmaJgB";

            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("xi-api-key", _config.ElevenLabsApiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

            var payload = new
            {
                text,
                model_id = "eleven_multilingual_v2"
            };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, cancellationToken);
            resp.EnsureSuccessStatusCode();

            string outDir = Path.Combine(AiProviderConfig.GetAppDataFolder(), "generated_audio");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"elevenlabs_tts_{Guid.NewGuid():N}.mp3");
            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(outPath, bytes, cancellationToken);
            return outPath;
        }
        catch
        {
            return await _localFallback.GenerateVoiceoverAsync(text, voicePreset, cancellationToken);
        }
    }

    public async Task<string> GenerateSoundEffectAsync(
        string prompt,
        double durationSeconds = 2.0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.ElevenLabsApiKey))
        {
            return await _localFallback.GenerateSoundEffectAsync(prompt, durationSeconds, cancellationToken);
        }

        try
        {
            string url = "https://api.elevenlabs.io/v1/sound-generation";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("xi-api-key", _config.ElevenLabsApiKey);

            var payload = new
            {
                text = prompt,
                duration_seconds = Math.Clamp(durationSeconds, 0.5, 10.0)
            };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, cancellationToken);
            resp.EnsureSuccessStatusCode();

            string outDir = Path.Combine(AiProviderConfig.GetAppDataFolder(), "generated_audio");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, $"elevenlabs_sfx_{Guid.NewGuid():N}.mp3");
            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(outPath, bytes, cancellationToken);
            return outPath;
        }
        catch
        {
            return await _localFallback.GenerateSoundEffectAsync(prompt, durationSeconds, cancellationToken);
        }
    }
}
