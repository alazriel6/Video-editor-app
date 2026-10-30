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

    private Guid? _lastCoalescedClipId;
    private string? _lastCoalescedKey;
    private DateTime _lastCoalescedUtc = DateTime.MinValue;
    private bool _coalescingSealed = true;

    public event Action? StateChanged;

    public bool IsProjectModified { get; private set; }
    public bool CanUndo => _undoList.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string LastActionDescription => _undoList.Count > 0 ? _undoList.Last!.Value.Description : "Ready";
    public string UndoTooltip => CanUndo ? $"Undo {_undoList.Last!.Value.Description} (Ctrl+Z)" : "Nothing to Undo";
    public string RedoTooltip => CanRedo ? $"Redo {_redoStack.Peek().Description} (Ctrl+Y)" : "Nothing to Redo";

    public void MarkSaved()
    {
        IsProjectModified = false;
        _coalescingSealed = true;
        StateChanged?.Invoke();
    }

    public void MarkModified()
    {
        if (!IsProjectModified)
        {
            IsProjectModified = true;
            StateChanged?.Invoke();
        }
    }

    public void SealCoalescing()
    {
        _coalescingSealed = true;
    }

    public void ExecuteCommand(ITimelineCommand command)
    {
        _coalescingSealed = true;
        command.Execute();
        PushUndo(command);
        _redoStack.Clear();
        IsProjectModified = true;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Records an already-applied command onto the Undo stack without re-invoking Execute().
    /// </summary>
    public void PushExecutedCommand(ITimelineCommand command)
    {
        _coalescingSealed = true;
        PushUndo(command);
        _redoStack.Clear();
        IsProjectModified = true;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Records or coalesces a live Inspector / Color / Text / Keyframe edit on a TimelineClip.
    /// Continuous slider/wheel/curve drags on the same property within a drag gesture coalesce into a single Undo step.
    /// </summary>
    public void RecordOrCoalesceClipEdit(
        TimelineClip targetClip,
        string coalesceKey,
        string description,
        TimelineClip beforeSnapshot,
        TimelineClip afterSnapshot,
        Action<TimelineClip>? onRestored = null)
    {
        var now = DateTime.UtcNow;
        bool canCoalesce = !_coalescingSealed &&
                           _lastCoalescedClipId == targetClip.Id &&
                           string.Equals(_lastCoalescedKey, coalesceKey, StringComparison.Ordinal) &&
                           (now - _lastCoalescedUtc).TotalMilliseconds < 650 &&
                           _undoList.Last?.Value is ClipStateEditCommand;

        if (canCoalesce && _undoList.Last?.Value is ClipStateEditCommand existingCmd)
        {
            existingCmd.UpdateAfterState(afterSnapshot);
            _lastCoalescedUtc = now;
            _redoStack.Clear();
            IsProjectModified = true;
            StateChanged?.Invoke();
            return;
        }

        var cmd = new ClipStateEditCommand(description, targetClip, beforeSnapshot, afterSnapshot, onRestored);
        PushUndo(cmd);
        _redoStack.Clear();
        _lastCoalescedClipId = targetClip.Id;
        _lastCoalescedKey = coalesceKey;
        _lastCoalescedUtc = now;
        _coalescingSealed = false;
        IsProjectModified = true;
        StateChanged?.Invoke();
    }

    public void RecordOrCoalesceClipEdit(
        TimelineClip targetClip,
        TimelineClip beforeSnapshot,
        TimelineClip afterSnapshot,
        string coalesceKey,
        string description,
        Action<TimelineClip>? onRestored = null)
    {
        RecordOrCoalesceClipEdit(targetClip, coalesceKey, description, beforeSnapshot, afterSnapshot, onRestored);
    }

    public void RecordSnapshot(ProjectDocument document, string description, Action mutation, Action<ProjectDocument> onRestore)
    {
        _coalescingSealed = true;
        string beforeJson = ProjectSerializer.ToJson(document);
        mutation();
        string afterJson = ProjectSerializer.ToJson(document);

        PushUndo(new DocumentSnapshotCommand(description, beforeJson, afterJson, onRestore));
        _redoStack.Clear();
        IsProjectModified = true;
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
        _coalescingSealed = true;
        if (_undoList.Count == 0) return;
        var cmd = _undoList.Last!.Value;
        _undoList.RemoveLast();
        cmd.Undo();
        _redoStack.Push(cmd);
        IsProjectModified = _undoList.Count > 0;
        StateChanged?.Invoke();
    }

    public void Redo()
    {
        _coalescingSealed = true;
        if (_redoStack.Count == 0) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        PushUndo(cmd);
        IsProjectModified = true;
        StateChanged?.Invoke();
    }

    public void Clear()
    {
        _coalescingSealed = true;
        _undoList.Clear();
        _redoStack.Clear();
        IsProjectModified = false;
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

public class ClipStateEditCommand : IEditCommand
{
    private readonly TimelineClip _targetClip;
    private readonly TimelineClip _beforeState;
    private TimelineClip _afterState;
    private readonly Action<TimelineClip>? _onRestored;

    public string Description { get; }

    public ClipStateEditCommand(
        string description,
        TimelineClip targetClip,
        TimelineClip beforeState,
        TimelineClip afterState,
        Action<TimelineClip>? onRestored = null)
    {
        Description = description;
        _targetClip = targetClip;
        _beforeState = beforeState;
        _afterState = afterState;
        _onRestored = onRestored;
    }

    public void UpdateAfterState(TimelineClip newAfterState)
    {
        _afterState = newAfterState;
    }

    public void Execute()
    {
        _targetClip.CopyStateFrom(_afterState);
        _onRestored?.Invoke(_targetClip);
    }

    public void Undo()
    {
        _targetClip.CopyStateFrom(_beforeState);
        _onRestored?.Invoke(_targetClip);
    }
}

public class KeyframeMutationCommand : IEditCommand
{
    private readonly TimelineClip _clip;
    private readonly TimelineClip _beforeSnapshot;
    private readonly TimelineClip _afterSnapshot;
    private readonly Action<TimelineClip>? _onRestored;

    public string Description { get; }

    public KeyframeMutationCommand(
        string description,
        TimelineClip clip,
        TimelineClip beforeSnapshot,
        TimelineClip afterSnapshot,
        Action<TimelineClip>? onRestored = null)
    {
        Description = description;
        _clip = clip;
        _beforeSnapshot = beforeSnapshot;
        _afterSnapshot = afterSnapshot;
        _onRestored = onRestored;
    }

    public void Execute()
    {
        _clip.CopyStateFrom(_afterSnapshot);
        _onRestored?.Invoke(_clip);
    }

    public void Undo()
    {
        _clip.CopyStateFrom(_beforeSnapshot);
        _onRestored?.Invoke(_clip);
    }
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
    private readonly TimelineTrack? _oldTrack;
    private readonly TimelineTrack? _newTrack;
    private readonly double _oldStart;
    private readonly double _newStart;

    public string Description => $"Move '{_clip.Label}'";

    public MoveClipCommand(TimelineClip clip, double oldStart, double newStart, TimelineTrack? oldTrack = null, TimelineTrack? newTrack = null)
    {
        _clip = clip;
        _oldStart = oldStart;
        _newStart = Math.Max(0, newStart);
        _oldTrack = oldTrack;
        _newTrack = newTrack;
    }

    public void Execute()
    {
        _clip.TimelineStart = _newStart;
        if (_oldTrack != null && _newTrack != null && _oldTrack != _newTrack)
        {
            _oldTrack.Clips.Remove(_clip);
            if (!_newTrack.Clips.Contains(_clip))
                _newTrack.Clips.Add(_clip);
        }
    }

    public void Undo()
    {
        _clip.TimelineStart = _oldStart;
        if (_oldTrack != null && _newTrack != null && _oldTrack != _newTrack)
        {
            _newTrack.Clips.Remove(_clip);
            if (!_oldTrack.Clips.Contains(_clip))
                _oldTrack.Clips.Add(_clip);
        }
    }
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

    public string Description => $"Trim '{_clip.Label}'";

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

public class AddClipCommand : IEditCommand
{
    private readonly TimelineTrack _track;
    private readonly TimelineClip _clip;

    public string Description => $"Add clip '{_clip.Label}'";

    public AddClipCommand(TimelineTrack track, TimelineClip clip)
    {
        _track = track;
        _clip = clip;
    }

    public void Execute()
    {
        if (!_track.Clips.Contains(_clip))
            _track.Clips.Add(_clip);
    }

    public void Undo()
    {
        _track.Clips.Remove(_clip);
    }
}

public class RemoveClipCommand : IEditCommand
{
    private readonly TimelineTrack _track;
    private readonly TimelineClip _clip;

    public string Description => $"Remove clip '{_clip.Label}'";

    public RemoveClipCommand(TimelineTrack track, TimelineClip clip)
    {
        _track = track;
        _clip = clip;
    }

    public void Execute()
    {
        _track.Clips.Remove(_clip);
    }

    public void Undo()
    {
        if (!_track.Clips.Contains(_clip))
            _track.Clips.Add(_clip);
    }
}
