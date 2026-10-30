using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AiVideoEditor.Core.Models;
using AiVideoEditor.App.Controls;
using AiVideoEditor.App.Controls.TimelineControl;
using AiVideoEditor.App.ViewModels;

namespace AiVideoEditor.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;
    private bool _suppressScrollSync;
    private bool _suppressZoomSync;
    private bool _allowWindowClose;

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => WireUpViewModel();
        AddHandler(KeyDownEvent, OnWindowKeyDownTunnel, RoutingStrategies.Tunnel);
        KeyDown += OnWindowKeyDown;
        AddHandler(DragDrop.DropEvent, OnWindowFileDrop);
        Closing += OnMainWindowClosing;
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowWindowClose || Vm == null || !Vm.IsProjectModified)
            return;

        e.Cancel = true;
        Vm.RequestActionWithUnsavedCheck("closing Vortex Cut Studio", () =>
        {
            _allowWindowClose = true;
            Close();
        });
    }

    private void OnWindowKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // Universal Tunneling Undo / Redo for sliders, curves, keyframes, numeric boxes, text inputs
        if (ctrl && e.Key == Key.Z)
        {
            if (shift) Vm.Redo();
            else Vm.Undo();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.Y)
        {
            Vm.Redo();
            e.Handled = true;
            return;
        }
    }

    private void WireUpViewModel()
    {
        if (Vm == null) return;

        PreviewCanvas?.AttachEngine(Vm.Compositor, Vm.Document);
        TimelineCanvas?.BindDocument(Vm.Document, Vm.UndoRedo, Vm.TimelineService, Vm.FrameReader);
        TimelineCanvas?.SelectClip(Vm.Timeline.SelectedClip);
        TopTimecodeScrubber?.SetTimecode(Vm.Timeline.PlayheadTime, Vm.Document.Fps);
        InspectorKeyframeGraph?.BindClip(Vm.Timeline.SelectedClip, Vm.Timeline.PlayheadTime);
        InspectorCurveEditor?.BindClip(Vm.Timeline.SelectedClip);

        if (PreviewCanvas != null)
        {
            PreviewCanvas.ClipPositionDragged += (newPosX, newPosY) =>
            {
                if (Vm == null) return;
                Vm.Inspector.PositionX = newPosX;
                Vm.Inspector.PositionY = newPosY;
            };

            PreviewCanvas.ClipScaleDragged += newScale =>
            {
                if (Vm == null) return;
                Vm.Inspector.Scale = newScale;
            };

            PreviewCanvas.ClipRotationDragged += newRot =>
            {
                if (Vm == null) return;
                Vm.Inspector.Rotation = newRot;
            };

            PreviewCanvas.ClipClickedOnCanvas += clip =>
            {
                if (Vm == null) return;
                Vm.Timeline.SelectedClip = clip;
                Vm.Inspector.BindClip(clip);
                TimelineCanvas?.SelectClip(clip);
                InspectorKeyframeGraph?.BindClip(clip, Vm.Timeline.PlayheadTime);
                InspectorCurveEditor?.BindClip(clip);
            };

            PreviewCanvas.TextLayerDoubleClicked += clip =>
            {
                if (Vm == null) return;
                Vm.RightPanelTabIndex = 0;
                Vm.Timeline.SelectedClip = clip;
                Vm.Inspector.BindClip(clip);
                InspectorKeyframeGraph?.BindClip(clip, Vm.Timeline.PlayheadTime);
                InspectorCurveEditor?.BindClip(clip);
                InspectorSubtitleTextBox?.Focus();
                InspectorSubtitleTextBox?.SelectAll();
            };

            PreviewCanvas.FrameRendered += () =>
            {
                if (Vm == null) return;
                if (Vm.Compositor.LastScopeData != null)
                    InspectorVideoScope?.UpdateScopeData(Vm.Compositor.LastScopeData);
                else
                    InspectorVideoScope?.UpdateScope(Vm.Compositor.LastScopeHistograms);
            };
        }

        if (InspectorCurveEditor != null)
        {
            InspectorCurveEditor.CurveChanged += SyncMonitorCanvas;
            InspectorCurveEditor.CurveCommitted += (before, after, desc) =>
            {
                if (Vm == null) return;
                Vm.Inspector.RecordCurveCommit(before, after, desc);
            };
        }

        if (InspectorKeyframeGraph != null)
        {
            InspectorKeyframeGraph.KeyframeCommandCommitted += (before, after, desc) =>
            {
                if (Vm == null) return;
                Vm.Inspector.RecordKeyframeGraphCommit(before, after, desc);
            };
            InspectorKeyframeGraph.PlayheadRequested += targetTime =>
            {
                if (Vm == null) return;
                Vm.StopPlayback();
                Vm.IsSourceMonitorActive = false;
                Vm.Timeline.PlayheadTime = Math.Max(0, targetTime);
                Vm.Inspector.SyncPlayheadValues();
                SyncMonitorCanvas();
                TimelineCanvas?.SetPlayhead(Vm.Timeline.PlayheadTime);
            };

            InspectorKeyframeGraph.KeyframesMutated += () =>
            {
                if (Vm == null) return;
                Vm.Inspector.SyncPlayheadValues();
                SyncMonitorCanvas();
                TimelineCanvas?.InvalidateVisual();
            };
        }

        if (TopTimecodeScrubber != null)
        {
            TopTimecodeScrubber.TimeScrubbed += time =>
            {
                if (Vm == null) return;
                Vm.StopPlayback();
                if (Vm.IsSourceMonitorActive)
                {
                    Vm.SourceMonitorTime = time;
                    Vm.UpdateSourceMonitorReadouts();
                }
                else
                {
                    Vm.Timeline.PlayheadTime = time;
                    Vm.Inspector.SyncPlayheadValues();
                }
                SyncMonitorCanvas();
                TimelineCanvas?.SetPlayhead(Vm.Timeline.PlayheadTime);
                InspectorKeyframeGraph?.BindClip(Vm.Timeline.SelectedClip, Vm.Timeline.PlayheadTime);
            };
        }

        if (TimelineCanvas != null)
        {
            TimelineCanvas.PlayheadScrubbed += time =>
            {
                if (Vm == null) return;
                Vm.StopPlayback();
                Vm.IsSourceMonitorActive = false;
                Vm.Timeline.PlayheadTime = time;
                Vm.Inspector.SyncPlayheadValues();
                SyncMonitorCanvas();
                TopTimecodeScrubber?.SetTimecode(time, Vm.Document.Fps);
                InspectorKeyframeGraph?.BindClip(Vm.Timeline.SelectedClip, time);
                var (lDb, rDb) = Vm.AudioPlayer.SampleRealtimeVuLevels(Vm.Document, time);
                MasterVuMeter?.SetLevels(lDb, rDb);
            };

            TimelineCanvas.ClipSelected += clip =>
            {
                if (Vm == null) return;
                Vm.Timeline.SelectedClip = clip;
                Vm.Inspector.BindClip(clip);
                InspectorKeyframeGraph?.BindClip(clip, Vm.Timeline.PlayheadTime);
                InspectorCurveEditor?.BindClip(clip);
                SyncMonitorCanvas();
            };

            TimelineCanvas.TimelineMutated += () =>
            {
                if (Vm == null) return;
                Vm.Timeline.UpdateTimecodeDisplay();
                Vm.Inspector.BindClip(TimelineCanvas.SelectedClip);
                InspectorKeyframeGraph?.BindClip(TimelineCanvas.SelectedClip, Vm.Timeline.PlayheadTime);
                InspectorCurveEditor?.BindClip(TimelineCanvas.SelectedClip);
                SyncMonitorCanvas();
            };

            TimelineCanvas.ScrollMetricsChanged += (offsetSec, maxSec, viewportSec) =>
            {
                if (Vm == null) return;
                _suppressScrollSync = true;
                Vm.Timeline.MaxScrollSeconds = maxSec;
                Vm.Timeline.ViewportDurationSeconds = viewportSec;
                Vm.Timeline.ScrollOffsetSeconds = offsetSec;
                _suppressScrollSync = false;
            };

            TimelineCanvas.ZoomChanged += pps =>
            {
                if (Vm == null) return;
                _suppressZoomSync = true;
                Vm.Timeline.ZoomPixelsPerSecond = pps;
                _suppressZoomSync = false;
            };

            TimelineCanvas.ContextMenuActionRequested += (actionCode, clickTime) =>
            {
                if (Vm == null) return;
                if (actionCode == "FitTimeline")
                {
                    TimelineCanvas?.FitTimelineToWindow();
                    return;
                }
                Vm.HandleTimelineContextMenuAction(actionCode, clickTime);
            };
        }

        Vm.RequestCanvasRefresh += SyncMonitorCanvas;
        Vm.RequestPlaybackTick += SyncPlaybackTick;

        Vm.RequestTimelineRefresh += () =>
        {
            if (Vm == null) return;
            UpdateScopeState();
            TimelineCanvas?.BindDocument(Vm.Document, Vm.UndoRedo, Vm.TimelineService, Vm.FrameReader);
            TimelineCanvas?.SetPlayhead(Vm.Timeline.PlayheadTime);
            TimelineCanvas?.SelectClip(Vm.Timeline.SelectedClip);
            TopTimecodeScrubber?.SetTimecode(Vm.Timeline.PlayheadTime, Vm.Document.Fps);
            _lastBoundClip = Vm.Timeline.SelectedClip;
            InspectorKeyframeGraph?.BindClip(Vm.Timeline.SelectedClip, Vm.Timeline.PlayheadTime);
            InspectorCurveEditor?.BindClip(Vm.Timeline.SelectedClip);
        };

        Vm.RequestVuMeterRefresh += (leftDb, rightDb) =>
        {
            MasterVuMeter?.SetLevels(leftDb, rightDb);
        };
    }

    private TimelineClip? _lastBoundClip;

    private void UpdateScopeState()
    {
        if (Vm == null) return;
        bool shouldEnableScopes = Vm.RightPanelTabIndex == 0 &&
                                  Vm.Inspector.SelectedInspectorTabIndex == 1 &&
                                  Vm.Inspector.IsColorWheelsExpanded;
        Vm.Compositor.EnableScopeUpdates = shouldEnableScopes;
    }

    private void SyncPlaybackTick(double time)
    {
        if (Vm == null || PreviewCanvas == null) return;

        TimelineCanvas?.SetPlayhead(time);
        TopTimecodeScrubber?.SetTimecode(time, Vm.Document.Fps);
        InspectorKeyframeGraph?.SetPlayheadTime(time);

        if (Vm.IsSourceMonitorActive)
        {
            PreviewCanvas.Mode = MonitorDisplayMode.SourceAsset;
            PreviewCanvas.SetSourceAsset(Vm.SourceMonitorAsset, time);
        }
        else
        {
            PreviewCanvas.Mode = MonitorDisplayMode.ProgramSequence;
            PreviewCanvas.SetTime(time);
        }
    }

    private void SyncMonitorCanvas()
    {
        if (Vm == null || PreviewCanvas == null) return;

        UpdateScopeState();
        PreviewCanvas.PreviewQualityScale = Vm.PreviewQualityScale;
        PreviewCanvas.UpdateDocument(Vm.Document, Vm.Timeline.SelectedClip);
        if (Vm.IsSourceMonitorActive)
        {
            PreviewCanvas.Mode = MonitorDisplayMode.SourceAsset;
            PreviewCanvas.SetSourceAsset(Vm.SourceMonitorAsset, Vm.SourceMonitorTime);
            TopTimecodeScrubber?.SetTimecode(Vm.SourceMonitorTime, Vm.SourceMonitorAsset?.Fps ?? Vm.Document.Fps);
        }
        else
        {
            PreviewCanvas.Mode = MonitorDisplayMode.ProgramSequence;
            PreviewCanvas.SetTime(Vm.Timeline.PlayheadTime);
            TopTimecodeScrubber?.SetTimecode(Vm.Timeline.PlayheadTime, Vm.Document.Fps);
        }

        if (_lastBoundClip != Vm.Timeline.SelectedClip)
        {
            _lastBoundClip = Vm.Timeline.SelectedClip;
            InspectorKeyframeGraph?.BindClip(Vm.Timeline.SelectedClip, Vm.Timeline.PlayheadTime);
            InspectorCurveEditor?.BindClip(Vm.Timeline.SelectedClip);
        }
        else
        {
            InspectorKeyframeGraph?.SetPlayheadTime(Vm.Timeline.PlayheadTime);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        // Undo / Redo / Clipboard / File Shortcuts
        if (ctrl && e.Key == Key.Z)
        {
            if (shift) Vm.Redo();
            else Vm.Undo();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.Y)
        {
            Vm.Redo();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.X)
        {
            Vm.Timeline.CutSelectedClip();
            Vm.Inspector.BindClip(null);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.C)
        {
            Vm.Timeline.CopySelectedClip();
            e.Handled = true;
            return;
        }
        if (ctrl && alt && e.Key == Key.V)
        {
            Vm.Timeline.PasteAttributesToSelectedClip();
            Vm.Inspector.BindClip(Vm.Timeline.SelectedClip);
            e.Handled = true;
            return;
        }
        if (ctrl && alt && e.Key == Key.L)
        {
            Vm.Timeline.ToggleLinkAudio();
            e.Handled = true;
            return;
        }
        if (ctrl && !alt && e.Key == Key.V)
        {
            Vm.Timeline.PasteClipAtPlayhead();
            Vm.Inspector.BindClip(Vm.Timeline.SelectedClip);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.D)
        {
            Vm.Timeline.DuplicateSelectedClip();
            Vm.Inspector.BindClip(Vm.Timeline.SelectedClip);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.B)
        {
            Vm.Timeline.SplitAtPlayhead();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.R)
        {
            Vm.OpenSpeedDurationModal();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.N)
        {
            OnNewProjectClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.I)
        {
            OnImportMediaClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.O)
        {
            OnLoadProjectClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (ctrl && shift && e.Key == Key.S)
        {
            OnSaveProjectAsClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (ctrl && !shift && e.Key == Key.S)
        {
            OnSaveProjectClick(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.M)
        {
            Vm.OpenExportModal();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.K)
        {
            Vm.FocusAiAssistant();
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key == Key.OemComma)
        {
            Vm.OpenSettingsModal();
            e.Handled = true;
            return;
        }
        if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            if (TimelineCanvas != null)
                TimelineCanvas.SetZoom(TimelineCanvas.PixelsPerSecond * 1.25);
            e.Handled = true;
            return;
        }
        if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            if (TimelineCanvas != null)
                TimelineCanvas.SetZoom(TimelineCanvas.PixelsPerSecond / 1.25);
            e.Handled = true;
            return;
        }

        // Single-Key Transport, Navigation & Tool Shortcuts
        if (e.Key == Key.Space)
        {
            Vm.TogglePlayPause();
            e.Handled = true;
        }
        else if (e.Key == Key.J && !ctrl)
        {
            Vm.ShuttleReverse();
            e.Handled = true;
        }
        else if (e.Key == Key.K && !ctrl)
        {
            if (Vm.Timeline.IsPlaying)
                Vm.ShuttleStop();
            else
                Vm.Inspector.AddMasterKeyframeAtPlayhead();
            e.Handled = true;
        }
        else if (e.Key == Key.L && !ctrl)
        {
            Vm.ShuttleForward();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && !ctrl)
        {
            double step = shift ? -1.0 : (-1.0 / Math.Max(1.0, Vm.Document.Fps));
            Vm.StepFrame(step);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && !ctrl)
        {
            double step = shift ? 1.0 : (1.0 / Math.Max(1.0, Vm.Document.Fps));
            Vm.StepFrame(step);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && !ctrl)
        {
            Vm.StopPlayback();
            Vm.Timeline.JumpToPreviousCutPoint();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && !ctrl)
        {
            Vm.StopPlayback();
            Vm.Timeline.JumpToNextCutPoint();
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            Vm.SeekToStart();
            e.Handled = true;
        }
        else if (e.Key == Key.End)
        {
            Vm.StopPlayback();
            Vm.Timeline.PlayheadTime = Math.Max(0, Vm.Document.TotalDuration);
            Vm.NotifyTimelineAndCanvasChanged();
            e.Handled = true;
        }
        else if (e.Key == Key.I && !ctrl)
        {
            Vm.SetMarkIn();
            e.Handled = true;
        }
        else if (e.Key == Key.O && !ctrl)
        {
            Vm.SetMarkOut();
            e.Handled = true;
        }
        else if (e.Key == Key.F9)
        {
            Vm.InsertSourceClipToTimeline();
            e.Handled = true;
        }
        else if (e.Key == Key.F10)
        {
            Vm.OverwriteSourceClipToTimeline();
            e.Handled = true;
        }
        else if (e.Key == Key.V && !ctrl)
        {
            ActivateTimelineTool(NleTimelineTool.Selection);
            e.Handled = true;
        }
        else if (e.Key == Key.C && !ctrl)
        {
            ActivateTimelineTool(NleTimelineTool.Razor);
            e.Handled = true;
        }
        else if (e.Key == Key.S && !ctrl)
        {
            Vm.Timeline.SplitAtPlayhead();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && !ctrl)
        {
            ActivateTimelineTool(NleTimelineTool.Slip);
            e.Handled = true;
        }
        else if (e.Key == Key.H && !ctrl)
        {
            ActivateTimelineTool(NleTimelineTool.Hand);
            e.Handled = true;
        }
        else if (e.Key == Key.T && !ctrl)
        {
            ActivateTimelineTool(NleTimelineTool.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.M && !ctrl)
        {
            Vm.Timeline.AddMarkerAtPlayhead();
            e.Handled = true;
        }
        else if (e.Key == Key.F && shift)
        {
            Vm.Timeline.FreezeFrameAtPlayhead();
            e.Handled = true;
        }
        else if (e.Key == Key.N && !ctrl)
        {
            if (TimelineCanvas != null)
            {
                TimelineCanvas.SnappingEnabled = !TimelineCanvas.SnappingEnabled;
                Vm.Timeline.SnappingEnabled = TimelineCanvas.SnappingEnabled;
            }
            e.Handled = true;
        }
        else if ((e.Key == Key.OemBackslash || e.Key == Key.OemPipe) && !ctrl)
        {
            TimelineCanvas?.FitTimelineToWindow();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (shift)
                Vm.Timeline.RippleDeleteSelectedClip();
            else
                Vm.Timeline.DeleteSelectedClip();
            Vm.Inspector.BindClip(null);
            e.Handled = true;
        }
    }

    private void ActivateTimelineTool(NleTimelineTool tool)
    {
        TimelineCanvas?.SetActiveTool(tool);
        var activeBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
        var idleBrush = new SolidColorBrush(Color.FromRgb(34, 36, 44));

        if (BtnToolSelect != null) BtnToolSelect.Background = tool == NleTimelineTool.Selection ? activeBrush : idleBrush;
        if (BtnToolRazor != null) BtnToolRazor.Background = tool == NleTimelineTool.Razor ? activeBrush : idleBrush;
        if (BtnToolSlip != null) BtnToolSlip.Background = tool == NleTimelineTool.Slip ? activeBrush : idleBrush;
        if (BtnToolHand != null) BtnToolHand.Background = tool == NleTimelineTool.Hand ? activeBrush : idleBrush;
        if (BtnToolText != null) BtnToolText.Background = tool == NleTimelineTool.Text ? activeBrush : idleBrush;

        if (Vm != null)
        {
            Vm.StatusMessage = $"Active Tool: {tool}";
        }
    }

    private void OnSelectToolClick(object? sender, RoutedEventArgs e) => ActivateTimelineTool(NleTimelineTool.Selection);
    private void OnRazorToolClick(object? sender, RoutedEventArgs e) => ActivateTimelineTool(NleTimelineTool.Razor);
    private void OnSlipToolClick(object? sender, RoutedEventArgs e) => ActivateTimelineTool(NleTimelineTool.Slip);
    private void OnHandToolClick(object? sender, RoutedEventArgs e) => ActivateTimelineTool(NleTimelineTool.Hand);
    private void OnTextToolClick(object? sender, RoutedEventArgs e) => ActivateTimelineTool(NleTimelineTool.Text);

    private void OnFitTimelineClick(object? sender, RoutedEventArgs e) => TimelineCanvas?.FitTimelineToWindow();

    private void OnSnapToggleClick(object? sender, RoutedEventArgs e)
    {
        if (TimelineCanvas != null && Vm != null)
        {
            TimelineCanvas.SnappingEnabled = Vm.Timeline.SnappingEnabled;
        }
    }

    private void OnStepBackwardClick(object? sender, RoutedEventArgs e)
    {
        Vm?.StepFrame(-1.0 / Math.Max(1.0, Vm.Document.Fps));
    }

    private void OnStepForwardClick(object? sender, RoutedEventArgs e)
    {
        Vm?.StepFrame(1.0 / Math.Max(1.0, Vm.Document.Fps));
    }

    private void OnShuttleReverseClick(object? sender, RoutedEventArgs e) => Vm?.ShuttleReverse();
    private void OnShuttleStopClick(object? sender, RoutedEventArgs e) => Vm?.ShuttleStop();
    private void OnShuttleForwardClick(object? sender, RoutedEventArgs e) => Vm?.ShuttleForward();

    private void OnToggleSafeMarginsClick(object? sender, RoutedEventArgs e)
    {
        if (Vm != null)
            Vm.ShowSafeMargins = !Vm.ShowSafeMargins;
    }

    private void OnMediaPoolAssetDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm?.MediaBin.SelectedAsset != null)
        {
            Vm.LoadAssetIntoSourceMonitor(Vm.MediaBin.SelectedAsset);
        }
    }

    private void OnLoadSelectedToSourceClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.MediaBin.SelectedAsset != null)
        {
            Vm.LoadAssetIntoSourceMonitor(Vm.MediaBin.SelectedAsset);
        }
    }

    private void OnTimelineZoomSliderChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressZoomSync) return;
        TimelineCanvas?.SetZoom(e.NewValue);
    }

    private void OnTimelineScrollBarValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressScrollSync) return;
        TimelineCanvas?.SetScrollOffsetSeconds(e.NewValue);
    }

    private void OnWindowFileDrop(object? sender, DragEventArgs e)
    {
        if (Vm == null) return;
        var files = e.Data.GetFiles();
        if (files == null) return;

        var localPaths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();

        if (localPaths.Count > 0)
        {
            Vm.MediaBin.ImportFiles(localPaths);
        }
    }

    private async void OnImportMediaClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Video, Audio, or Image Files",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Media Files")
                {
                    Patterns = new[] { "*.mp4", "*.mov", "*.mkv", "*.avi", "*.webm", "*.wav", "*.mp3", "*.flac", "*.aac", "*.png", "*.jpg", "*.jpeg" }
                },
                FilePickerFileTypes.All
            }
        });

        var localPaths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();

        if (localPaths.Count > 0)
        {
            Vm.MediaBin.ImportFiles(localPaths);
        }
    }

    private void OnNewProjectClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        Vm.RequestActionWithUnsavedCheck("creating a new project", () =>
        {
            Vm.CreateNewProject();
        });
    }

    private async void OnSaveProjectClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        if (!string.IsNullOrWhiteSpace(Vm.CurrentProjectFilePath))
        {
            await Vm.SaveProjectToFileAsync(Vm.CurrentProjectFilePath);
            return;
        }

        await PromptAndSaveProjectAsAsync();
    }

    private async void OnSaveProjectAsClick(object? sender, RoutedEventArgs e)
    {
        await PromptAndSaveProjectAsAsync();
    }

    private async Task<bool> PromptAndSaveProjectAsAsync()
    {
        if (Vm == null) return false;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Vortex Cut Studio Project (.vcsproj)",
            DefaultExtension = "vcsproj",
            SuggestedFileName = $"{Vm.ProjectName}.vcsproj",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Vortex Cut Studio Project (.vcsproj)") { Patterns = new[] { "*.vcsproj", "*.vortex", "*.scproj", "*.aive", "*.json" } }
            }
        });

        string? localPath = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            await Vm.SaveProjectToFileAsync(localPath);
            return true;
        }
        return false;
    }

    private void OnLoadProjectClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        Vm.RequestActionWithUnsavedCheck("opening another project", async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Vortex Cut Studio Project (.vcsproj)",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Vortex Cut Studio Project") { Patterns = new[] { "*.vcsproj", "*.vortex", "*.scproj", "*.aive", "*.json" } }
                }
            });

            string? localPath = files.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                await Vm.LoadProjectFromFileAsync(localPath);
            }
        });
    }

    private async void OnArchiveProjectClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Destination Folder to Archive / Collect Project & Media",
            AllowMultiple = false
        });

        string? folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            string targetSubfolder = Path.Combine(folderPath, $"{Vm.ProjectName}_Archive");
            await Vm.ArchiveProjectToFolderAsync(targetSubfolder);
        }
    }

    private async void OnRelinkMissingMediaClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Media Search Folder to Relink Offline Files",
            AllowMultiple = false
        });

        string? folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            Vm.RelinkMissingMediaFromFolder(folderPath);
        }
    }

    private async void OnExportInterchangeClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Timeline for DaVinci Resolve / Premiere Pro",
            DefaultExtension = "xml",
            SuggestedFileName = $"{Vm.ProjectName}_Resolve.xml",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Final Cut Pro 7 XML (.xml)") { Patterns = new[] { "*.xml" } },
                new FilePickerFileType("SMPTE CMX 3600 EDL (.edl)") { Patterns = new[] { "*.edl" } }
            }
        });

        string? localPath = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            await Vm.ExportInterchangeAsync(localPath);
        }
    }

    private async void OnBrowseLutFileClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load 3D / 1D Color LUT (.cube)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Adobe / DaVinci Resolve LUT (.cube)") { Patterns = new[] { "*.cube" } },
                FilePickerFileTypes.All
            }
        });

        string? localPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            Vm.Inspector.SetLutFilePath(localPath);
        }
    }

    private void OnUnsavedModalDiscardClick(object? sender, RoutedEventArgs e)
    {
        Vm?.ExecutePendingUnsavedProceedAction();
    }

    private async void OnUnsavedModalSaveAndContinueClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        bool saved;
        if (!string.IsNullOrWhiteSpace(Vm.CurrentProjectFilePath))
        {
            await Vm.SaveProjectToFileAsync(Vm.CurrentProjectFilePath);
            saved = true;
        }
        else
        {
            saved = await PromptAndSaveProjectAsAsync();
        }

        if (saved)
        {
            Vm.ExecutePendingUnsavedProceedAction();
        }
    }

    private async void OnBrowseExportPathClick(object? sender, RoutedEventArgs e)
    {
        if (Vm == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Video",
            DefaultExtension = "mp4",
            SuggestedFileName = $"{Vm.ProjectName}_Export.mp4",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("MP4 Video") { Patterns = new[] { "*.mp4" } },
                new FilePickerFileType("QuickTime MOV") { Patterns = new[] { "*.mov" } },
                new FilePickerFileType("WAV Audio") { Patterns = new[] { "*.wav" } }
            }
        });

        string? localPath = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            Vm.ExportOutputPath = localPath;
        }
    }
}
