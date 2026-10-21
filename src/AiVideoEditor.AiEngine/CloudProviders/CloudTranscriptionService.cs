using System.Net.Http.Headers;
using System.Text.Json;
using AiVideoEditor.AiEngine.Configuration;
using AiVideoEditor.AiEngine.LocalProviders;
using AiVideoEditor.Core.Interfaces;
using AiVideoEditor.Core.Models;
using AiVideoEditor.MediaEngine.Audio;

namespace AiVideoEditor.AiEngine.CloudProviders;

public sealed class CloudTranscriptionService : IAiTranscriptionService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly AiProviderConfig _config;
    private readonly LocalWhisperService _localFallback;

    public CloudTranscriptionService(AiProviderConfig config, LocalWhisperService localFallback)
    {
        _config = config;
        _localFallback = localFallback;
    }

    public async Task<List<TranscriptSegment>> TranscribeAsync(
        string mediaFilePath,
        string language = "auto",
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string apiKey = !string.IsNullOrWhiteSpace(_config.GroqApiKey) ? _config.GroqApiKey : _config.OpenAiApiKey;
        bool useGroq = !string.IsNullOrWhiteSpace(_config.GroqApiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return await _localFallback.TranscribeAsync(mediaFilePath, language, progress, cancellationToken);
        }

        progress?.Report(15);
        string wavPath = await AudioExtractor.Extract16kHzMonoWavAsync(mediaFilePath, cancellationToken);
        progress?.Report(35);

        try
        {
            string endpoint = useGroq
                ? "https://api.groq.com/openai/v1/audio/transcriptions"
                : "https://api.openai.com/v1/audio/transcriptions";
            string modelName = useGroq ? "whisper-large-v3-turbo" : "whisper-1";

            using var form = new MultipartFormDataContent();
            await using var fs = File.OpenRead(wavPath);
            var streamContent = new StreamContent(fs);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            form.Add(streamContent, "file", "audio.wav");
            form.Add(new StringContent(modelName), "model");
            form.Add(new StringContent("verbose_json"), "response_format");

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = form;

            using var response = await Http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var segments = new List<TranscriptSegment>();
            if (doc.RootElement.TryGetProperty("segments", out var segArray) && segArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in segArray.EnumerateArray())
                {
                    double start = item.TryGetProperty("start", out var s) ? s.GetDouble() : 0;
                    double end = item.TryGetProperty("end", out var e) ? e.GetDouble() : start + 2.0;
                    string text = item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        segments.Add(new TranscriptSegment
                        {
                            StartTime = start,
                            EndTime = end,
                            Text = text.Trim(),
                            Confidence = 0.98f
                        });
                    }
                }
            }
            else if (doc.RootElement.TryGetProperty("text", out var fullTextProp))
            {
                string fullText = fullTextProp.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(fullText))
                {
                    segments.Add(new TranscriptSegment
                    {
                        StartTime = 0,
                        EndTime = 4.0,
                        Text = fullText.Trim(),
                        Confidence = 0.98f
                    });
                }
            }

            progress?.Report(100);
            if (segments.Count > 0)
                return segments;
        }
        catch
        {
            // Fallback to local Whisper service
        }
        finally
        {
            try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { }
        }

        return await _localFallback.TranscribeAsync(mediaFilePath, language, progress, cancellationToken);
    }
}
