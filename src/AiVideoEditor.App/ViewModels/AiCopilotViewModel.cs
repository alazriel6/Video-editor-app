using System.Collections.ObjectModel;
using AiVideoEditor.AiEngine;
using AiVideoEditor.Core.Commands;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiVideoEditor.App.ViewModels;

public class ConsoleExecutionEntry
{
    public string Header { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Timestamp { get; init; } = DateTime.Now.ToString("HH:mm:ss");
}

public partial class AiCopilotViewModel : ViewModelBase
{
    private readonly HybridAiRouter _automationRouter;
    private readonly UndoRedoManager _undoRedo;
    private readonly Func<ProjectDocument> _getDocument;
    private readonly Action<ProjectDocument> _restoreDocument;
    private readonly Action _onTimelineMutated;
    private readonly Action<string> _setStatus;

    public ObservableCollection<ConsoleExecutionEntry> Messages { get; } = new();

    [ObservableProperty]
    private string _promptInput = string.Empty;

    [ObservableProperty]
    private bool _isProcessing;

    public AiCopilotViewModel(
        HybridAiRouter automationRouter,
        UndoRedoManager undoRedo,
        Func<ProjectDocument> getDocument,
        Action<ProjectDocument> restoreDocument,
        Action onTimelineMutated,
        Action<string> setStatus)
    {
        _automationRouter = automationRouter;
        _undoRedo = undoRedo;
        _getDocument = getDocument;
        _restoreDocument = restoreDocument;
        _onTimelineMutated = onTimelineMutated;
        _setStatus = setStatus;

        Messages.Add(new ConsoleExecutionEntry
        {
            Header = "VORTEX AI ASSISTANT — HYBRID LOCAL (LM STUDIO) & CLOUD AGENT",
            Content = "Type natural language editing commands or click a Quick AI preset:\n  • \"Potong bagian yang hening di seluruh video, tambahkan subtitle otomatis warna kuning, dan beri efek zoom-in keyframe di awal video\"\n  • \"Buat animasi zoom-in halus (Scale 1.0 ke 1.25) dengan keyframe di detik ke-0 sampai ke-3\""
        });
    }

    [RelayCommand]
    public async Task SendPromptAsync(string? quickPrompt = null)
    {
        string prompt = !string.IsNullOrWhiteSpace(quickPrompt) ? quickPrompt : PromptInput;
        if (string.IsNullOrWhiteSpace(prompt) || IsProcessing) return;

        if (string.IsNullOrWhiteSpace(quickPrompt))
            PromptInput = string.Empty;

        Messages.Add(new ConsoleExecutionEntry
        {
            Header = "USER PROMPT",
            Content = $"> {prompt}"
        });

        IsProcessing = true;
        _setStatus("🤖 AI Edit Assistant: Planning and executing timeline & keyframe operations...");

        try
        {
            var doc = _getDocument();
            string beforeJson = ProjectSerializer.ToJson(doc);

            var execResult = await _automationRouter.ExecutePromptAsync(prompt, doc);

            if (execResult.TimelineModified)
            {
                string afterJson = ProjectSerializer.ToJson(doc);
                _undoRedo.ExecuteCommand(new DocumentSnapshotCommand(
                    $"AI Edit: {(prompt.Length > 24 ? prompt[..24] + "..." : prompt)}",
                    beforeJson,
                    afterJson,
                    _restoreDocument));
            }

            string engineLabel = _automationRouter.Config.AgentMode == AiEngine.Configuration.AiExecutionMode.Local
                ? _automationRouter.Config.LocalServerProvider
                : _automationRouter.Config.SelectedCloudLlmProvider;

            Messages.Add(new ConsoleExecutionEntry
            {
                Header = $"AI EXECUTION LOG ({engineLabel.ToUpperInvariant()})",
                Content = execResult.AssistantMessage
            });

            _onTimelineMutated();
            _setStatus(execResult.TimelineModified
                ? $"🤖 AI Edit Assistant: Executed {execResult.ExecutedActions.Count} operation(s) (Undo with Ctrl+Z)."
                : "🤖 AI Edit Assistant: Evaluation completed.");
        }
        catch (Exception ex)
        {
            Messages.Add(new ConsoleExecutionEntry
            {
                Header = "FAULT",
                Content = ex.Message
            });
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    public void ClearConsoleLog()
    {
        Messages.Clear();
    }
}
