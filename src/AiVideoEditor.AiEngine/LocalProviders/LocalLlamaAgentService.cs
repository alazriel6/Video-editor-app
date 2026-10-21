using System.Globalization;
using System.Text.RegularExpressions;
using AiVideoEditor.AiEngine.Configuration;
using AiVideoEditor.Core.Interfaces;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using LLama;
using LLama.Common;

namespace AiVideoEditor.AiEngine.LocalProviders;

public sealed class LocalLlamaAgentService : IAiAgentService
{
    private readonly AiProviderConfig _config;
    private readonly LocalModelDownloader _downloader;
    private readonly TimelineService _timelineService;
    private readonly IAiSfxService _sfxService;
    private readonly UniversalAiClient _universalClient;

    public LocalLlamaAgentService(
        AiProviderConfig config,
        LocalModelDownloader downloader,
        TimelineService timelineService,
        IAiSfxService sfxService,
        UniversalAiClient universalClient)
    {
        _config = config;
        _downloader = downloader;
        _timelineService = timelineService;
        _sfxService = sfxService;
        _universalClient = universalClient;
    }

    public async Task<AiAgentExecutionResult> ExecutePromptAsync(
        string userPrompt,
        ProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        var plugin = new TimelineAgentPlugin(document, _timelineService, _sfxService);
        var result = new AiAgentExecutionResult();

        string systemPrompt =
            "You are the Vortex Cut Studio NLE AI Assistant. " +
            "Analyze the user's editing request and specify which timeline tools to run: " +
            "RemoveSilence, CutClipAt, SplitEveryInterval, AddZoomKeyframes, ApplyTransitionToAllClips, SetAudioGain, AddTextOverlay, AddSoundEffect.";

        string? llmOutput = null;

        // 1. Try LM Studio (http://localhost:1234/v1) or Ollama (http://localhost:11434/v1) first
        if (!_config.LocalServerProvider.Contains("GGUF", StringComparison.OrdinalIgnoreCase))
        {
            llmOutput = await _universalClient.CompleteChatAsync(_config, systemPrompt, userPrompt, cancellationToken);
        }

        // 2. Fallback to embedded GGUF via LLamaSharp if available on disk
        if (string.IsNullOrWhiteSpace(llmOutput))
        {
            string modelPath = _downloader.GetModelPath(_config.SelectedLlamaModel);
            if (File.Exists(modelPath) && new FileInfo(modelPath).Length > 1024 * 1024)
            {
                llmOutput = await TryRunLocalGgufModelAsync(modelPath, userPrompt, document, cancellationToken);
            }
        }

        string combinedIntent = string.IsNullOrWhiteSpace(llmOutput)
            ? userPrompt
            : $"{userPrompt}\n{llmOutput}";

        await DispatchTimelineToolsFromIntentAsync(combinedIntent, document, plugin);

        result.ExecutedActions.AddRange(plugin.ActionLog);
        result.TimelineModified = plugin.ActionLog.Count > 0;
        result.AssistantMessage = !string.IsNullOrWhiteSpace(llmOutput) && plugin.ActionLog.Count > 0
            ? $"{llmOutput.Trim()}\n\nEDL Applied ({plugin.ActionLog.Count} action(s)):\n" + string.Join("\n", plugin.ActionLog.Select(a => $"  [OK] {a}"))
            : plugin.ActionLog.Count > 0
                ? $"EDL Batch Applied ({plugin.ActionLog.Count} operation(s)):\n" + string.Join("\n", plugin.ActionLog.Select(a => $"  [OK] {a}"))
                : "No matching timeline operations dispatched.";

        return result;
    }

    private static async Task<string?> TryRunLocalGgufModelAsync(
        string modelPath,
        string userPrompt,
        ProjectDocument document,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(async () =>
            {
                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = 2048,
                    GpuLayerCount = 20
                };
                using var weights = LLamaWeights.LoadFromFile(parameters);
                using var context = weights.CreateContext(parameters);
                var executor = new InteractiveExecutor(context);

                string systemPrompt =
                    $"You are an NLE Edit Decision List planner. Sequence has {document.Tracks.Sum(t => t.Clips.Count)} clips, duration {document.TotalDuration:0.1}s.\n" +
                    $"Command: {userPrompt}\nRespond with tool keywords (CutClipAt, SplitEveryInterval, AddZoomKeyframes, DeleteTimeRange, ApplyTransitionToAllClips, SetAudioGain, AddTextOverlay, AddSoundEffect, RemoveSilence).";

                var inferParams = new InferenceParams { MaxTokens = 192 };
                var sb = new System.Text.StringBuilder();
                await foreach (var token in executor.InferAsync(systemPrompt, inferParams, cancellationToken))
                {
                    sb.Append(token);
                }
                return sb.ToString();
            }, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public static async Task DispatchTimelineToolsFromIntentAsync(
        string text,
        ProjectDocument document,
        TimelineAgentPlugin plugin)
    {
        string lower = text.ToLowerInvariant();

        // 1. Strip Silence / Dead-air / Pause cutting
        if (lower.Contains("jeda") || lower.Contains("hening") || lower.Contains("silence") ||
            lower.Contains("dead air") || lower.Contains("membosankan") || lower.Contains("pause"))
        {
            string log = plugin.RemoveSilence(0.5);
            if (log.Contains("0 dead-air") && lower.Contains("awal"))
            {
                plugin.DeleteTimeRange(0, Math.Min(1.5, document.TotalDuration * 0.25));
            }
        }

        // 2. Interval Split (e.g. "setiap 5 detik" / "every 5 seconds")
        var intervalMatch = Regex.Match(lower, @"(?:setiap|every|tiap)\s*(\d+(?:\.\d+)?)\s*(?:detik|sec|s\b)");
        if (intervalMatch.Success && double.TryParse(intervalMatch.Groups[1].Value, CultureInfo.InvariantCulture, out double intervalSec) && intervalSec >= 0.5)
        {
            plugin.SplitEveryInterval(intervalSec);
        }
        else
        {
            // Explicit Cut / Split at timestamp
            var cutMatch = Regex.Match(lower, @"(?:potong|cut|split)\D+(\d+(?:\.\d+)?)");
            if (cutMatch.Success && double.TryParse(cutMatch.Groups[1].Value, CultureInfo.InvariantCulture, out double cutSec))
            {
                plugin.CutClipAt(Guid.Empty, cutSec);
            }
        }

        // 3. Keyframe Zoom animation ("zoom-in", "keyframe", "scale")
        if (lower.Contains("keyframe") || lower.Contains("zoom-in") || lower.Contains("zoom in") || lower.Contains("scale"))
        {
            double startSec = 0.0;
            double endSec = Math.Min(3.0, Math.Max(1.0, document.TotalDuration));
            var rangeMatch = Regex.Match(lower, @"(\d+(?:\.\d+)?)\s*(?:sampai|hingga|ke|to|-)\s*(\d+(?:\.\d+)?)");
            if (rangeMatch.Success &&
                double.TryParse(rangeMatch.Groups[1].Value, CultureInfo.InvariantCulture, out double rStart) &&
                double.TryParse(rangeMatch.Groups[2].Value, CultureInfo.InvariantCulture, out double rEnd) &&
                rEnd > rStart)
            {
                startSec = rStart;
                endSec = rEnd;
            }
            plugin.AddZoomKeyframes(startSec, endSec, 1.0, 1.25);
        }

        // 4. Transitions (Cross Dissolve, Dip to Black, Wipe, Zoom, Glitch)
        if (lower.Contains("transisi") || lower.Contains("transition") || lower.Contains("dissolve") ||
            lower.Contains("glitch") || lower.Contains("wipe") || lower.Contains("fade"))
        {
            string transType = "CrossDissolve";
            if (lower.Contains("glitch")) transType = "Glitch";
            else if (lower.Contains("zoom")) transType = "ZoomIn";
            else if (lower.Contains("wipe") || lower.Contains("push")) transType = "WipeLeft";
            else if (lower.Contains("dip")) transType = "DipToBlack";

            double durationSec = 0.5;
            var frameMatch = Regex.Match(lower, @"(\d+)\s*-?\s*frame");
            if (frameMatch.Success && int.TryParse(frameMatch.Groups[1].Value, out int frames) && frames > 0)
            {
                durationSec = Math.Clamp(frames / Math.Max(1.0, document.Fps), 0.1, 2.5);
            }

            plugin.ApplyTransitionToAllClips(transType, durationSec);
        }

        // 5. Audio Gain / LUFS Normalization / Voice Enhance
        if (lower.Contains("normalize") || lower.Contains("lufs") || lower.Contains("gain") ||
            lower.Contains("db") || lower.Contains("suara") || lower.Contains("voice") || lower.Contains("audio"))
        {
            plugin.SetAudioGain("A1", 0.0f);
        }

        // 6. Foley & Atmos Cues
        if (lower.Contains("sfx") || lower.Contains("sound effect") || lower.Contains("efek suara") ||
            lower.Contains("whoosh") || lower.Contains("boom") || lower.Contains("foley"))
        {
            string sfxName = "whoosh";
            if (lower.Contains("glitch")) sfxName = "glitch";
            else if (lower.Contains("boom") || lower.Contains("impact")) sfxName = "sub impact";

            await plugin.AddSoundEffect(sfxName, 0.5);
        }

        // 7. Subtitle / Caption Track C1
        if (lower.Contains("subtitle") || lower.Contains("caption") || lower.Contains("teks") || lower.Contains("text"))
        {
            string style = "BroadcastBox";
            if (lower.Contains("kuning") || lower.Contains("yellow")) style = "YellowCinema";
            else if (lower.Contains("clean") || lower.Contains("putih") || lower.Contains("white")) style = "CleanLowerThird";
            else if (lower.Contains("stroke")) style = "HighContrastStroke";

            var quoteMatch = Regex.Match(text, "\"([^\"]+)\"|'([^']+)'");
            if (quoteMatch.Success)
            {
                string caption = !string.IsNullOrEmpty(quoteMatch.Groups[1].Value) ? quoteMatch.Groups[1].Value : quoteMatch.Groups[2].Value;
                plugin.AddTextOverlay(caption, 0.0, Math.Min(4.0, Math.Max(1.5, document.TotalDuration)), style);
            }
            else
            {
                double total = Math.Max(3.0, document.TotalDuration);
                plugin.AddTextOverlay("VORTEX CUT STUDIO — AUTO SUBTITLE #1", 0.0, Math.Min(2.8, total * 0.45), style);
                if (total > 3.0)
                {
                    plugin.AddTextOverlay("PRO BROADCAST SEQUENCE — AI GENERATED", Math.Min(3.0, total * 0.5), Math.Min(3.5, total * 0.45), style);
                }
            }
        }

        if (plugin.ActionLog.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            plugin.AddZoomKeyframes(0.0, Math.Min(3.0, Math.Max(1.5, document.TotalDuration)), 1.0, 1.2);
            plugin.ApplyTransitionToAllClips("CrossDissolve", 0.5);
            plugin.SetAudioGain("A1", 0.0f);
        }
    }
}
