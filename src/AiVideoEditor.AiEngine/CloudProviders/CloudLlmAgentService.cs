using AiVideoEditor.AiEngine.Configuration;
using AiVideoEditor.AiEngine.LocalProviders;
using AiVideoEditor.Core.Interfaces;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AiVideoEditor.AiEngine.CloudProviders;

public sealed class CloudLlmAgentService : IAiAgentService
{
    private readonly AiProviderConfig _config;
    private readonly TimelineService _timelineService;
    private readonly IAiSfxService _sfxService;
    private readonly UniversalAiClient _universalClient;

    public CloudLlmAgentService(
        AiProviderConfig config,
        TimelineService timelineService,
        IAiSfxService sfxService,
        UniversalAiClient universalClient)
    {
        _config = config;
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

        string provider = _config.SelectedCloudLlmProvider ?? "OpenAI";

        if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_config.OpenAiApiKey))
        {
            try
            {
                var builder = Kernel.CreateBuilder();
                string modelId = string.IsNullOrWhiteSpace(_config.SelectedCloudLlmModel)
                    ? "gpt-4o-mini"
                    : _config.SelectedCloudLlmModel;

                builder.AddOpenAIChatCompletion(modelId, _config.OpenAiApiKey);
                var kernel = builder.Build();
                kernel.Plugins.AddFromObject(plugin, "TimelineTools");

                var chatService = kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();
                history.AddSystemMessage(
                    "You are the Edit Decision Automation planner inside Vortex Cut Studio. " +
                    "Call the provided TimelineTools functions (CutClipAt, SplitEveryInterval, AddZoomKeyframes, DeleteTimeRange, ApplyTransitionToAllClips, SetAudioGain, AddTextOverlay, AddSoundEffect, RemoveSilence) " +
                    "to execute the batch edit instruction on the sequence.");
                history.AddUserMessage(
                    $"Sequence: '{document.ProjectName}', Duration: {document.TotalDuration:0.1}s, Tracks: {document.Tracks.Count}.\n" +
                    $"Command: {userPrompt}");

                var settings = new OpenAIPromptExecutionSettings
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
                };

                var response = await chatService.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);
                result.ExecutedActions.AddRange(plugin.ActionLog);
                result.TimelineModified = plugin.ActionLog.Count > 0;
                result.AssistantMessage = !string.IsNullOrWhiteSpace(response.Content)
                    ? response.Content!
                    : $"EDL Batch Applied ({plugin.ActionLog.Count} operation(s)).";

                if (result.TimelineModified)
                    return result;
            }
            catch { }
        }

        // Universal Cloud Call (Google Gemini, Anthropic Claude, Groq, or custom OpenAI)
        string systemPrompt =
            "You are the Vortex Cut Studio AI NLE Assistant. " +
            "Describe the edits and specify which timeline tools to apply: RemoveSilence, CutClipAt, SplitEveryInterval, AddZoomKeyframes, ApplyTransitionToAllClips, SetAudioGain, AddTextOverlay, AddSoundEffect.";

        string? cloudReply = await _universalClient.CompleteChatAsync(_config, systemPrompt, userPrompt, cancellationToken);
        string combined = string.IsNullOrWhiteSpace(cloudReply) ? userPrompt : $"{userPrompt}\n{cloudReply}";

        await LocalLlamaAgentService.DispatchTimelineToolsFromIntentAsync(combined, document, plugin);
        result.ExecutedActions.AddRange(plugin.ActionLog);
        result.TimelineModified = plugin.ActionLog.Count > 0;
        result.AssistantMessage = !string.IsNullOrWhiteSpace(cloudReply)
            ? $"{cloudReply.Trim()}\n\nEDL Batch Applied ({plugin.ActionLog.Count} operation(s)):\n" + string.Join("\n", plugin.ActionLog.Select(a => $"  [OK] {a}"))
            : plugin.ActionLog.Count > 0
                ? $"EDL Batch Applied ({plugin.ActionLog.Count} operation(s)):\n" + string.Join("\n", plugin.ActionLog.Select(a => $"  [OK] {a}"))
                : "No timeline operations required.";

        return result;
    }
}
