using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;

namespace AiVideoEditor.Core.Commands;

public interface IEditCommand : ITimelineCommand
{
}

public interface ITimelineCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

public class UndoRedoManager
{
    private const int MaxStackDepth = 100;
    private readonly LinkedList<ITimelineCommand> _undoList = new();
    private readonly Stack<ITimelineCommand> _redoStack = new();

    public event Action? StateChanged;

    public bool CanUndo => _undoList.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string LastActionDescription => _undoList.Count > 0 ? _undoList.Last!.Value.Description : "Ready";
    public string UndoTooltip => CanUndo ? $"Undo {_undoList.Last!.Value.Description} (Ctrl+Z)" : "Nothing to Undo";
    public string RedoTooltip => CanRedo ? $"Redo {_redoStack.Peek().Description} (Ctrl+Y)" : "Nothing to Redo";

    public void ExecuteCommand(ITimelineCommand command)
    {
        command.Execute();
        PushUndo(command);
        _redoStack.Clear();
        StateChanged?.Invoke();
    }

    public void RecordSnapshot(ProjectDocument document, string description, Action mutation, Action<ProjectDocument> onRestore)
    {
        string beforeJson = ProjectSerializer.ToJson(document);
        mutation();
        string afterJson = ProjectSerializer.ToJson(document);

        PushUndo(new DocumentSnapshotCommand(description, beforeJson, afterJson, onRestore));
        _redoStack.Clear();
        StateChanged?.Invoke();
    }

    private void PushUndo(ITimelineCommand cmd)
    {
        _undoList.AddLast(cmd);
        while (_undoList.Count > MaxStackDepth)
        {
            _undoList.RemoveFirst();
        }
    }

    public void Undo()
    {
        if (_undoList.Count == 0) return;
        var cmd = _undoList.Last!.Value;
        _undoList.RemoveLast();
        cmd.Undo();
        _redoStack.Push(cmd);
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        PushUndo(cmd);
        StateChanged?.Invoke();
    }

    public void Clear()
    {
        _undoList.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke();
    }
}

public class PropertyChangeCommand<T> : IEditCommand
{
    private readonly Action<T> _setter;
    private readonly T _oldValue;
    private readonly T _newValue;

    public string Description { get; }

    public PropertyChangeCommand(string description, Action<T> setter, T oldValue, T newValue)
    {
        Description = description;
        _setter = setter;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public void Execute() => _setter(_newValue);
    public void Undo() => _setter(_oldValue);
}

public class DocumentSnapshotCommand : IEditCommand
{
    private readonly string _beforeJson;
    private readonly string _afterJson;
    private readonly Action<ProjectDocument> _restoreCallback;

    public string Description { get; }

    public DocumentSnapshotCommand(
        string description,
        string beforeJson,
        string afterJson,
        Action<ProjectDocument> restoreCallback)
    {
        Description = description;
        _beforeJson = beforeJson;
        _afterJson = afterJson;
        _restoreCallback = restoreCallback;
    }

    public void Execute()
    {
        var doc = ProjectSerializer.FromJson(_afterJson);
        if (doc != null) _restoreCallback(doc);
    }

    public void Undo()
    {
        var doc = ProjectSerializer.FromJson(_beforeJson);
        if (doc != null) _restoreCallback(doc);
    }
}

public class SplitClipCommand : IEditCommand
{
    private readonly TimelineTrack _track;
    private readonly TimelineClip _originalClip;
    private readonly double _splitTimelineTime;
    private readonly double _oldDuration;
    private readonly double _oldSourceOut;
    private TimelineClip? _rightClip;

    public string Description => $"Split Clip '{_originalClip.Label}'";

    public SplitClipCommand(TimelineTrack track, TimelineClip clip, double splitTimelineTime)
    {
        _track = track;
        _originalClip = clip;
        _splitTimelineTime = splitTimelineTime;
        _oldDuration = clip.Duration;
        _oldSourceOut = clip.SourceOut;
    }

    public void Execute()
    {
        double offsetInClip = _splitTimelineTime - _originalClip.TimelineStart;
        if (offsetInClip <= 0.05 || offsetInClip >= _originalClip.Duration - 0.05)
            return;

        double rightDuration = _originalClip.Duration - offsetInClip;
        double splitSourcePoint = _originalClip.SourceIn + (offsetInClip * _originalClip.Speed);

        _originalClip.Duration = offsetInClip;
        _originalClip.SourceOut = splitSourcePoint;

        _rightClip = _originalClip.Clone();
        _rightClip.TimelineStart = _splitTimelineTime;
        _rightClip.Duration = rightDuration;
        _rightClip.SourceIn = splitSourcePoint;
        _rightClip.SourceOut = _oldSourceOut;

        int index = _track.Clips.IndexOf(_originalClip);
        if (index >= 0 && index + 1 <= _track.Clips.Count)
            _track.Clips.Insert(index + 1, _rightClip);
        else
            _track.Clips.Add(_rightClip);
    }

    public void Undo()
    {
        if (_rightClip != null)
        {
            _track.Clips.Remove(_rightClip);
        }
        _originalClip.Duration = _oldDuration;
        _originalClip.SourceOut = _oldSourceOut;
    }
}

public class MoveClipCommand : IEditCommand
{
    private readonly TimelineClip _clip;
    private readonly double _oldStart;
    private readonly double _newStart;

    public string Description => $"Move Clip '{_clip.Label}'";

    public MoveClipCommand(TimelineClip clip, double oldStart, double newStart)
    {
        _clip = clip;
        _oldStart = oldStart;
        _newStart = Math.Max(0, newStart);
    }

    public void Execute() => _clip.TimelineStart = _newStart;
    public void Undo() => _clip.TimelineStart = _oldStart;
}

public class TrimClipCommand : IEditCommand
{
    private readonly TimelineClip _clip;
    private readonly double _oldStart;
    private readonly double _oldDuration;
    private readonly double _oldSourceIn;
    private readonly double _oldSourceOut;
    private readonly double _newStart;
    private readonly double _newDuration;
    private readonly double _newSourceIn;
    private readonly double _newSourceOut;

    public string Description => $"Trim Clip '{_clip.Label}'";

    public TrimClipCommand(
        TimelineClip clip,
        double oldStart, double oldDuration, double oldSourceIn, double oldSourceOut,
        double newStart, double newDuration, double newSourceIn, double newSourceOut)
    {
        _clip = clip;
        _oldStart = oldStart;
        _oldDuration = oldDuration;
        _oldSourceIn = oldSourceIn;
        _oldSourceOut = oldSourceOut;
        _newStart = newStart;
        _newDuration = newDuration;
        _newSourceIn = newSourceIn;
        _newSourceOut = newSourceOut;
    }

    public void Execute()
    {
        _clip.TimelineStart = _newStart;
        _clip.Duration = _newDuration;
        _clip.SourceIn = _newSourceIn;
        _clip.SourceOut = _newSourceOut;
    }

    public void Undo()
    {
        _clip.TimelineStart = _oldStart;
        _clip.Duration = _oldDuration;
        _clip.SourceIn = _oldSourceIn;
        _clip.SourceOut = _oldSourceOut;
    }
}
