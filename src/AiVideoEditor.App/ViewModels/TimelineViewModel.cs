using System.Collections.ObjectModel;
using System.Diagnostics;
using AiVideoEditor.AiEngine;
using AiVideoEditor.Core.Commands;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiVideoEditor.App.ViewModels;

public partial class TimelineViewModel : ViewModelBase
{
    private readonly TimelineService _timelineService;
    private readonly HybridAiRouter _automationRouter;
    private readonly UndoRedoManager _undoRedo;
    private readonly Action _refreshTimelineUi;
    private readonly Action<string> _setStatus;
    private ProjectDocument _document;
    private TimelineClip? _clipboardClip;

    [ObservableProperty]
    private double _playheadTime;

    [ObservableProperty]
    private string _formattedTimecode = "00:00:00:00";

    [ObservableProperty]
    private string _durationTimecode = "00:00:00:00";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _shuttleSpeed = 0.0;

    [ObservableProperty]
    private double _zoomPixelsPerSecond = 60.0;

    [ObservableProperty]
    private double _scrollOffsetSeconds = 0.0;

    [ObservableProperty]
    private double _maxScrollSeconds = 60.0;

    [ObservableProperty]
    private double _viewportDurationSeconds = 15.0;

    [ObservableProperty]
    private TimelineClip? _selectedClip;

    [ObservableProperty]
    private bool _isBusyTranscribing;

    [ObservableProperty]
    private string _activeToolName = "Select (V)";

    [ObservableProperty]
    private bool _snappingEnabled = true;

    [ObservableProperty]
    private bool _linkVideoAndAudio = true;

    public ObservableCollection<TranscriptSegment> ActiveTranscriptSegments { get; } = new();

    public TimelineViewModel(
        ProjectDocument document,
        TimelineService timelineService,
        HybridAiRouter automationRouter,
        UndoRedoManager undoRedo,
        Action refreshTimelineUi,
        Action<string> setStatus)
    {
        _document = document;
        _timelineService = timelineService;
        _automationRouter = automationRouter;
        _undoRedo = undoRedo;
        _refreshTimelineUi = refreshTimelineUi;
        _setStatus = setStatus;
        _linkVideoAndAudio = document.LinkVideoAndAudio;

        UpdateTimecodeDisplay();
    }

    public void SyncFromDocument(ProjectDocument document)
    {
        _document = document;
        LinkVideoAndAudio = document.LinkVideoAndAudio;
        RefreshTranscriptList();
        UpdateTimecodeDisplay();
    }

    partial void OnPlayheadTimeChanged(double value)
    {
        UpdateTimecodeDisplay();
    }

    partial void OnLinkVideoAndAudioChanged(bool value)
    {
        _document.LinkVideoAndAudio = value;
        _setStatus(value ? "Link Video & Audio enabled" : "Video & Audio unlinked");
    }

    public void UpdateTimecodeDisplay()
    {
        double fps = _document.Fps > 0 ? _document.Fps : 24.0;
        FormattedTimecode = TimecodeCalculator.SecondsToSmpte(PlayheadTime, fps);
        DurationTimecode = TimecodeCalculator.SecondsToSmpte(_document.TotalDuration, fps);
        MaxScrollSeconds = Math.Max(60.0, _document.TotalDuration + 30.0);
    }

    [RelayCommand]
    public void CutSelectedClip()
    {
        if (SelectedClip == null) return;
        _clipboardClip = SelectedClip.Clone();
        string label = SelectedClip.Label;
        DeleteSelectedClip();
        _setStatus($"Cut '{label}' to clipboard (Ctrl+X).");
    }

    [RelayCommand]
    public void CopySelectedClip()
    {
        if (SelectedClip == null) return;
        _clipboardClip = SelectedClip.Clone();
        _setStatus($"Copied '{SelectedClip.Label}' to clipboard (Ctrl+C).");
    }

    [RelayCommand]
    public void PasteClipAtPlayhead()
    {
        PasteClipAtTime(PlayheadTime);
    }

    public void PasteClipAtTime(double targetTime)
    {
        if (_clipboardClip == null)
        {
            _setStatus("Clipboard is empty. Copy or Cut a clip first (Ctrl+C / Ctrl+X).");
            return;
        }

        TimelineClip? pasted = null;
        _undoRedo.RecordSnapshot(_document, $"Paste '{_clipboardClip.Label}'", () =>
        {
            pasted = _timelineService.PasteClipAtTime(_document, _clipboardClip, targetTime);
        }, _ => { });

        if (pasted != null)
        {
            SelectedClip = pasted;
            _refreshTimelineUi();
            _setStatus($"Pasted '{pasted.Label}' at {TimecodeCalculator.SecondsToSmpte(targetTime, _document.Fps)} (Ctrl+V).");
        }
    }

    [RelayCommand]
    public void PasteAttributesToSelectedClip()
    {
        if (_clipboardClip == null || SelectedClip == null)
        {
            _setStatus("Copy a source clip first (Ctrl+C), then select a target clip to Paste Attributes (Ctrl+Alt+V).");
            return;
        }

        var target = SelectedClip;
        var src = _clipboardClip;
        _undoRedo.RecordSnapshot(_document, $"Paste Attributes to '{target.Label}'", () =>
        {
            target.CopyAttributesFrom(src);
        }, _ => { });

        _refreshTimelineUi();
        _setStatus($"Pasted Transform, Color & Effects from '{src.Label}' onto '{target.Label}' (Ctrl+Alt+V).");
    }

    [RelayCommand]
    public void DuplicateSelectedClip()
    {
        if (SelectedClip == null) return;
        TimelineClip? copy = null;
        Guid id = SelectedClip.Id;
        _undoRedo.RecordSnapshot(_document, $"Duplicate '{SelectedClip.Label}'", () =>
        {
            copy = _timelineService.DuplicateClip(_document, id);
        }, _ => { });

        if (copy != null)
        {
            SelectedClip = copy;
            _refreshTimelineUi();
            _setStatus($"Duplicated '{copy.Label}' (Ctrl+D).");
        }
    }

    [RelayCommand]
    public void SplitAtPlayhead()
    {
        int splitCount = 0;
        _undoRedo.RecordSnapshot(_document, $"Split at {FormattedTimecode}", () =>
        {
            splitCount = _timelineService.SplitAllClipsAtPlayhead(_document, PlayheadTime, SelectedClip?.Id);
            if (splitCount == 0 && SelectedClip != null)
            {
                splitCount = _timelineService.SplitAllClipsAtPlayhead(_document, PlayheadTime, null);
            }
        }, _ => { });

        _refreshTimelineUi();
        _setStatus(splitCount > 0
            ? $"Split {splitCount} clip(s) at {FormattedTimecode} (Ctrl+B)."
            : $"Move playhead inside a clip to split ({FormattedTimecode}).");
    }

    [RelayCommand]
    public void DeleteSelectedClip()
    {
        if (SelectedClip == null) return;
        string label = SelectedClip.Label;
        Guid id = SelectedClip.Id;
        Guid assetId = SelectedClip.MediaAssetId;
        double start = SelectedClip.TimelineStart;
        double dur = SelectedClip.Duration;

        _undoRedo.RecordSnapshot(_document, $"Delete '{label}'", () =>
        {
            foreach (var track in _document.Tracks.Where(t => !t.IsLocked))
            {
                var found = track.Clips.FirstOrDefault(c => c.Id == id);
                if (found != null)
                {
                    track.Clips.Remove(found);
                    break;
                }
            }

            if (_document.LinkVideoAndAudio && assetId != Guid.Empty)
            {
                foreach (var track in _document.Tracks.Where(t => !t.IsLocked))
                {
                    var linked = track.Clips.FirstOrDefault(c =>
                        c.MediaAssetId == assetId &&
                        Math.Abs(c.TimelineStart - start) < 0.08 &&
                        Math.Abs(c.Duration - dur) < 0.08);
                    if (linked != null)
                    {
                        track.Clips.Remove(linked);
                        break;
                    }
                }
            }
        }, _ => { });

        SelectedClip = null;
        _refreshTimelineUi();
        _setStatus($"Deleted clip '{label}' (Del).");
    }

    [RelayCommand]
    public void RippleDeleteSelectedClip()
    {
        if (SelectedClip != null)
        {
            string label = SelectedClip.Label;
            Guid id = SelectedClip.Id;

            _undoRedo.RecordSnapshot(_document, $"Ripple Delete '{label}'", () =>
            {
                _timelineService.RippleDeleteClip(_document, id);
            }, _ => { });

            SelectedClip = null;
            _refreshTimelineUi();
            _setStatus($"Ripple deleted '{label}' and closed gap (Shift+Del).");
        }
        else
        {
            CloseGapAtTime(PlayheadTime);
        }
    }

    [RelayCommand]
    public void CloseGapAtPlayhead()
    {
        CloseGapAtTime(PlayheadTime);
    }

    public void CloseGapAtTime(double timeSeconds)
    {
        bool closed = false;
        _undoRedo.RecordSnapshot(_document, "Close Timeline Gap", () =>
        {
            closed = _timelineService.CloseGapAtTime(_document, timeSeconds);
        }, _ => { });

        _refreshTimelineUi();
        _setStatus(closed
            ? "Closed empty timeline gap and shifted subsequent clips left."
            : "No empty gap found at the specified position.");
    }

    [RelayCommand]
    public void DetachAudioFromSelectedClip()
    {
        if (SelectedClip == null) return;
        Guid id = SelectedClip.Id;
        TimelineClip? audioClip = null;
        _undoRedo.RecordSnapshot(_document, $"Detach Audio from '{SelectedClip.Label}'", () =>
        {
            audioClip = _timelineService.DetachAudioToTrack(_document, id);
        }, _ => { });

        if (audioClip != null)
        {
            LinkVideoAndAudio = false;
            _refreshTimelineUi();
            _setStatus($"Detached audio from '{SelectedClip.Label}' onto Audio track.");
        }
    }

    [RelayCommand]
    public void FreezeFrameAtPlayhead()
    {
        TimelineClip? frozen = null;
        _undoRedo.RecordSnapshot(_document, $"Freeze Frame at {FormattedTimecode}", () =>
        {
            frozen = _timelineService.InsertFreezeFrameAtPlayhead(_document, PlayheadTime, 3.0);
        }, _ => { });

        if (frozen != null)
        {
            SelectedClip = frozen;
            _refreshTimelineUi();
            _setStatus($"Inserted 3.0s Freeze Frame at {FormattedTimecode}.");
        }
        else
        {
            _setStatus("Position playhead over an active video clip to insert a Freeze Frame.");
        }
    }

    [RelayCommand]
    public void CycleClipSpeed()
    {
        var clip = SelectedClip ?? _document.Tracks.SelectMany(t => t.Clips)
            .FirstOrDefault(c => PlayheadTime >= c.TimelineStart && PlayheadTime <= c.TimelineStart + c.Duration);

        if (clip == null)
        {
            _setStatus("Select a clip on the timeline to change Speed / Duration (Ctrl+R).");
            return;
        }

        double[] speeds = { 0.5, 1.0, 1.5, 2.0, 4.0 };
        int nextIdx = 0;
        for (int i = 0; i < speeds.Length; i++)
        {
            if (Math.Abs(clip.Speed - speeds[i]) < 0.08)
            {
                nextIdx = (i + 1) % speeds.Length;
                break;
            }
        }

        double newSpeed = speeds[nextIdx];
        double sourceSpan = Math.Max(0.2, clip.SourceOut - clip.SourceIn);
        clip.Speed = newSpeed;
        clip.Duration = Math.Max(0.2, Math.Round(sourceSpan / newSpeed, 3));

        SelectedClip = clip;
        _refreshTimelineUi();
        _setStatus($"Speed set to {newSpeed:0.##}x ({clip.Duration:0.00}s) on '{clip.Label}'.");
    }

    [RelayCommand]
    public void JumpToPreviousCutPoint()
    {
        PlayheadTime = _timelineService.GetPreviousCutPoint(_document, PlayheadTime);
        _refreshTimelineUi();
    }

    [RelayCommand]
    public void JumpToNextCutPoint()
    {
        PlayheadTime = _timelineService.GetNextCutPoint(_document, PlayheadTime);
        _refreshTimelineUi();
    }

    [RelayCommand]
    public void RevealSelectedClipInExplorer()
    {
        if (SelectedClip == null) return;
        var asset = _document.MediaPool.FirstOrDefault(m => m.Id == SelectedClip.MediaAssetId);
        if (asset == null || string.IsNullOrWhiteSpace(asset.FilePath) || !File.Exists(asset.FilePath))
        {
            _setStatus("Selected clip does not reference a disk media file.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{asset.FilePath}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public void AddVideoTrack()
    {
        int count = _document.Tracks.Count(t => t.Type == TrackType.Video) + 1;
        _document.Tracks.Insert(1, new TimelineTrack
        {
            Name = $"Video {count} (Overlay)",
            TrackCode = $"V{count}",
            Type = TrackType.Video
        });
        _refreshTimelineUi();
        _setStatus($"Added Video {count} track.");
    }

    [RelayCommand]
    public void AddAudioTrack()
    {
        int count = _document.Tracks.Count(t => t.Type == TrackType.Audio) + 1;
        _document.Tracks.Add(new TimelineTrack
        {
            Name = $"Audio {count}",
            TrackCode = $"A{count}",
            Type = TrackType.Audio
        });
        _refreshTimelineUi();
        _setStatus($"Added Audio {count} track.");
    }

    [RelayCommand]
    public void ToggleLinkAudio()
    {
        LinkVideoAndAudio = !LinkVideoAndAudio;
    }

    [RelayCommand]
    public void AddMarkerAtPlayhead()
    {
        int num = _document.Markers.Count + 1;
        _document.Markers.Add(new TimelineMarker
        {
            TimeSeconds = Math.Round(PlayheadTime, 3),
            Label = $"M{num}",
            ColorHex = "#F59E0B"
        });
        _refreshTimelineUi();
        _setStatus($"Added Marker M{num} at {FormattedTimecode} (M).");
    }

    [RelayCommand]
    public void EnhanceVoiceOnTimeline()
    {
        int count = 0;
        _undoRedo.RecordSnapshot(_document, "Clean Voice Noise", () =>
        {
            foreach (var track in _document.Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    clip.DialogueIsolateEnabled = true;
                    clip.EqPreset = "PodcastVoice";
                    clip.CompressorEnabled = true;
                    clip.GainDb = 0.0f;
                    count++;
                }
            }
        }, _ => { });

        _refreshTimelineUi();
        _setStatus($"Clean Voice Noise: Enhanced vocal clarity and normalized {count} clip(s).");
    }

    [RelayCommand]
    public void AddQuickTextClip()
    {
        InsertTextClipAtTime(PlayheadTime);
    }

    public void InsertTextClipAtTime(double timeSeconds)
    {
        TimelineClip? created = null;
        _undoRedo.RecordSnapshot(_document, "Add Text Clip", () =>
        {
            created = _timelineService.AddTextOverlay(_document, "Double-click to edit text", timeSeconds, 3.0, "BroadcastBox");
        }, _ => { });

        SelectedClip = created;
        _refreshTimelineUi();
        _setStatus($"Added Text clip on Subtitles track at {TimecodeCalculator.SecondsToSmpte(timeSeconds, _document.Fps)}.");
    }

    [RelayCommand]
    public async Task GenerateAutoCaptionsAsync()
    {
        if (IsBusyTranscribing) return;

        var targetClip = (SelectedClip != null && SelectedClip.MediaAssetId != Guid.Empty ? SelectedClip : null)
                         ?? _document.Tracks.Where(t => t.Type == TrackType.Video).SelectMany(t => t.Clips).FirstOrDefault(c => c.MediaAssetId != Guid.Empty)
                         ?? _document.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.MediaAssetId != Guid.Empty);

        var asset = (targetClip != null
                        ? _document.MediaPool.FirstOrDefault(m => m.Id == targetClip.MediaAssetId)
                        : null)
                    ?? _document.MediaPool.FirstOrDefault();

        if (asset == null)
        {
            _setStatus("Auto Subtitles: Import a video or audio file onto the timeline first.");
            return;
        }

        IsBusyTranscribing = true;
        _setStatus($"Auto Subtitles: Transcribing '{asset.Name}'...");

        try
        {
            var progress = new Progress<double>(p => _setStatus($"Auto Subtitles: Transcribing '{asset.Name}' ({p:0}%)..."));
            var segments = await _automationRouter.TranscribeAsync(asset.FilePath, "auto", progress);

            _undoRedo.RecordSnapshot(_document, "Auto Subtitles", () =>
            {
                asset.Transcript = segments;
                double offset = targetClip?.TimelineStart ?? 0.0;
                _timelineService.PopulateSubtitleTrackFromTranscript(_document, segments, offset, "BroadcastBox");
            }, _ => { });

            RefreshTranscriptList();
            _refreshTimelineUi();
            _setStatus($"Auto Subtitles: Generated {segments.Count} subtitle clips.");
        }
        finally
        {
            IsBusyTranscribing = false;
        }
    }

    [RelayCommand]
    public void RemoveSilence()
    {
        ExecuteStripSilenceWithParams(-38.0f, 400.0, 80.0, 120.0);
    }

    public int ExecuteStripSilenceWithParams(float thresholdDb, double minDurationMs, double padStartMs, double padEndMs)
    {
        int removedCount = 0;
        _undoRedo.RecordSnapshot(_document, "Remove Silence", () =>
        {
            removedCount = _timelineService.StripSilenceWithParameters(_document, thresholdDb, minDurationMs, padStartMs, padEndMs);
        }, _ => { });

        _refreshTimelineUi();
        _setStatus(removedCount > 0
            ? $"Remove Silence: Cut {removedCount} silent pause(s) below {thresholdDb:0} dB."
            : $"Remove Silence: No silent pauses found below {thresholdDb:0} dB.");
        return removedCount;
    }

    [RelayCommand]
    public void JumpToTranscriptSegment(TranscriptSegment? segment)
    {
        if (segment == null) return;
        PlayheadTime = segment.StartTime;
        _refreshTimelineUi();
        _setStatus($"Jumped to subtitle at {TimecodeCalculator.SecondsToSmpte(segment.StartTime, _document.Fps)}.");
    }

    [RelayCommand]
    public void DeleteTranscriptSegmentAndCutVideo(TranscriptSegment? segment)
    {
        if (segment == null) return;

        _undoRedo.RecordSnapshot(_document, $"Delete Dialogue Segment", () =>
        {
            segment.IsDeleted = true;
            _timelineService.DeleteTimeRangeAndRipple(_document, segment.StartTime, segment.EndTime);
        }, _ => { });

        RefreshTranscriptList();
        _refreshTimelineUi();
        _setStatus($"Deleted segment [{segment.StartTime:0.00}s - {segment.EndTime:0.00}s] and closed gap.");
    }

    public void RefreshTranscriptList()
    {
        ActiveTranscriptSegments.Clear();
        foreach (var asset in _document.MediaPool)
        {
            foreach (var seg in asset.Transcript.Where(s => !s.IsDeleted))
            {
                ActiveTranscriptSegments.Add(seg);
            }
        }
    }
}
