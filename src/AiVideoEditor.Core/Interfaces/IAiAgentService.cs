using AiVideoEditor.Core.Models;

namespace AiVideoEditor.Core.Interfaces;

public class AiAgentExecutionResult
{
    public string AssistantMessage { get; set; } = string.Empty;
    public List<string> ExecutedActions { get; set; } = new();
    public bool TimelineModified { get; set; }
}

public interface IAiAgentService
{
    Task<AiAgentExecutionResult> ExecutePromptAsync(
        string userPrompt,
        ProjectDocument document,
        CancellationToken cancellationToken = default);
}
