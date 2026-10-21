using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiVideoEditor.AiEngine.Configuration;

namespace AiVideoEditor.AiEngine;

public sealed class UniversalAiClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<(bool Connected, List<string> Models, string StatusMessage)> TestConnectionAndFetchModelsAsync(
        string providerName,
        string baseUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var models = new List<string>();
        try
        {
            string trimmedBase = (baseUrl ?? string.Empty).Trim().TrimEnd('/');

            if (providerName.Contains("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                    return (false, models, "Google Gemini API Key is empty.");

                string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}";
                using var resp = await Http.GetAsync(url, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                    return (false, models, $"Gemini API HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}");

                string json = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        if (item.TryGetProperty("name", out var nameProp))
                        {
                            string name = nameProp.GetString() ?? "";
                            if (name.StartsWith("models/")) name = name["models/".Length..];
                            if (name.Contains("gemini", StringComparison.OrdinalIgnoreCase))
                                models.Add(name);
                        }
                    }
                }
                if (models.Count == 0)
                {
                    models.AddRange(new[] { "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash" });
                }
                return (true, models, $"Connected to Google Gemini ({models.Count} models available).");
            }

            if (string.IsNullOrWhiteSpace(trimmedBase))
                trimmedBase = "http://localhost:1234/v1";

            string modelsUrl = trimmedBase.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
                ? trimmedBase
                : $"{trimmedBase}/models";

            using var req = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await Http.SendAsync(req, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in dataArr.EnumerateArray())
                    {
                        if (m.TryGetProperty("id", out var idProp))
                        {
                            string? id = idProp.GetString();
                            if (!string.IsNullOrWhiteSpace(id))
                                models.Add(id);
                        }
                    }
                }
                else if (doc.RootElement.TryGetProperty("models", out var modelsArr) && modelsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in modelsArr.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var nameProp))
                        {
                            string? id = nameProp.GetString();
                            if (!string.IsNullOrWhiteSpace(id))
                                models.Add(id);
                        }
                    }
                }

                if (models.Count == 0)
                    models.Add("local-model");

                return (true, models, $"Connected to {providerName} ({models.Count} model(s) loaded).");
            }

            // Fallback check for Ollama /api/tags
            if (trimmedBase.Contains("11434"))
            {
                string ollamaTags = trimmedBase.Replace("/v1", "") + "/api/tags";
                using var ollamaResp = await Http.GetAsync(ollamaTags, cancellationToken);
                if (ollamaResp.IsSuccessStatusCode)
                {
                    string body = await ollamaResp.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("models", out var arr))
                    {
                        foreach (var m in arr.EnumerateArray())
                        {
                            if (m.TryGetProperty("name", out var n))
                                models.Add(n.GetString() ?? "llama3");
                        }
                    }
                    return (true, models, $"Connected to Ollama ({models.Count} model(s) found).");
                }
            }

            return (false, models, $"Server responded HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) at {modelsUrl}");
        }
        catch (Exception ex)
        {
            return (false, models, $"Connection failed ({providerName}): {ex.Message}");
        }
    }

    public async Task<string?> CompleteChatAsync(
        AiProviderConfig config,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (config.AgentMode == AiExecutionMode.Local)
            {
                string baseUrl = config.LocalServerProvider.Contains("Ollama", StringComparison.OrdinalIgnoreCase)
                    ? config.OllamaBaseUrl
                    : config.LmStudioBaseUrl;
                string model = string.IsNullOrWhiteSpace(config.SelectedLocalServerModel)
                    ? "local-model"
                    : config.SelectedLocalServerModel;

                return await CallOpenAiCompatibleChatAsync(baseUrl, null, model, systemPrompt, userPrompt, cancellationToken);
            }
            else
            {
                string provider = config.SelectedCloudLlmProvider ?? "OpenAI";
                if (provider.Contains("Gemini", StringComparison.OrdinalIgnoreCase))
                {
                    return await CallGoogleGeminiAsync(
                        config.GeminiApiKey,
                        string.IsNullOrWhiteSpace(config.SelectedCloudLlmModel) ? "gemini-2.5-flash" : config.SelectedCloudLlmModel,
                        systemPrompt,
                        userPrompt,
                        cancellationToken);
                }
                else if (provider.Contains("Claude", StringComparison.OrdinalIgnoreCase))
                {
                    return await CallAnthropicClaudeAsync(
                        config.ClaudeApiKey,
                        string.IsNullOrWhiteSpace(config.SelectedCloudLlmModel) ? "claude-3-7-sonnet-latest" : config.SelectedCloudLlmModel,
                        systemPrompt,
                        userPrompt,
                        cancellationToken);
                }
                else if (provider.Contains("Groq", StringComparison.OrdinalIgnoreCase))
                {
                    return await CallOpenAiCompatibleChatAsync(
                        "https://api.groq.com/openai/v1",
                        config.GroqApiKey,
                        string.IsNullOrWhiteSpace(config.SelectedCloudLlmModel) ? "llama-3.3-70b-versatile" : config.SelectedCloudLlmModel,
                        systemPrompt,
                        userPrompt,
                        cancellationToken);
                }
                else
                {
                    string openAiBase = string.IsNullOrWhiteSpace(config.CustomOpenAiBaseUrl)
                        ? "https://api.openai.com/v1"
                        : config.CustomOpenAiBaseUrl;
                    return await CallOpenAiCompatibleChatAsync(
                        openAiBase,
                        config.OpenAiApiKey,
                        string.IsNullOrWhiteSpace(config.SelectedCloudLlmModel) ? "gpt-4o-mini" : config.SelectedCloudLlmModel,
                        systemPrompt,
                        userPrompt,
                        cancellationToken);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> CallOpenAiCompatibleChatAsync(
        string baseUrl,
        string? apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        string endpoint = baseUrl.Trim().TrimEnd('/');
        if (!endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            endpoint += "/chat/completions";

        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.2,
            max_tokens = 512
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var resp = await Http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode) return null;

        string json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content))
            {
                return content.GetString();
            }
        }
        return null;
    }

    private static async Task<string?> CallGoogleGeminiAsync(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        string cleanModel = model.StartsWith("models/") ? model["models/".Length..] : model;
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        var payload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 512
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var resp = await Http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode) return null;

        string json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0)
        {
            var cand = candidates[0];
            if (cand.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textProp))
            {
                return textProp.GetString();
            }
        }
        return null;
    }

    private static async Task<string?> CallAnthropicClaudeAsync(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        string url = "https://api.anthropic.com/v1/messages";

        var payload = new
        {
            model,
            max_tokens = 512,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await Http.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode) return null;

        string json = await resp.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("content", out var contentArr) &&
            contentArr.ValueKind == JsonValueKind.Array &&
            contentArr.GetArrayLength() > 0 &&
            contentArr[0].TryGetProperty("text", out var textProp))
        {
            return textProp.GetString();
        }
        return null;
    }
}
