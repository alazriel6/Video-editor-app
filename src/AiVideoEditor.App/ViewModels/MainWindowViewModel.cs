using Avalonia.Threading;
using AiVideoEditor.AiEngine;
using AiVideoEditor.AiEngine.Configuration;
using AiVideoEditor.Core.Commands;
using AiVideoEditor.Core.Interfaces;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using AiVideoEditor.MediaEngine.Audio;
using AiVideoEditor.MediaEngine.Decoding;
using AiVideoEditor.MediaEngine.Exporting;
using AiVideoEditor.MediaEngine.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiVideoEditor.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    public ProjectDocument Document { get; private set; }
    public UndoRedoManager UndoRedo { get; } = new();
    public TimelineService TimelineService { get; } = new();
    public VideoFrameReader FrameReader { get; } = new();
    public CompositorEngine Compositor { get; }
    public MultiTrackAudioPlayer AudioPlayer { get; } = new();
    public HybridAiRouter AiRouter { get; }
    public VideoExporter VideoExporter { get; }
    public AutoSaveService AutoSave { get; }

    public MediaBinViewModel MediaBin { get; }
    public TimelineViewModel Timeline { get; }
    public InspectorViewModel Inspector { get; }
    public AiCopilotViewModel AiCopilot { get; }
    public SettingsViewModel Settings { get; }

    private readonly DispatcherTimer _playbackTimer;
    private DateTime _lastTickUtc;
    private Action? _pendingUnsavedProceedAction;

    public event Action? RequestCanvasRefresh;
    public event Action? RequestTimelineRefresh;
    public event Action<double>? RequestPlaybackTick;
    public event Action<float, float>? RequestVuMeterRefresh;

    [ObservableProperty]
    private string _projectName = "Vortex_Master_Sequence";

    [ObservableProperty]
    private string? _currentProjectFilePath;

    [ObservableProperty]
    private bool _isProjectModified;

    public string WindowTitle =>
        $"Vortex Cut Studio  —  {ProjectName}{(IsProjectModified ? " *" : "")}" +
        (!string.IsNullOrWhiteSpace(CurrentProjectFilePath) ? $"  ({Path.GetFileName(CurrentProjectFilePath)})" : "");

    [ObservableProperty]
    private int _missingMediaCount;

    public bool HasMissingMedia => MissingMediaCount > 0;

    [ObservableProperty]
    private bool _isUnsavedChangesModalOpen;

    [ObservableProperty]
    private string _unsavedChangesMessage = "You have unsaved changes in the current project. Save before continuing?";

    [ObservableProperty]
    private string _statusMessage = "Vortex Cut Studio Ready  •  Import Media (Ctrl+I) or Drag & Drop Video/Audio";

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    [ObservableProperty]
    private string _undoTooltip = "Undo (Ctrl+Z)";

    [ObservableProperty]
    private string _redoTooltip = "Redo (Ctrl+Y)";

    [ObservableProperty]
    private bool _autoDuckingEnabled = false;

    [ObservableProperty]
    private bool _showSafeMargins = false;

    [ObservableProperty]
    private string _selectedAspectRatio = "16:9 (Landscape)";

    [ObservableProperty]
    private string _selectedPreviewQuality = "1/2 (540p Fast)";

    [ObservableProperty]
    private double _previewQualityScale = 0.5;

    public IReadOnlyList<string> AspectRatioOptions { get; } = new[]
    {
        "16:9 (Landscape)",
        "9:16 (Vertical)",
        "1:1 (Square)"
    };

    public IReadOnlyList<string> PreviewQualityOptions { get; } = new[]
    {
        "Full (1080p)",
        "1/2 (540p Fast)",
        "1/4 (270p Draft)"
    };

    // Source Monitor & Program Monitor Dual State
    [ObservableProperty]
    private bool _isSourceMonitorActive = false;

    [ObservableProperty]
    private MediaAsset? _sourceMonitorAsset;

    [ObservableProperty]
    private double _sourceMonitorTime = 0.0;

    [ObservableProperty]
    private string _sourceMonitorTimecode = "00:00:00:00";

    [ObservableProperty]
    private string _sourceInOutSummary = "IN 00:00:00:00  •  OUT 00:00:00:00";

    // Left & Right Quadrant Tabs
    [ObservableProperty]
    private int _leftPanelTabIndex;

    [ObservableProperty]
    private int _rightPanelTabIndex;

    // Modals (AI Studio Settings, Strip Silence, Speed/Duration, Deliver/Export)
    [ObservableProperty]
    private bool _isSettingsModalOpen;

    [ObservableProperty]
    private bool _isStripSilenceModalOpen;

    [ObservableProperty]
    private bool _isSpeedDurationModalOpen;

    [ObservableProperty]
    private bool _isExportModalOpen;

    // Speed / Duration Modal Properties (Ctrl+R)
    [ObservableProperty]
    private double _modalClipSpeedPercent = 100.0;

    [ObservableProperty]
    private double _modalClipDurationSeconds = 5.0;

    [ObservableProperty]
    private bool _modalClipReversePlayback;

    // Strip Silence Modal Parameters
    [ObservableProperty]
    private float _stripSilenceThresholdDb = -38.0f;

    [ObservableProperty]
    private double _stripSilenceMinDurationMs = 400.0;

    [ObservableProperty]
    private double _stripSilencePadStartMs = 80.0;

    [ObservableProperty]
    private double _stripSilencePadEndMs = 120.0;

    // Deliver / Export Modal Properties (Section 4)
    private CancellationTokenSource? _exportCts;

    [ObservableProperty]
    private string _selectedExportPreset = "YouTube 1080p MP4 (H.264)";

    [ObservableProperty]
    private string _exportResolution = "1080p (1920x1080)";

    [ObservableProperty]
    private double _exportFps = 30.0;

    [ObservableProperty]
    private string _selectedHardwareEncoder = "libx264";

    [ObservableProperty]
    private string _selectedExportRange = "Entire Timeline";

    [ObservableProperty]
    private string _exportOutputPath = string.Empty;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private double _exportProgressPercent;

    [ObservableProperty]
    private string _exportProgressStatus = "Ready to render master sequence";

    public IReadOnlyList<string> ExportPresets { get; } = new[]
    {
        "YouTube 1080p MP4 (H.264)",
        "4K UHD Master (3840x2160)",
        "Vertical Shorts 9:16 (1080x1920)",
        "Apple ProRes 422 (MOV)",
        "Audio Only (WAV 48kHz)"
    };

    public IReadOnlyList<string> ExportResolutions { get; } = new[]
    {
        "1080p (1920x1080)",
        "4K UHD (3840x2160)",
        "Vertical 9:16 (1080x1920)",
        "720p HD (1280x720)"
    };

    public IReadOnlyList<string> ExportRanges { get; } = new[]
    {
        "Entire Timeline",
        "Mark In / Out Range (I / O)"
    };

    public IReadOnlyList<double> ExportFpsOptions { get; } = new[] { 23.976, 24.0, 25.0, 29.97, 30.0, 60.0 };

    public IReadOnlyList<string> HardwareEncoders { get; } = FFmpegExportEngine.ProbeAvailableEncoders();

    public MainWindowViewModel()
    {
        var config = AiProviderConfig.Load();
        AiRouter = new HybridAiRouter(config, TimelineService);
        Compositor = new CompositorEngine(FrameReader, AiRouter);
        VideoExporter = new VideoExporter(Compositor, AudioPlayer);

        Document = CreateInitialStarterProject();
        ProjectName = Document.ProjectName;
        AutoDuckingEnabled = Document.AutoDuckingEnabled;

        ExportOutputPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Vortex_Master_Export.mp4");

        UndoRedo.StateChanged += () =>
        {
            CanUndo = UndoRedo.CanUndo;
            CanRedo = UndoRedo.CanRedo;
            UndoTooltip = UndoRedo.UndoTooltip;
            RedoTooltip = UndoRedo.RedoTooltip;
            IsProjectModified = UndoRedo.IsProjectModified;
            OnPropertyChanged(nameof(WindowTitle));
        };

        Timeline = new TimelineViewModel(
            Document,
            TimelineService,
            AiRouter,
            UndoRedo,
            NotifyTimelineAndCanvasChanged,
            msg => StatusMessage = msg);

        Inspector = new InspectorViewModel(
            () => NotifyTimelineAndCanvasChanged(),
            (transType, dur) =>
            {
                UndoRedo.RecordSnapshot(Document, $"Apply '{transType}' to V-Tracks", () =>
                {
                    TimelineService.ApplyTransitionToAllClips(Document, transType, dur);
                }, RestoreProjectDocument);
                NotifyTimelineAndCanvasChanged();
                StatusMessage = $"Applied '{transType}' ({dur:0.00}s) transition blocks across video cuts.";
            },
            () => Timeline.PlayheadTime,
            newTime =>
            {
                Timeline.PlayheadTime = Math.Max(0, newTime);
                NotifyTimelineAndCanvasChanged();
            },
            msg => StatusMessage = msg,
            UndoRedo,
            (transType, dur) =>
            {
                var v1 = Document.Tracks.FirstOrDefault(t => t.Type == TrackType.Video) ?? Document.Tracks.FirstOrDefault();
                if (v1 == null) return;
                var block = TimelineService.AddStandaloneTransitionBlock(Document, transType, Timeline.PlayheadTime, dur, v1);
                UndoRedo.ExecuteCommand(new AddClipCommand(v1, block));
                Timeline.SelectedClip = block;
                Inspector?.BindClip(block);
                NotifyTimelineAndCanvasChanged();
                StatusMessage = $"Inserted standalone {TimelineService.FormatTransitionTitle(transType)} transition block ({dur:0.1}s).";
            },
            () => { if (!Timeline.IsPlaying) TogglePlayPause(); });

        MediaBin = new MediaBinViewModel(
            Document,
            FrameReader,
            TimelineService,
            AiRouter,
            UndoRedo,
            NotifyTimelineAndCanvasChanged,
            clip =>
            {
                IsSourceMonitorActive = false;
                SourceMonitorAsset = Document.MediaPool.LastOrDefault();
                UpdateSourceMonitorReadouts();
                Timeline.SelectedClip = clip;
                Inspector.BindClip(clip);
                NotifyTimelineAndCanvasChanged();
            },
            msg => StatusMessage = msg,
            ApplyEffectLibraryCode);

        AiCopilot = new AiCopilotViewModel(
            AiRouter,
            UndoRedo,
            () => Document,
            RestoreProjectDocument,
            () =>
            {
                Inspector.SyncPlayheadValues();
                NotifyTimelineAndCanvasChanged();
            },
            msg => StatusMessage = msg);

        Settings = new SettingsViewModel(config, AiRouter.Downloader, AiRouter.UniversalClient, msg => StatusMessage = msg);

        UpdateSourceMonitorReadouts();

        // 60 FPS Master Clock & VU Meter Loop
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _playbackTimer.Tick += OnPlaybackTick;

        // 3-Minute Crash-Recovery Autosave Service (%AppData%/VortexCutStudio/Autosave)
        AutoSave = new AutoSaveService(
            () => Document,
            UndoRedo,
            onAutoSaveNotified: path =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    StatusMessage = $"Auto-saved recovery backup ({Path.GetFileName(path)}).";
                });
            },
            interval: TimeSpan.FromMinutes(3));
    }

    /// <summary>
    /// Starts with a clean, empty Media Pool and empty Timeline (no mock/dummy clips).
    /// </summary>
    private static ProjectDocument CreateInitialStarterProject()
    {
        var doc = ProjectDocument.CreateDefault();
        doc.ProjectName = "Vortex_Master_Sequence";
        doc.Fps = 24.0;
        return doc;
    }

    partial void OnProjectNameChanged(string value)
    {
        Document.ProjectName = value;
        OnPropertyChanged(nameof(WindowTitle));
        RequestCanvasRefresh?.Invoke();
    }

    partial void OnCurrentProjectFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnIsProjectModifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnMissingMediaCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMissingMedia));
    }

    partial void OnShowSafeMarginsChanged(bool value)
    {
        Compositor.ShowSafeMargins = value;
        RequestCanvasRefresh?.Invoke();
    }

    partial void OnSelectedAspectRatioChanged(string value)
    {
        if (value.StartsWith("9:16"))
        {
            Document.Width = 1080;
            Document.Height = 1920;
            Document.AspectRatioPreset = "9:16";
        }
        else if (value.StartsWith("1:1"))
        {
            Document.Width = 1080;
            Document.Height = 1080;
            Document.AspectRatioPreset = "1:1";
        }
        else
        {
            Document.Width = 1920;
            Document.Height = 1080;
            Document.AspectRatioPreset = "16:9";
        }
        RequestCanvasRefresh?.Invoke();
        StatusMessage = $"Canvas aspect ratio set to {value} ({Document.Width}x{Document.Height}).";
    }

    partial void OnSelectedPreviewQualityChanged(string value)
    {
        PreviewQualityScale = value.StartsWith("Full") ? 1.0 : value.StartsWith("1/4") ? 0.25 : 0.5;
        RequestCanvasRefresh?.Invoke();
        StatusMessage = $"Preview resolution set to {value}.";
    }

    public void ApplyEffectLibraryCode(string code)
    {
        var clip = Timeline.SelectedClip ?? Document.Tracks.SelectMany(t => t.Clips)
            .FirstOrDefault(c => Timeline.PlayheadTime >= c.TimelineStart && Timeline.PlayheadTime <= c.TimelineStart + c.Duration);

        if (clip == null)
        {
            StatusMessage = "Select a clip on the timeline first to apply an effect or transition.";
            return;
        }

        UndoRedo.RecordSnapshot(Document, $"Apply '{code}' to '{clip.Label}'", () =>
        {
            if (code.StartsWith("Trans:"))
            {
                string tType = code["Trans:".Length..];
                if (clip.IsTransitionClip)
                {
                    clip.TransitionType = tType;
                    clip.Label = $"{tType} Transition";
                    if (clip.InTransition != null) clip.InTransition.Type = tType;
                }
                else
                {
                    if (tType == "None")
                    {
                        clip.InTransition = null;
                        clip.OutTransition = null;
                        clip.FadeInDuration = 0;
                        clip.FadeOutDuration = 0;
                    }
                    else
                    {
                        clip.InTransition = new TransitionSpec { Type = tType, Duration = 0.5 };
                        clip.OutTransition = new TransitionSpec { Type = tType, Duration = 0.5 };
                        clip.FadeInDuration = 0.5;
                        clip.FadeOutDuration = 0.5;
                    }
                }
            }
            else if (code.StartsWith("Color:"))
            {
                clip.ColorFilterPreset = code["Color:".Length..];
            }
            else if (code.StartsWith("Fx:"))
            {
                string fx = code["Fx:".Length..];
                switch (fx)
                {
                    case "CameraShake": clip.CameraShakeAmount = clip.CameraShakeAmount > 2f ? 0f : 45f; break;
                    case "GaussianBlur": clip.GaussianBlurSigma = clip.GaussianBlurSigma > 0.5f ? 0f : 6.0f; break;
                    case "ChromaticAberration": clip.ChromaticAberrationAmount = clip.ChromaticAberrationAmount > 2f ? 0f : 45f; break;
                    case "FilmGrain": clip.FilmGrainAmount = clip.FilmGrainAmount > 2f ? 0f : 35f; break;
                    case "Vignette": clip.Vignette = clip.Vignette > 2f ? 0f : 50f; break;
                    case "DropShadow": clip.DropShadowEnabled = !clip.DropShadowEnabled; break;
                    case "RemoveBackground": clip.SubjectMatteEnabled = !clip.SubjectMatteEnabled; break;
                }
            }
            else if (code.StartsWith("Audio:"))
            {
                string afx = code["Audio:".Length..];
                switch (afx)
                {
                    case "CleanVoice":
                        clip.DialogueIsolateEnabled = true;
                        clip.CompressorEnabled = true;
                        break;
                    case "PodcastVoice":
                        clip.EqPreset = "PodcastVoice";
                        clip.CompressorEnabled = true;
                        break;
                    case "BassBoost":
                        clip.EqPreset = "BassBoost";
                        break;
                    case "StudioReverb":
                        clip.ReverbPreset = "Studio";
                        clip.ReverbDryWet = 0.28f;
                        break;
                }
            }
        }, RestoreProjectDocument);

        Timeline.SelectedClip = clip;
        Inspector.BindClip(clip);
        NotifyTimelineAndCanvasChanged();
        StatusMessage = $"Applied '{code}' to '{clip.Label}'.";
    }

    public void HandleTimelineContextMenuAction(string actionCode, double clickTime)
    {
        if (actionCode.StartsWith("Trans:"))
        {
            ApplyEffectLibraryCode(actionCode);
            return;
        }

        switch (actionCode)
        {
            case "Cut": Timeline.CutSelectedClip(); break;
            case "Copy": Timeline.CopySelectedClip(); break;
            case "Paste": Timeline.PasteClipAtPlayhead(); break;
            case "PasteAtCursor": Timeline.PasteClipAtTime(clickTime); break;
            case "PasteAttributes":
                Timeline.PasteAttributesToSelectedClip();
                Inspector.BindClip(Timeline.SelectedClip);
                break;
            case "Duplicate":
                Timeline.DuplicateSelectedClip();
                Inspector.BindClip(Timeline.SelectedClip);
                break;
            case "SplitAtPlayhead": Timeline.SplitAtPlayhead(); break;
            case "LiftDelete":
                Timeline.DeleteSelectedClip();
                Inspector.BindClip(null);
                break;
            case "RippleDelete":
                Timeline.RippleDeleteSelectedClip();
                Inspector.BindClip(null);
                break;
            case "CloseGapHere": Timeline.CloseGapAtTime(clickTime); break;
            case "SpeedDurationModal": OpenSpeedDurationModal(); break;
            case "FreezeFrame":
                Timeline.FreezeFrameAtPlayhead();
                Inspector.BindClip(Timeline.SelectedClip);
                break;
            case "DetachAudio": Timeline.DetachAudioFromSelectedClip(); break;
            case "ToggleLinkAudioVideo": Timeline.ToggleLinkAudio(); break;
            case "ClearKeyframes": Inspector.ClearAllKeyframes(); break;
            case "ResetAllEffectsAndColor": Inspector.ResetTransforms(); break;
            case "RevealInExplorer": Timeline.RevealSelectedClipInExplorer(); break;
            case "AddStandaloneTransitionBlock":
                Timeline.AddStandaloneTransitionBlockAtSelectionOrPlayhead();
                Inspector.BindClip(Timeline.SelectedClip);
                break;
            case "AddStandaloneTransitionBlockHere":
                Timeline.AddStandaloneTransitionBlockAtTime(clickTime);
                Inspector.BindClip(Timeline.SelectedClip);
                break;
            case "InsertTextHere":
                Timeline.InsertTextClipAtTime(clickTime);
                Inspector.BindClip(Timeline.SelectedClip);
                break;
            case "AddAdjustmentLayer":
                Timeline.AddAdjustmentLayer();
                Inspector.BindClip(Timeline.SelectedClip);
                break;
            case "CreateCompoundClip":
                Timeline.CreateCompoundClip();
                Inspector.BindClip(Timeline.SelectedClip);
                break;
            case "AddVideoTrack": Timeline.AddVideoTrack(); break;
            case "AddAudioTrack": Timeline.AddAudioTrack(); break;
            default:
                if (actionCode.StartsWith("DeleteTrack:"))
                {
                    string idStr = actionCode["DeleteTrack:".Length..];
                    if (Guid.TryParse(idStr, out var trackId) && Document.Tracks.Count > 1)
                    {
                        var tr = Document.Tracks.FirstOrDefault(t => t.Id == trackId);
                        if (tr != null)
                        {
                            UndoRedo.RecordSnapshot(Document, $"Delete Track {tr.Name}", () =>
                            {
                                Document.Tracks.Remove(tr);
                            }, RestoreProjectDocument);
                            NotifyTimelineAndCanvasChanged();
                            StatusMessage = $"Deleted track '{tr.Name}'.";
                        }
                    }
                }
                break;
        }
    }

    public void LoadAssetIntoSourceMonitor(MediaAsset? asset)
    {
        if (asset == null) return;
        SourceMonitorAsset = asset;
        SourceMonitorTime = asset.MarkIn;
        IsSourceMonitorActive = true;
        UpdateSourceMonitorReadouts();
        RequestCanvasRefresh?.Invoke();
        StatusMessage = $"Loaded '{asset.Name}' into Source Monitor ({asset.ResolutionSummary}).";
    }

    public void UpdateSourceMonitorReadouts()
    {
        double fps = SourceMonitorAsset?.Fps ?? Document.Fps;
        SourceMonitorTimecode = TimecodeCalculator.SecondsToSmpte(SourceMonitorTime, fps);
        double inPt = SourceMonitorAsset?.MarkIn ?? 0.0;
        double outPt = SourceMonitorAsset?.MarkOut ?? 0.0;
        SourceInOutSummary = $"IN {TimecodeCalculator.SecondsToSmpte(inPt, fps)}  •  OUT {TimecodeCalculator.SecondsToSmpte(outPt, fps)}";
    }

    [RelayCommand]
    public void SwitchToSourceMonitor()
    {
        if (SourceMonitorAsset == null && MediaBin.SelectedAsset != null)
            SourceMonitorAsset = MediaBin.SelectedAsset;
        IsSourceMonitorActive = true;
        UpdateSourceMonitorReadouts();
        RequestCanvasRefresh?.Invoke();
    }

    [RelayCommand]
    public void SwitchToProgramMonitor()
    {
        IsSourceMonitorActive = false;
        RequestCanvasRefresh?.Invoke();
    }

    [RelayCommand]
    public void FocusAiAssistant()
    {
        RightPanelTabIndex = 1;
        StatusMessage = "Smart Edit Assistant (Ctrl+K) ready — Type an edit command or click a Quick action.";
    }

    [RelayCommand]
    public void SetMarkIn()
    {
        if (IsSourceMonitorActive && SourceMonitorAsset != null)
        {
            SourceMonitorAsset.MarkIn = Math.Clamp(SourceMonitorTime, 0, Math.Max(0, SourceMonitorAsset.MarkOut - 0.1));
            UpdateSourceMonitorReadouts();
            RequestCanvasRefresh?.Invoke();
            StatusMessage = $"Source Mark In set to {TimecodeCalculator.SecondsToSmpte(SourceMonitorAsset.MarkIn, SourceMonitorAsset.Fps)} (I).";
        }
        else
        {
            Document.TimelineMarkIn = Math.Round(Timeline.PlayheadTime, 3);
            if (Document.TimelineMarkOut.HasValue && Document.TimelineMarkOut.Value <= Document.TimelineMarkIn.Value)
                Document.TimelineMarkOut = null;
            RequestTimelineRefresh?.Invoke();
            StatusMessage = $"Sequence Mark In set at {Timeline.FormattedTimecode} (I).";
        }
    }

    [RelayCommand]
    public void SetMarkOut()
    {
        if (IsSourceMonitorActive && SourceMonitorAsset != null)
        {
            SourceMonitorAsset.MarkOut = Math.Clamp(SourceMonitorTime, SourceMonitorAsset.MarkIn + 0.1, SourceMonitorAsset.Duration);
            UpdateSourceMonitorReadouts();
            RequestCanvasRefresh?.Invoke();
            StatusMessage = $"Source Mark Out set to {TimecodeCalculator.SecondsToSmpte(SourceMonitorAsset.MarkOut, SourceMonitorAsset.Fps)} (O).";
        }
        else
        {
            Document.TimelineMarkOut = Math.Round(Timeline.PlayheadTime, 3);
            RequestTimelineRefresh?.Invoke();
            StatusMessage = $"Sequence Mark Out set at {Timeline.FormattedTimecode} (O).";
        }
    }

    [RelayCommand]
    public void InsertSourceClipToTimeline()
    {
        var asset = SourceMonitorAsset ?? MediaBin.SelectedAsset;
        if (asset == null) return;

        TimelineClip? inserted = null;
        UndoRedo.RecordSnapshot(Document, $"Insert (F9) '{asset.Name}'", () =>
        {
            inserted = TimelineService.InsertOrOverwriteSubClip(Document, asset, asset.MarkIn, asset.MarkOut, Timeline.PlayheadTime, rippleInsert: true);
        }, RestoreProjectDocument);

        IsSourceMonitorActive = false;
        Timeline.SelectedClip = inserted;
        Inspector.BindClip(inserted);
        NotifyTimelineAndCanvasChanged();
        StatusMessage = $"Insert Edit (F9): Spliced '{asset.Name}' at {Timeline.FormattedTimecode}.";
    }

    [RelayCommand]
    public void OverwriteSourceClipToTimeline()
    {
        var asset = SourceMonitorAsset ?? MediaBin.SelectedAsset;
        if (asset == null) return;

        TimelineClip? placed = null;
        UndoRedo.RecordSnapshot(Document, $"Overwrite (F10) '{asset.Name}'", () =>
        {
            placed = TimelineService.InsertOrOverwriteSubClip(Document, asset, asset.MarkIn, asset.MarkOut, Timeline.PlayheadTime, rippleInsert: false);
        }, RestoreProjectDocument);

        IsSourceMonitorActive = false;
        Timeline.SelectedClip = placed;
        Inspector.BindClip(placed);
        NotifyTimelineAndCanvasChanged();
        StatusMessage = $"Overwrite Edit (F10): Placed '{asset.Name}' at {Timeline.FormattedTimecode}.";
    }

    public void RestoreProjectDocument(ProjectDocument restored)
    {
        StopPlayback();
        Document = restored;
        ProjectName = Document.ProjectName;
        AutoDuckingEnabled = Document.AutoDuckingEnabled;
        MediaBin.SyncFromDocument(Document);
        Timeline.SyncFromDocument(Document);
        var firstClip = Document.Tracks.SelectMany(t => t.Clips).FirstOrDefault();
        Timeline.SelectedClip = firstClip;
        Inspector.BindClip(firstClip);
        NotifyTimelineAndCanvasChanged();
    }

    public void NotifyTimelineAndCanvasChanged()
    {
        Timeline.UpdateTimecodeDisplay();
        Inspector.SyncPlayheadValues();
        var (lDb, rDb) = AudioPlayer.SampleRealtimeVuLevels(Document, Timeline.PlayheadTime);
        RequestVuMeterRefresh?.Invoke(lDb, rDb);
        RequestTimelineRefresh?.Invoke();
        RequestCanvasRefresh?.Invoke();
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        if (Timeline.IsPlaying)
        {
            StopPlayback();
        }
        else
        {
            StartShuttlePlayback(1.0);
        }
    }

    public void ShuttleReverse()
    {
        double nextSpeed = Timeline.ShuttleSpeed < 0 ? Math.Max(-4.0, Timeline.ShuttleSpeed * 2.0) : -1.0;
        StartShuttlePlayback(nextSpeed);
    }

    public void ShuttleStop()
    {
        StopPlayback();
        StatusMessage = $"Shuttle K (Pause) at {Timeline.FormattedTimecode}.";
    }

    public void ShuttleForward()
    {
        double nextSpeed = Timeline.ShuttleSpeed >= 1.0 ? Math.Min(4.0, Timeline.ShuttleSpeed * 2.0) : 1.0;
        StartShuttlePlayback(nextSpeed);
    }

    private void StartShuttlePlayback(double speedMultiplier)
    {
        if (!IsSourceMonitorActive && Document.TotalDuration <= 0.05)
        {
            StatusMessage = "Import a video or audio file (Ctrl+I) to start playback.";
            return;
        }

        if (speedMultiplier > 0 && Timeline.PlayheadTime >= Document.TotalDuration - 0.04)
            Timeline.PlayheadTime = 0;

        Timeline.ShuttleSpeed = speedMultiplier;
        Timeline.IsPlaying = true;
        _lastTickUtc = DateTime.UtcNow;

        if (Math.Abs(speedMultiplier - 1.0) < 0.05 && !IsSourceMonitorActive)
        {
            AudioPlayer.StartPlayback(Document, Timeline.PlayheadTime);
        }
        else
        {
            AudioPlayer.StopPlayback();
        }

        _playbackTimer.Start();
        StatusMessage = $"Transport Playback ({speedMultiplier:+0.#;-0.#}x)  •  {Timeline.FormattedTimecode}";
    }

    public void StopPlayback()
    {
        _playbackTimer.Stop();
        AudioPlayer.StopPlayback();
        Timeline.IsPlaying = false;
        Timeline.ShuttleSpeed = 0.0;
        RequestVuMeterRefresh?.Invoke(-60f, -60f);
    }

    [RelayCommand]
    public void SeekToStart()
    {
        StopPlayback();
        if (IsSourceMonitorActive)
        {
            SourceMonitorTime = 0;
            UpdateSourceMonitorReadouts();
        }
        else
        {
            Timeline.PlayheadTime = 0;
        }
        NotifyTimelineAndCanvasChanged();
    }

    [RelayCommand]
    public void StepFrame(double deltaSeconds)
    {
        StopPlayback();
        if (IsSourceMonitorActive && SourceMonitorAsset != null)
        {
            SourceMonitorTime = Math.Clamp(SourceMonitorTime + deltaSeconds, 0, SourceMonitorAsset.Duration);
            UpdateSourceMonitorReadouts();
            RequestCanvasRefresh?.Invoke();
        }
        else
        {
            Timeline.PlayheadTime = Math.Clamp(Timeline.PlayheadTime + deltaSeconds, 0, Math.Max(0.1, Document.TotalDuration));
            NotifyTimelineAndCanvasChanged();
        }
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _lastTickUtc).TotalSeconds, 0.001, 0.1);
        _lastTickUtc = now;
        double speed = Math.Abs(Timeline.ShuttleSpeed) > 0.01 ? Timeline.ShuttleSpeed : 1.0;

        if (IsSourceMonitorActive && SourceMonitorAsset != null)
        {
            double nextSrc = SourceMonitorTime + dt * speed;
            if (nextSrc >= SourceMonitorAsset.Duration || nextSrc < 0)
            {
                SourceMonitorTime = Math.Clamp(nextSrc, 0, SourceMonitorAsset.Duration);
                StopPlayback();
                UpdateSourceMonitorReadouts();
                RequestCanvasRefresh?.Invoke();
                return;
            }
            SourceMonitorTime = nextSrc;
            UpdateSourceMonitorReadouts();
            RequestPlaybackTick?.Invoke(SourceMonitorTime);
            return;
        }

        double nextTime = Timeline.PlayheadTime + dt * speed;
        if (nextTime >= Document.TotalDuration || nextTime < 0)
        {
            Timeline.PlayheadTime = Math.Clamp(nextTime, 0, Document.TotalDuration);
            StopPlayback();
            NotifyTimelineAndCanvasChanged();
            return;
        }

        Timeline.PlayheadTime = nextTime;
        Inspector.SyncPlayheadValues();
        var (lDb, rDb) = AudioPlayer.SampleRealtimeVuLevels(Document, Timeline.PlayheadTime);
        RequestVuMeterRefresh?.Invoke(lDb, rDb);
        RequestPlaybackTick?.Invoke(nextTime);
    }

    [RelayCommand]
    public void Undo()
    {
        if (!UndoRedo.CanUndo) return;
        UndoRedo.Undo();
        NotifyTimelineAndCanvasChanged();
        StatusMessage = "Undo executed (Ctrl+Z).";
    }

    [RelayCommand]
    public void Redo()
    {
        if (!UndoRedo.CanRedo) return;
        UndoRedo.Redo();
        NotifyTimelineAndCanvasChanged();
        StatusMessage = "Redo executed (Ctrl+Y).";
    }

    [RelayCommand]
    public void OpenSettingsModal() => IsSettingsModalOpen = true;

    [RelayCommand]
    public void CloseSettingsModal()
    {
        Settings.SaveSettings();
        IsSettingsModalOpen = false;
    }

    [RelayCommand]
    public void OpenStripSilenceModal()
    {
        StopPlayback();
        IsStripSilenceModalOpen = true;
    }

    [RelayCommand]
    public void CloseStripSilenceModal() => IsStripSilenceModalOpen = false;

    [RelayCommand]
    public void ApplyStripSilenceModal()
    {
        Timeline.ExecuteStripSilenceWithParams(
            StripSilenceThresholdDb,
            StripSilenceMinDurationMs,
            StripSilencePadStartMs,
            StripSilencePadEndMs);
        IsStripSilenceModalOpen = false;
    }

    [RelayCommand]
    public void OpenSpeedDurationModal()
    {
        StopPlayback();
        var clip = Timeline.SelectedClip ?? Document.Tracks.SelectMany(t => t.Clips)
            .FirstOrDefault(c => Timeline.PlayheadTime >= c.TimelineStart && Timeline.PlayheadTime <= c.TimelineStart + c.Duration);
        if (clip == null)
        {
            StatusMessage = "Select a clip on the timeline to open Speed / Duration (Ctrl+R).";
            return;
        }

        Timeline.SelectedClip = clip;
        ModalClipSpeedPercent = Math.Round(clip.Speed * 100.0, 1);
        ModalClipDurationSeconds = Math.Round(clip.Duration, 2);
        ModalClipReversePlayback = clip.ReversePlayback;
        IsSpeedDurationModalOpen = true;
    }

    [RelayCommand]
    public void CloseSpeedDurationModal() => IsSpeedDurationModalOpen = false;

    [RelayCommand]
    public void ApplySpeedDurationModal()
    {
        var clip = Timeline.SelectedClip;
        if (clip != null)
        {
            double newSpeed = Math.Clamp(ModalClipSpeedPercent / 100.0, 0.1, 10.0);
            bool rev = ModalClipReversePlayback;
            UndoRedo.RecordSnapshot(Document, $"Speed {ModalClipSpeedPercent:0}% on '{clip.Label}'", () =>
            {
                double srcSpan = Math.Max(0.2, clip.SourceOut - clip.SourceIn);
                clip.Speed = newSpeed;
                clip.Duration = Math.Max(0.15, Math.Round(srcSpan / newSpeed, 3));
                clip.ReversePlayback = rev;
            }, RestoreProjectDocument);

            Inspector.BindClip(clip);
            NotifyTimelineAndCanvasChanged();
            StatusMessage = $"Updated '{clip.Label}' Speed to {newSpeed:0.##}x ({clip.Duration:0.00}s){(rev ? " [Reversed]" : "")}.";
        }
        IsSpeedDurationModalOpen = false;
    }

    partial void OnSelectedExportPresetChanged(string value)
    {
        if (value.Contains("4K", StringComparison.OrdinalIgnoreCase))
        {
            ExportResolution = "4K UHD (3840x2160)";
            ExportOutputPath = Path.ChangeExtension(ExportOutputPath, ".mp4");
        }
        else if (value.Contains("Vertical", StringComparison.OrdinalIgnoreCase))
        {
            ExportResolution = "Vertical 9:16 (1080x1920)";
            ExportOutputPath = Path.ChangeExtension(ExportOutputPath, ".mp4");
        }
        else if (value.Contains("ProRes", StringComparison.OrdinalIgnoreCase))
        {
            ExportResolution = "1080p (1920x1080)";
            SelectedHardwareEncoder = "prores_ks";
            ExportOutputPath = Path.ChangeExtension(ExportOutputPath, ".mov");
        }
        else if (value.Contains("Audio Only", StringComparison.OrdinalIgnoreCase))
        {
            ExportOutputPath = Path.ChangeExtension(ExportOutputPath, ".wav");
        }
        else
        {
            ExportResolution = "1080p (1920x1080)";
            ExportOutputPath = Path.ChangeExtension(ExportOutputPath, ".mp4");
        }
    }

    [RelayCommand]
    public void OpenExportModal()
    {
        StopPlayback();
        ExportProgressPercent = 0;
        ExportProgressStatus = "Ready to render master sequence";
        IsExportModalOpen = true;
    }

    [RelayCommand]
    public void CloseExportModal()
    {
        if (IsExporting) return;
        IsExportModalOpen = false;
    }

    [RelayCommand]
    public void CancelExport()
    {
        _exportCts?.Cancel();
        ExportProgressStatus = "Cancelling export...";
    }

    [RelayCommand]
    public void OpenExportFolder()
    {
        try
        {
            string? folder = Path.GetDirectoryName(ExportOutputPath);
            if (File.Exists(ExportOutputPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{ExportOutputPath}\"");
            }
            else if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
            }
        }
        catch { }
    }

    [RelayCommand]
    public async Task StartExportAsync()
    {
        if (IsExporting) return;
        IsExporting = true;
        _exportCts = new CancellationTokenSource();

        var (w, h) = ExportResolution switch
        {
            var s when s.StartsWith("4K") => (3840, 2160),
            var s when s.StartsWith("Vertical") => (1080, 1920),
            var s when s.StartsWith("720p") => (1280, 720),
            _ => (1920, 1080)
        };

        bool useInOut = SelectedExportRange.Contains("Mark In", StringComparison.OrdinalIgnoreCase);
        bool isAudioOnly = SelectedExportPreset.StartsWith("Audio Only", StringComparison.OrdinalIgnoreCase);

        var exportSettings = new ExportSettings
        {
            OutputPath = ExportOutputPath,
            PresetName = SelectedExportPreset,
            Width = w,
            Height = h,
            Fps = ExportFps,
            HardwareEncoder = SelectedHardwareEncoder,
            BitrateKbps = w >= 3840 ? 45000 : 18000,
            UseMarkInOutRange = useInOut,
            RangeStartSeconds = Document.TimelineMarkIn,
            RangeEndSeconds = Document.TimelineMarkOut,
            AudioOnly = isAudioOnly
        };

        try
        {
            var progress = new Progress<(double percent, string status, TimeSpan eta)>(p =>
            {
                ExportProgressPercent = p.percent;
                ExportProgressStatus = $"{p.status}  •  ETA {p.eta:mm\\:ss}";
                StatusMessage = ExportProgressStatus;
            });

            await VideoExporter.ExportProjectAsync(Document, exportSettings, progress, _exportCts.Token);
            StatusMessage = $"Master render complete: {ExportOutputPath}";
        }
        catch (OperationCanceledException)
        {
            ExportProgressPercent = 0;
            ExportProgressStatus = "Render cancelled by user.";
            StatusMessage = "Export cancelled.";
        }
        catch (Exception ex)
        {
            ExportProgressStatus = $"Render fault: {ex.Message}";
            StatusMessage = ExportProgressStatus;
        }
        finally
        {
            IsExporting = false;
            _exportCts?.Dispose();
            _exportCts = null;
        }
    }

    public void RequestActionWithUnsavedCheck(string actionDescription, Action onProceed)
    {
        if (!IsProjectModified)
        {
            onProceed();
            return;
        }

        StopPlayback();
        _pendingUnsavedProceedAction = onProceed;
        UnsavedChangesMessage = $"'{ProjectName}' has unsaved edits. Save changes before {actionDescription}?";
        IsUnsavedChangesModalOpen = true;
    }

    public void ExecutePendingUnsavedProceedAction()
    {
        IsUnsavedChangesModalOpen = false;
        var act = _pendingUnsavedProceedAction;
        _pendingUnsavedProceedAction = null;
        act?.Invoke();
    }

    [RelayCommand]
    public void CancelUnsavedChangesModal()
    {
        IsUnsavedChangesModalOpen = false;
        _pendingUnsavedProceedAction = null;
    }

    public void CreateNewProject()
    {
        StopPlayback();
        var fresh = CreateInitialStarterProject();
        CurrentProjectFilePath = null;
        MissingMediaCount = 0;
        RestoreProjectDocument(fresh);
        UndoRedo.Clear();
        UndoRedo.MarkSaved();
        IsProjectModified = false;
        StatusMessage = "Created new empty project sequence (Ctrl+N).";
    }

    public async Task SaveProjectToFileAsync(string filePath)
    {
        await ProjectSerializer.SaveAsync(Document, filePath);
        CurrentProjectFilePath = filePath;
        ProjectName = Path.GetFileNameWithoutExtension(filePath);
        UndoRedo.MarkSaved();
        IsProjectModified = false;
        StatusMessage = $"Saved project '{ProjectName}' to {Path.GetFileName(filePath)} (Ctrl+S).";
    }

    public async Task LoadProjectFromFileAsync(string filePath)
    {
        var loaded = await ProjectSerializer.LoadAsync(filePath);
        CurrentProjectFilePath = filePath;
        var missing = ProjectSerializer.FindMissingMediaAssets(loaded);
        MissingMediaCount = missing.Count;
        RestoreProjectDocument(loaded);
        UndoRedo.Clear();
        UndoRedo.MarkSaved();
        IsProjectModified = false;

        if (missing.Count > 0)
        {
            StatusMessage = $"Opened '{loaded.ProjectName}' — Warning: {missing.Count} offline media file(s). Use File > Relink Missing Media.";
        }
        else
        {
            StatusMessage = $"Opened project '{loaded.ProjectName}' from {Path.GetFileName(filePath)}.";
        }
    }

    public async Task ArchiveProjectToFolderAsync(string archiveFolderPath)
    {
        string archivedVcsproj = await ProjectSerializer.ArchiveProjectAsync(Document, archiveFolderPath);
        StatusMessage = $"Archived project & collected all media + LUTs into: {archiveFolderPath} ({Path.GetFileName(archivedVcsproj)}).";
    }

    public int RelinkMissingMediaFromFolder(string searchFolderPath)
    {
        int relinked = ProjectSerializer.TryAutoRelinkMissingMedia(Document, new[] { searchFolderPath });
        var remaining = ProjectSerializer.FindMissingMediaAssets(Document);
        MissingMediaCount = remaining.Count;
        MediaBin.SyncFromDocument(Document);
        NotifyTimelineAndCanvasChanged();
        if (relinked > 0)
            UndoRedo.MarkModified();

        StatusMessage = relinked > 0
            ? $"Relinked {relinked} media file(s) from '{searchFolderPath}'. Remaining offline: {remaining.Count}."
            : $"No matching media files found in '{searchFolderPath}'. Remaining offline: {remaining.Count}.";
        return relinked;
    }

    public async Task ExportInterchangeAsync(string outputPath)
    {
        string ext = Path.GetExtension(outputPath).ToLowerInvariant();
        if (ext == ".edl")
        {
            await ProjectInterchangeExporter.ExportCmx3600EdlAsync(Document, outputPath);
            StatusMessage = $"Exported SMPTE CMX 3600 EDL for DaVinci Resolve / Premiere: {Path.GetFileName(outputPath)}";
        }
        else
        {
            await ProjectInterchangeExporter.ExportFcp7XmlAsync(Document, outputPath);
            StatusMessage = $"Exported FCP7 XML (xmeml v5) for DaVinci Resolve / Premiere: {Path.GetFileName(outputPath)}";
        }
    }

    public void Dispose()
    {
        _playbackTimer.Stop();
        AutoSave.Dispose();
        AudioPlayer.Dispose();
        Compositor.Dispose();
        AiRouter.Dispose();
    }
}
