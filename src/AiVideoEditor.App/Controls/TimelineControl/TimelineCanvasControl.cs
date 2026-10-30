using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AiVideoEditor.Core.Commands;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using AiVideoEditor.MediaEngine.Decoding;
using SkiaSharp;

namespace AiVideoEditor.App.Controls.TimelineControl;

public enum NleTimelineTool
{
    Selection, // V
    Razor,     // C
    Slip,      // Y
    Hand,      // H
    Text       // T
}

public sealed class TimelineCanvasControl : Control
{
    private const double HeaderWidth = 186.0;
    private const double RulerHeight = 28.0;
    private const double TrackHeight = 56.0;
    private const double TrackGap = 1.0;
    private const double TrimHandleWidth = 8.0;

    private ProjectDocument? _document;
    private UndoRedoManager? _undoRedo;
    private TimelineService? _timelineService;
    private VideoFrameReader? _frameReader;

    private readonly Dictionary<Guid, List<WriteableBitmap>> _filmstripCache = new();
    private readonly HashSet<Guid> _pendingFilmstripLoads = new();
    private const int MaxCachedFilmstrips = 40;

    public double PlayheadTime { get; private set; }
    public double PixelsPerSecond { get; private set; } = 60.0;
    public double ScrollOffsetSeconds { get; private set; }
    public TimelineClip? SelectedClip { get; private set; }
    public NleTimelineTool ActiveTool { get; private set; } = NleTimelineTool.Selection;
    public bool SnappingEnabled { get; set; } = true;

    private double? _activeSnapLineTime;
    private double? _razorHoverTime;

    public event Action<double>? PlayheadScrubbed;
    public event Action<TimelineClip?>? ClipSelected;
    public event Action? TimelineMutated;
    public event Action<double, double, double>? ScrollMetricsChanged;
    public event Action<double>? ZoomChanged;
    public event Action<string, double>? ContextMenuActionRequested;

    private enum DragInteraction
    {
        None,
        ScrubPlayhead,
        MoveClip,
        TrimClipLeft,
        TrimClipRight,
        TrimTransitionIn,
        TrimTransitionOut,
        SlipClipContent,
        PanTimeline
    }

    private DragInteraction _dragMode = DragInteraction.None;
    private TimelineClip? _activeClip;
    private TimelineClip? _activeClipBeforeSnapshot;
    private TimelineClip? _linkedAudioClip;
    private TimelineTrack? _activeClipSourceTrack;
    private Point _dragStartPoint;
    private double _dragOrigStart;
    private double _dragOrigDuration;
    private double _dragOrigSourceIn;
    private double _dragOrigSourceOut;
    private double _dragOrigScrollSeconds;
    private double _dragOrigTransitionDur;

    public TimelineCanvasControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public void BindDocument(
        ProjectDocument document,
        UndoRedoManager undoRedo,
        TimelineService? timelineService = null,
        VideoFrameReader? frameReader = null)
    {
        _document = document;
        _undoRedo = undoRedo;
        if (timelineService != null) _timelineService = timelineService;
        if (frameReader != null) _frameReader = frameReader;
        ClampScrollAndNotify();
        InvalidateVisual();
    }

    public double GetMaxScrollSeconds()
    {
        double total = _document?.TotalDuration ?? 10.0;
        return Math.Max(60.0, total + 30.0);
    }

    public double GetViewportDurationSeconds()
    {
        double trackViewportW = Math.Max(100.0, Bounds.Width - HeaderWidth);
        return Math.Max(1.0, trackViewportW / Math.Max(2.0, PixelsPerSecond));
    }

    public void SetScrollOffsetSeconds(double offsetSeconds)
    {
        double maxScroll = GetMaxScrollSeconds();
        double clamped = Math.Clamp(offsetSeconds, 0.0, maxScroll);
        if (Math.Abs(ScrollOffsetSeconds - clamped) < 0.0005)
            return;

        ScrollOffsetSeconds = clamped;
        ClampScrollAndNotify();
        InvalidateVisual();
    }

    private void ClampScrollAndNotify()
    {
        double maxScroll = GetMaxScrollSeconds();
        ScrollOffsetSeconds = Math.Clamp(ScrollOffsetSeconds, 0.0, maxScroll);
        ScrollMetricsChanged?.Invoke(ScrollOffsetSeconds, maxScroll, GetViewportDurationSeconds());
    }

    public void SetActiveTool(NleTimelineTool tool)
    {
        ActiveTool = tool;
        _razorHoverTime = null;
        Cursor = tool switch
        {
            NleTimelineTool.Razor => new Cursor(StandardCursorType.Cross),
            NleTimelineTool.Hand => new Cursor(StandardCursorType.Hand),
            NleTimelineTool.Slip => new Cursor(StandardCursorType.SizeWestEast),
            NleTimelineTool.Text => new Cursor(StandardCursorType.Ibeam),
            _ => Cursor.Default
        };
        InvalidateVisual();
    }

    public void SetPlayhead(double timeSeconds)
    {
        PlayheadTime = Math.Max(0, timeSeconds);

        // Auto-scroll timeline during playback if playhead leaves visible right edge
        double viewportDur = GetViewportDurationSeconds();
        if (PlayheadTime > ScrollOffsetSeconds + viewportDur * 0.92)
        {
            ScrollOffsetSeconds = Math.Max(0, PlayheadTime - viewportDur * 0.15);
            ClampScrollAndNotify();
        }
        else if (PlayheadTime < ScrollOffsetSeconds)
        {
            ScrollOffsetSeconds = Math.Max(0, PlayheadTime);
            ClampScrollAndNotify();
        }

        InvalidateVisual();
    }

    public void SetZoom(double pixelsPerSecond)
    {
        double clamped = Math.Clamp(pixelsPerSecond, 2.0, 600.0);
        if (Math.Abs(PixelsPerSecond - clamped) < 0.01) return;
        PixelsPerSecond = clamped;
        ClampScrollAndNotify();
        ZoomChanged?.Invoke(PixelsPerSecond);
        InvalidateVisual();
    }

    public void SelectClip(TimelineClip? clip)
    {
        SelectedClip = clip;
        ClipSelected?.Invoke(clip);
        InvalidateVisual();
    }

    public void FitTimelineToWindow()
    {
        double totalDur = Math.Max(5.0, _document?.TotalDuration ?? 10.0);
        double usableW = Math.Max(160.0, Bounds.Width - HeaderWidth - 36.0);
        ScrollOffsetSeconds = 0;
        SetZoom(Math.Clamp(usableW / totalDur, 2.0, 300.0));
    }

    public double TimeToX(double timeSec) => HeaderWidth + ((timeSec - ScrollOffsetSeconds) * PixelsPerSecond);
    public double XToTime(double mouseX) => Math.Max(0, ScrollOffsetSeconds + ((mouseX - HeaderWidth) / PixelsPerSecond));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width <= 10 || bounds.Height <= 10) return;

        // #17181E Panel Background
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(23, 24, 30)), new Rect(0, 0, bounds.Width, bounds.Height));

        if (_document == null) return;

        var hairlinePen = new Pen(new SolidColorBrush(Color.FromRgb(44, 47, 59)), 1);

        // 1. Render Tracks & Clips
        double viewStartSec = Math.Max(0.0, ScrollOffsetSeconds);
        double viewEndSec = XToTime(bounds.Width);

        for (int i = 0; i < _document.Tracks.Count; i++)
        {
            var track = _document.Tracks[i];
            double trackTop = RulerHeight + i * (TrackHeight + TrackGap);
            var trackLaneRect = new Rect(HeaderWidth, trackTop, Math.Max(0, bounds.Width - HeaderWidth), TrackHeight);

            var laneColor = track.IsMuted
                ? Color.FromRgb(15, 16, 20)
                : Color.FromRgb(21, 22, 28);
            context.FillRectangle(new SolidColorBrush(laneColor), trackLaneRect);
            context.DrawLine(hairlinePen,
                new Point(HeaderWidth, trackTop + TrackHeight),
                new Point(bounds.Width, trackTop + TrackHeight));

            int clipCount = track.Clips.Count;
            // Draw normal clips first (with viewport culling)
            for (int c = 0; c < clipCount; c++)
            {
                var clip = track.Clips[c];
                if (clip.IsTransitionClip) continue;
                if ((clip.TimelineStart + clip.Duration) < viewStartSec || clip.TimelineStart > viewEndSec) continue;
                DrawClipBlock(context, track, clip, trackTop);
            }
            // Draw standalone transition blocks on top so handles & badges remain visible over cut boundaries
            for (int c = 0; c < clipCount; c++)
            {
                var clip = track.Clips[c];
                if (!clip.IsTransitionClip) continue;
                if ((clip.TimelineStart + clip.Duration) < viewStartSec || clip.TimelineStart > viewEndSec) continue;
                DrawClipBlock(context, track, clip, trackTop);
            }

            // Track Header (#1F2129)
            var headerRect = new Rect(0, trackTop, HeaderWidth, TrackHeight);
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(31, 33, 41)), headerRect);
            context.DrawLine(hairlinePen, new Point(0, trackTop + TrackHeight), new Point(HeaderWidth, trackTop + TrackHeight));
            context.DrawLine(hairlinePen, new Point(HeaderWidth, trackTop), new Point(HeaderWidth, trackTop + TrackHeight));

            DrawTrackHeader(context, track, headerRect);
        }

        // 2. Render Timecode Ruler & Markers (#1F2129)
        DrawTimeRuler(context, bounds.Width);

        // 3. Magnetic Snap Guideline (#3B82F6)
        if (_activeSnapLineTime.HasValue)
        {
            double snapX = TimeToX(_activeSnapLineTime.Value);
            if (snapX >= HeaderWidth && snapX <= bounds.Width)
            {
                var snapPen = new Pen(new SolidColorBrush(Color.FromRgb(59, 130, 246)), 1.2);
                context.DrawLine(snapPen, new Point(snapX, RulerHeight), new Point(snapX, bounds.Height));
            }
        }

        // 4. Razor Tool Hover Line
        if (ActiveTool == NleTimelineTool.Razor && _razorHoverTime.HasValue)
        {
            double rx = TimeToX(_razorHoverTime.Value);
            if (rx >= HeaderWidth && rx <= bounds.Width)
            {
                var razorPen = new Pen(new SolidColorBrush(Color.FromRgb(239, 68, 68)), 1.2, DashStyle.Dash);
                context.DrawLine(razorPen, new Point(rx, RulerHeight), new Point(rx, bounds.Height));
            }
        }

        // 5. Red Playhead Needle (#EF4444)
        double playheadX = TimeToX(PlayheadTime);
        if (playheadX >= HeaderWidth && playheadX <= bounds.Width)
        {
            var playheadBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            var playheadPen = new Pen(playheadBrush, 1.5);
            context.DrawLine(playheadPen, new Point(playheadX, 0), new Point(playheadX, bounds.Height));

            var headGeometry = new StreamGeometry();
            using (var geoCtx = headGeometry.Open())
            {
                geoCtx.BeginFigure(new Point(playheadX - 6, 0), true);
                geoCtx.LineTo(new Point(playheadX + 6, 0));
                geoCtx.LineTo(new Point(playheadX + 6, 9));
                geoCtx.LineTo(new Point(playheadX, 15));
                geoCtx.LineTo(new Point(playheadX - 6, 9));
                geoCtx.EndFigure(true);
            }
            context.DrawGeometry(playheadBrush, null, headGeometry);
        }
    }

    private void DrawTrackHeader(DrawingContext context, TimelineTrack track, Rect rect)
    {
        Color codeFill = track.Type switch
        {
            TrackType.Video => Color.FromRgb(37, 75, 124),    // #254B7C
            TrackType.Audio => Color.FromRgb(30, 94, 74),     // #1E5E4A
            _ => Color.FromRgb(124, 77, 37)                   // #7C4D25
        };

        var codeRect = new Rect(rect.X + 6, rect.Y + 7, 28, 18);
        context.FillRectangle(new SolidColorBrush(codeFill), codeRect, 2f);

        var codeText = new FormattedText(
            string.IsNullOrEmpty(track.TrackCode) ? "V1" : track.TrackCode,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono, Consolas, Monospace", FontStyle.Normal, FontWeight.Bold),
            10,
            new SolidColorBrush(Color.FromRgb(243, 244, 246)));
        context.DrawText(codeText, new Point(codeRect.X + 5, codeRect.Y + 2));

        var titleText = new FormattedText(
            track.Name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Medium),
            11,
            new SolidColorBrush(Color.FromRgb(243, 244, 246)))
        {
            MaxTextWidth = 136,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        context.DrawText(titleText, new Point(rect.X + 40, rect.Y + 8));

        var muteRect = new Rect(rect.X + 40, rect.Y + 31, 22, 17);
        var soloRect = new Rect(rect.X + 66, rect.Y + 31, 22, 17);
        var lockRect = new Rect(rect.X + 92, rect.Y + 31, 22, 17);
        var visRect = new Rect(rect.X + 118, rect.Y + 31, 22, 17);

        DrawHeaderToggleButton(context, muteRect, "M", track.IsMuted, Color.FromRgb(239, 68, 68));
        DrawHeaderToggleButton(context, soloRect, "S", track.IsSolo, Color.FromRgb(245, 158, 11));
        DrawHeaderToggleButton(context, lockRect, "L", track.IsLocked, Color.FromRgb(59, 130, 246));
        if (track.Type != TrackType.Audio)
        {
            DrawHeaderToggleButton(context, visRect, "V", !track.IsVisible, Color.FromRgb(107, 114, 128));
        }
    }

    private static void DrawHeaderToggleButton(DrawingContext context, Rect rect, string letter, bool active, Color activeColor)
    {
        var fill = active ? new SolidColorBrush(activeColor) : new SolidColorBrush(Color.FromRgb(40, 43, 54));
        var border = new Pen(new SolidColorBrush(Color.FromRgb(44, 47, 59)), 1);
        context.FillRectangle(fill, rect, 2f);
        context.DrawRectangle(border, rect, 2f);

        var ft = new FormattedText(
            letter,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
            9.5,
            new SolidColorBrush(active ? Color.FromRgb(15, 16, 20) : Color.FromRgb(156, 163, 175)));
        context.DrawText(ft, new Point(rect.X + 6, rect.Y + 2));
    }

    private List<WriteableBitmap>? GetOrCreateFilmstripBitmaps(MediaAsset asset)
    {
        if (_filmstripCache.TryGetValue(asset.Id, out var existing))
            return existing;

        if (_frameReader == null || !asset.HasVideo || string.IsNullOrWhiteSpace(asset.FilePath) || !File.Exists(asset.FilePath))
            return null;

        if (_pendingFilmstripLoads.Contains(asset.Id))
            return null;

        _pendingFilmstripLoads.Add(asset.Id);

        var reader = _frameReader;
        Task.Run(() =>
        {
            try
            {
                var skThumbs = ThumbnailStripService.GetOrBuildFilmstrip(asset, reader, 6);
                if (skThumbs.Count == 0)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => _pendingFilmstripLoads.Remove(asset.Id));
                    return;
                }

                var list = new List<WriteableBitmap>(skThumbs.Count);
                foreach (var skBmp in skThumbs)
                {
                    var wb = new WriteableBitmap(
                        new PixelSize(skBmp.Width, skBmp.Height),
                        new Vector(96, 96),
                        PixelFormat.Bgra8888,
                        AlphaFormat.Premul);
                    using (var locked = wb.Lock())
                    {
                        int bytes = Math.Min(skBmp.ByteCount, locked.RowBytes * skBmp.Height);
                        unsafe
                        {
                            Buffer.MemoryCopy((void*)skBmp.GetPixels(), (void*)locked.Address, bytes, bytes);
                        }
                    }
                    list.Add(wb);
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _pendingFilmstripLoads.Remove(asset.Id);
                    if (_filmstripCache.Count >= MaxCachedFilmstrips)
                    {
                        using var enumerator = _filmstripCache.Keys.GetEnumerator();
                        if (enumerator.MoveNext())
                        {
                            _filmstripCache.Remove(enumerator.Current);
                        }
                    }
                    _filmstripCache[asset.Id] = list;
                    InvalidateVisual();
                });
            }
            catch
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _pendingFilmstripLoads.Remove(asset.Id));
            }
        });

        return null;
    }

    public static Color ResolveTransitionColor(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return Color.FromRgb(59, 130, 246);

        string t = typeName.Trim().ToLowerInvariant();
        if (t.Contains("black"))
            return Color.FromRgb(30, 41, 59);
        if (t.Contains("white") || t.Contains("flash"))
            return Color.FromRgb(226, 232, 240);
        if (t.Contains("wipe"))
            return Color.FromRgb(16, 185, 129);
        if (t.Contains("slide") || t.Contains("push"))
            return Color.FromRgb(245, 158, 11);
        if (t.Contains("fade"))
            return Color.FromRgb(139, 92, 246);
        if (t.Contains("zoom") || t.Contains("blur") || t.Contains("glitch") || t.Contains("pixel") || t.Contains("burn") || t.Contains("radial") || t.Contains("rgb"))
            return Color.FromRgb(236, 72, 153);

        return Color.FromRgb(59, 130, 246);
    }

    private void DrawStandaloneTransitionBlock(DrawingContext context, TimelineTrack track, TimelineClip clip, double trackTop)
    {
        double rawX = TimeToX(clip.TimelineStart);
        double rawW = Math.Max(12.0, clip.Duration * PixelsPerSecond);
        if (rawX + rawW < HeaderWidth || rawX > Bounds.Width) return;

        double visibleX = Math.Max(HeaderWidth, rawX);
        double visibleRight = Math.Min(Bounds.Width, rawX + rawW);
        double visibleW = Math.Max(2.0, visibleRight - visibleX);
        var blockRect = new Rect(visibleX, trackTop + 3, visibleW, TrackHeight - 6);

        Color mainColor = ResolveTransitionColor(clip.TransitionType ?? clip.InTransition?.Type);
        Color borderColor = Color.FromArgb(220, mainColor.R, mainColor.G, mainColor.B);
        Color bgColor = Color.FromArgb(145, (byte)(mainColor.R / 2), (byte)(mainColor.G / 2), (byte)(mainColor.B / 2));

        bool isSelected = SelectedClip?.Id == clip.Id;

        // Background with subtle rounded corners
        context.FillRectangle(new SolidColorBrush(bgColor), blockRect, 4f);

        // Center diagonal split ramp pattern
        var penRamp = new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 1.2);
        context.DrawLine(penRamp, new Point(blockRect.Left, blockRect.Bottom), new Point(blockRect.Right, blockRect.Top));

        // Outline / selection border
        var borderPen = isSelected
            ? new Pen(new SolidColorBrush(Color.FromRgb(245, 158, 11)), 2.0)
            : new Pen(new SolidColorBrush(borderColor), 1.2);
        context.DrawRectangle(borderPen, blockRect, 4f);

        // Edge Drag Handles (Premiere style grip indicators)
        var gripPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1.5);
        if (blockRect.Width > 18)
        {
            context.DrawLine(gripPen, new Point(blockRect.Left + 3, blockRect.Y + 6), new Point(blockRect.Left + 3, blockRect.Bottom - 6));
            context.DrawLine(gripPen, new Point(blockRect.Right - 3, blockRect.Y + 6), new Point(blockRect.Right - 3, blockRect.Bottom - 6));
        }

        // Title and Duration Text
        if (blockRect.Width > 28)
        {
            string typeName = clip.TransitionType ?? "Cross Dissolve";
            string shortLabel = $"⧖ {typeName} ({clip.Duration:0.0#}s)";
            var text = new FormattedText(
                shortLabel,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                10,
                new SolidColorBrush(Color.FromRgb(243, 244, 246)))
            {
                MaxTextWidth = Math.Max(10, blockRect.Width - 10),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };
            context.DrawText(text, new Point(blockRect.X + 6, blockRect.Y + (blockRect.Height - text.Height) / 2));
        }
    }

    private void DrawClipBlock(DrawingContext context, TimelineTrack track, TimelineClip clip, double trackTop)
    {
        if (clip.IsTransitionClip)
        {
            DrawStandaloneTransitionBlock(context, track, clip, trackTop);
            return;
        }

        double rawX = TimeToX(clip.TimelineStart);
        double rawW = Math.Max(4.0, clip.Duration * PixelsPerSecond);
        if (rawX + rawW < HeaderWidth || rawX > Bounds.Width) return;

        // Clip to right of TrackHeaderWidth so clips never bleed over track headers
        double visibleX = Math.Max(HeaderWidth, rawX);
        double visibleRight = Math.Min(Bounds.Width, rawX + rawW);
        double visibleW = Math.Max(2.0, visibleRight - visibleX);
        var clipRect = new Rect(visibleX, trackTop + 2, visibleW, TrackHeight - 4);

        (Color fillCol, Color borderCol) = clip.Kind switch
        {
            ClipKind.AdjustmentLayer => (Color.FromRgb(109, 40, 217), Color.FromRgb(192, 132, 252)), // Studio Violet (#6D28D9)
            ClipKind.CompoundClip => (Color.FromRgb(15, 118, 110), Color.FromRgb(45, 212, 191)),     // Studio Teal (#0F766E)
            _ => track.Type switch
            {
                TrackType.Video => (Color.FromRgb(37, 75, 124), Color.FromRgb(74, 125, 191)),        // #254B7C
                TrackType.Audio => (Color.FromRgb(30, 94, 74), Color.FromRgb(56, 148, 118)),         // #1E5E4A
                _ => (Color.FromRgb(124, 77, 37), Color.FromRgb(181, 118, 63))                       // #7C4D25
            }
        };

        bool isSelected = SelectedClip?.Id == clip.Id;
        context.FillRectangle(new SolidColorBrush(fillCol), clipRect, 3f);

        var asset = _document?.FindAsset(clip.MediaAssetId);

        // Draw Real Video Filmstrip Thumbnails on Video Tracks
        if (asset != null && track.Type == TrackType.Video && visibleW > 28)
        {
            var thumbs = GetOrCreateFilmstripBitmaps(asset);
            if (thumbs != null && thumbs.Count > 0)
            {
                double thumbH = clipRect.Height - 18;
                double thumbW = thumbH * (16.0 / 9.0);
                if (thumbH > 8 && thumbW > 12)
                {
                    using (context.PushClip(new Rect(clipRect.X + 1, clipRect.Y + 16, Math.Max(1, clipRect.Width - 2), thumbH)))
                    {
                        int tileCount = Math.Max(1, (int)Math.Ceiling(rawW / thumbW));
                        int startTile = Math.Clamp((int)Math.Floor((HeaderWidth - rawX) / thumbW), 0, tileCount - 1);
                        int endTile = Math.Clamp((int)Math.Ceiling((Bounds.Width - rawX) / thumbW), 0, tileCount - 1);
                        for (int t = startTile; t <= endTile; t++)
                        {
                            double tileX = rawX + t * thumbW;
                            int idx = (t * thumbs.Count) / Math.Max(1, tileCount);
                            var bmp = thumbs[Math.Clamp(idx, 0, thumbs.Count - 1)];
                            var dest = new Rect(tileX, clipRect.Y + 16, thumbW - 1, thumbH);
                            using (context.PushOpacity(0.58))
                            {
                                context.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), dest);
                            }
                        }
                    }
                }
            }
        }

        // Draw High-Density Audio Waveform inside Audio / Video Clips
        if (asset != null && asset.WaveformPeaks.Length > 0 && (track.Type == TrackType.Audio || track.Type == TrackType.Video))
        {
            var waveColor = track.Type == TrackType.Audio
                ? Color.FromArgb(200, 16, 185, 129)
                : Color.FromArgb(120, 243, 244, 246);
            var wavePen = new Pen(new SolidColorBrush(waveColor), 1.0);
            int numBars = Math.Max(4, (int)(rawW / 3.0));
            double barStep = rawW / numBars;
            double bottomY = clipRect.Bottom - 2;
            double maxWaveH = track.Type == TrackType.Audio ? clipRect.Height * 0.68 : clipRect.Height * 0.35;

            int startBar = Math.Clamp((int)Math.Floor((HeaderWidth - rawX) / barStep), 0, numBars - 1);
            int endBar = Math.Clamp((int)Math.Ceiling((Bounds.Width - rawX) / barStep), 0, numBars - 1);

            for (int b = startBar; b <= endBar; b++)
            {
                double bx = rawX + b * barStep;
                int peakIdx = (b * asset.WaveformPeaks.Length) / numBars;
                float peak = asset.WaveformPeaks[Math.Clamp(peakIdx, 0, asset.WaveformPeaks.Length - 1)];
                double barH = Math.Max(1.5, peak * maxWaveH);
                context.DrawLine(wavePen, new Point(bx, bottomY), new Point(bx, bottomY - barH));
            }
        }

        // Draw Transition boxes & diagonal fade ramps with interactive grip handles
        if (clip.InTransition != null && clip.InTransition.Duration > 0.04)
        {
            double transW = Math.Min(visibleW * 0.48, clip.InTransition.Duration * PixelsPerSecond);
            var trRect = new Rect(clipRect.X, clipRect.Y, transW, clipRect.Height);
            Color transColor = ResolveTransitionColor(clip.InTransition.Type);

            context.FillRectangle(new SolidColorBrush(Color.FromArgb(90, transColor.R, transColor.G, transColor.B)), trRect);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(200, 243, 244, 246)), 1.2),
                new Point(trRect.Left, trRect.Bottom), new Point(trRect.Right, trRect.Top));

            // Interactive grip handle line
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(220, transColor.R, transColor.G, transColor.B)), 1.5),
                new Point(trRect.Right, trRect.Top + 2), new Point(trRect.Right, trRect.Bottom - 2));

            if (transW > 26)
            {
                var badgeText = new FormattedText(
                    $"{clip.InTransition.Duration:0.0#}s",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                    9,
                    new SolidColorBrush(Color.FromRgb(243, 244, 246)))
                {
                    MaxTextWidth = transW - 4,
                    MaxLineCount = 1
                };
                context.DrawText(badgeText, new Point(trRect.Left + 4, trRect.Bottom - 14));
            }
        }
        if (clip.OutTransition != null && clip.OutTransition.Duration > 0.04)
        {
            double transW = Math.Min(visibleW * 0.48, clip.OutTransition.Duration * PixelsPerSecond);
            var trRect = new Rect(clipRect.Right - transW, clipRect.Y, transW, clipRect.Height);
            Color transColor = ResolveTransitionColor(clip.OutTransition.Type);

            context.FillRectangle(new SolidColorBrush(Color.FromArgb(90, transColor.R, transColor.G, transColor.B)), trRect);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(200, 243, 244, 246)), 1.2),
                new Point(trRect.Left, trRect.Top), new Point(trRect.Right, trRect.Bottom));

            // Interactive grip handle line
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(220, transColor.R, transColor.G, transColor.B)), 1.5),
                new Point(trRect.Left, trRect.Top + 2), new Point(trRect.Left, trRect.Bottom - 2));

            if (transW > 26)
            {
                var badgeText = new FormattedText(
                    $"{clip.OutTransition.Duration:0.0#}s",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
                    9,
                    new SolidColorBrush(Color.FromRgb(243, 244, 246)))
                {
                    MaxTextWidth = transW - 4,
                    MaxLineCount = 1
                };
                context.DrawText(badgeText, new Point(trRect.Right - badgeText.Width - 4, trRect.Bottom - 14));
            }
        }

        // Clip Header Strip & Title
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(115, 15, 16, 20)),
            new Rect(clipRect.X, clipRect.Y, clipRect.Width, 16));

        string label = clip.TextData != null ? clip.TextData.Text : clip.Label;
        if (clip.IsFreezeFrame) label = $"❄ {label}";
        if (clip.ReversePlayback) label = $"◀ {label}";
        if (Math.Abs(clip.Speed - 1.0) > 0.02) label = $"[{clip.Speed:0.##}x] {label}";
        if (clip.SubjectMatteEnabled) label = $"[BG Removed] {label}";
        if (clip.DialogueIsolateEnabled) label = $"[Clean Voice] {label}";

        var ft = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold),
            10,
            new SolidColorBrush(Color.FromRgb(243, 244, 246)))
        {
            MaxTextWidth = Math.Max(8, visibleW - 10),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        context.DrawText(ft, new Point(clipRect.X + 5, clipRect.Y + 2));

        // On-Clip Rubber-Band Line (Opacity on Video/Subtitle clips, Volume dB on Audio clips)
        double bandTop = clipRect.Y + 18;
        double bandBottom = clipRect.Bottom - 6;
        double bandH = Math.Max(6, bandBottom - bandTop);
        var rubberPen = new Pen(new SolidColorBrush(Color.FromArgb(145, 255, 255, 255)), 1.0);
        if (track.Type == TrackType.Audio)
        {
            double normVol = Math.Clamp((clip.GainDb + 36.0) / 48.0, 0.0, 1.0);
            double bandY = bandBottom - normVol * bandH;
            context.DrawLine(rubberPen, new Point(clipRect.X + 2, bandY), new Point(clipRect.Right - 2, bandY));
        }
        else
        {
            double normOp = Math.Clamp(clip.Opacity, 0.0, 1.0);
            double bandY = bandBottom - normOp * bandH;
            context.DrawLine(rubberPen, new Point(clipRect.X + 2, bandY), new Point(clipRect.Right - 2, bandY));
        }

        // Draw Gold Keyframe Diamonds (◆ #F59E0B) along the bottom of the clip
        var kfTimes = clip.GetAllKeyframeRelativeTimes();
        if (kfTimes.Count > 0)
        {
            var goldBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            var kfLinePen = new Pen(new SolidColorBrush(Color.FromArgb(180, 245, 158, 11)), 1.2);
            double ky = clipRect.Bottom - 6;

            for (int k = 0; k < kfTimes.Count; k++)
            {
                double kx = TimeToX(clip.TimelineStart + kfTimes[k]);
                if (k + 1 < kfTimes.Count)
                {
                    double nextKx = TimeToX(clip.TimelineStart + kfTimes[k + 1]);
                    if (nextKx >= HeaderWidth && kx <= Bounds.Width)
                    {
                        context.DrawLine(kfLinePen,
                            new Point(Math.Max(HeaderWidth, kx), ky),
                            new Point(Math.Min(Bounds.Width, nextKx), ky));
                    }
                }

                if (kx >= HeaderWidth + 4 && kx <= Bounds.Width - 4)
                {
                    var dGeo = new StreamGeometry();
                    using (var g = dGeo.Open())
                    {
                        g.BeginFigure(new Point(kx, ky - 4), true);
                        g.LineTo(new Point(kx + 4, ky));
                        g.LineTo(new Point(kx, ky + 4));
                        g.LineTo(new Point(kx - 4, ky));
                        g.EndFigure(true);
                    }
                    context.DrawGeometry(goldBrush, new Pen(Brushes.Black, 0.7), dGeo);
                }
            }
        }

        // Border & Trim Handles
        if (isSelected)
        {
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(243, 244, 246)), 1.6), clipRect, 3f);
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                new Rect(clipRect.X, clipRect.Y + 4, 3, clipRect.Height - 8));
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                new Rect(clipRect.Right - 3, clipRect.Y + 4, 3, clipRect.Height - 8));
        }
        else
        {
            context.DrawRectangle(new Pen(new SolidColorBrush(borderCol), 1.0), clipRect, 3f);
        }
    }

    private void DrawTimeRuler(DrawingContext context, double width)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(31, 33, 41)), new Rect(0, 0, width, RulerHeight));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(44, 47, 59)), 1),
            new Point(0, RulerHeight), new Point(width, RulerHeight));

        double fps = _document?.Fps ?? 24.0;
        string tcHeader = TimecodeCalculator.SecondsToSmpte(PlayheadTime, fps);
        var headerText = new FormattedText(
            tcHeader,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono, JetBrains Mono, Consolas, Monospace", FontStyle.Normal, FontWeight.Bold),
            11,
            new SolidColorBrush(Color.FromRgb(239, 68, 68)));
        context.DrawText(headerText, new Point(12, 6));

        double stepSeconds = PixelsPerSecond >= 160 ? 0.5
            : PixelsPerSecond >= 80 ? 1.0
            : PixelsPerSecond >= 35 ? 2.0
            : PixelsPerSecond >= 15 ? 5.0
            : PixelsPerSecond >= 6 ? 10.0
            : 30.0;

        double firstTick = Math.Floor(ScrollOffsetSeconds / stepSeconds) * stepSeconds;
        double maxTime = XToTime(width) + stepSeconds;
        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(107, 114, 128)), 1);
        var minorTickPen = new Pen(new SolidColorBrush(Color.FromRgb(65, 70, 82)), 1);

        for (double t = Math.Max(0, firstTick); t <= maxTime; t += stepSeconds)
        {
            double x = TimeToX(t);
            if (x >= HeaderWidth)
            {
                context.DrawLine(tickPen, new Point(x, RulerHeight - 8), new Point(x, RulerHeight));
                string smpte = TimecodeCalculator.SecondsToSmpte(t, fps);
                var label = new FormattedText(
                    smpte,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Cascadia Mono, Consolas, Monospace"),
                    9.5,
                    new SolidColorBrush(Color.FromRgb(156, 163, 175)));
                context.DrawText(label, new Point(x + 3, 4));
            }

            // Minor sub-ticks
            for (int sub = 1; sub < 4; sub++)
            {
                double subX = TimeToX(t + (stepSeconds * sub / 4.0));
                if (subX >= HeaderWidth && subX <= width)
                {
                    context.DrawLine(minorTickPen, new Point(subX, RulerHeight - 4), new Point(subX, RulerHeight));
                }
            }
        }

        // Draw Timeline Mark In / Out Range Bar on Ruler
        if (_document != null && (_document.TimelineMarkIn.HasValue || _document.TimelineMarkOut.HasValue))
        {
            double inTime = _document.TimelineMarkIn ?? 0.0;
            double outTime = _document.TimelineMarkOut ?? Math.Max(inTime + 0.5, _document.TotalDuration);
            if (outTime > inTime)
            {
                double inX = Math.Max(HeaderWidth, TimeToX(inTime));
                double outX = Math.Min(width, TimeToX(outTime));
                if (outX > inX)
                {
                    context.FillRectangle(
                        new SolidColorBrush(Color.FromArgb(65, 59, 130, 246)),
                        new Rect(inX, 0, outX - inX, RulerHeight));
                    var ioPen = new Pen(new SolidColorBrush(Color.FromRgb(59, 130, 246)), 1.6);
                    context.DrawLine(ioPen, new Point(inX, 0), new Point(inX, RulerHeight));
                    context.DrawLine(ioPen, new Point(outX, 0), new Point(outX, RulerHeight));
                }
            }
        }

        // Draw Timeline Markers (🚩)
        if (_document?.Markers != null)
        {
            var markerBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            foreach (var marker in _document.Markers)
            {
                double mx = TimeToX(marker.TimeSeconds);
                if (mx < HeaderWidth || mx > width) continue;

                var flagGeo = new StreamGeometry();
                using (var g = flagGeo.Open())
                {
                    g.BeginFigure(new Point(mx, 2), true);
                    g.LineTo(new Point(mx + 8, 6));
                    g.LineTo(new Point(mx, 10));
                    g.EndFigure(true);
                }
                context.DrawGeometry(markerBrush, null, flagGeo);
                context.DrawLine(new Pen(markerBrush, 1.0, DashStyle.Dash), new Point(mx, 2), new Point(mx, RulerHeight));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_document == null) return;

        Focus();
        var pt = e.GetPosition(this);
        var pointProps = e.GetCurrentPoint(this).Properties;

        // Middle-click drag always pans timeline
        if (pointProps.IsMiddleButtonPressed)
        {
            _dragMode = DragInteraction.PanTimeline;
            _dragStartPoint = pt;
            _dragOrigScrollSeconds = ScrollOffsetSeconds;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.Hand);
            return;
        }

        // 1. Track Header Mute [M], Solo [S], Lock [L], Visibility [V], & Right-Click Context Menu
        if (pt.X < HeaderWidth && pt.Y > RulerHeight)
        {
            int trackIdx = (int)((pt.Y - RulerHeight) / (TrackHeight + TrackGap));
            if (trackIdx >= 0 && trackIdx < _document.Tracks.Count)
            {
                var track = _document.Tracks[trackIdx];
                if (pointProps.IsRightButtonPressed)
                {
                    ShowTrackHeaderContextMenu(track);
                    e.Handled = true;
                    return;
                }

                double trackTop = RulerHeight + trackIdx * (TrackHeight + TrackGap);
                var muteRect = new Rect(40, trackTop + 31, 22, 17);
                var soloRect = new Rect(66, trackTop + 31, 22, 17);
                var lockRect = new Rect(92, trackTop + 31, 22, 17);
                var visRect = new Rect(118, trackTop + 31, 22, 17);

                if (muteRect.Contains(pt))
                {
                    track.IsMuted = !track.IsMuted;
                    TimelineMutated?.Invoke();
                    InvalidateVisual();
                    return;
                }
                if (soloRect.Contains(pt))
                {
                    track.IsSolo = !track.IsSolo;
                    TimelineMutated?.Invoke();
                    InvalidateVisual();
                    return;
                }
                if (lockRect.Contains(pt))
                {
                    track.IsLocked = !track.IsLocked;
                    InvalidateVisual();
                    return;
                }
                if (track.Type != TrackType.Audio && visRect.Contains(pt))
                {
                    track.IsVisible = !track.IsVisible;
                    TimelineMutated?.Invoke();
                    InvalidateVisual();
                    return;
                }
            }
            return;
        }

        // 2. Hand Tool (H) pans timeline horizontally
        if (ActiveTool == NleTimelineTool.Hand)
        {
            _dragMode = DragInteraction.PanTimeline;
            _dragStartPoint = pt;
            _dragOrigScrollSeconds = ScrollOffsetSeconds;
            e.Pointer.Capture(this);
            return;
        }

        // 3. Text Tool (T) inserts a C1 subtitle clip at clicked time
        if (ActiveTool == NleTimelineTool.Text && pt.X >= HeaderWidth && _timelineService != null)
        {
            double clickTime = XToTime(pt.X);
            var created = _timelineService.AddTextOverlay(_document, "NEW CAPTION TITLE", clickTime, 3.0, "BroadcastBox");
            SelectClip(created);
            TimelineMutated?.Invoke();
            InvalidateVisual();
            return;
        }

        // 4. Timecode Ruler click -> Scrub Playhead
        if (pt.Y <= RulerHeight && pt.X >= HeaderWidth)
        {
            _dragMode = DragInteraction.ScrubPlayhead;
            PlayheadTime = XToTime(pt.X);
            e.Pointer.Capture(this);
            PlayheadScrubbed?.Invoke(PlayheadTime);
            InvalidateVisual();
            return;
        }

        // 5. Hit-test Clips
        int hitTrackIdx = (int)((pt.Y - RulerHeight) / (TrackHeight + TrackGap));
        if (hitTrackIdx >= 0 && hitTrackIdx < _document.Tracks.Count)
        {
            var track = _document.Tracks[hitTrackIdx];
            double clickTime = XToTime(pt.X);
            var hitClip = FindClipAtTime(track, clickTime);

            if (hitClip != null)
            {
                SelectClip(hitClip);

                // Right-click on a clip opens the Section 3.1 ClipContextMenu
                if (pointProps.IsRightButtonPressed)
                {
                    ShowClipContextMenu(track, hitClip, clickTime);
                    e.Handled = true;
                    return;
                }

                if (ActiveTool == NleTimelineTool.Razor && !track.IsLocked && _timelineService != null && _document != null && !hitClip.IsTransitionClip)
                {
                    double splitTime = SnapTimeIfEnabled(clickTime, hitClip);
                    _timelineService.CutClipAt(_document, hitClip.Id, splitTime);
                    TimelineMutated?.Invoke();
                    InvalidateVisual();
                    return;
                }

                if (track.IsLocked) return;

                _activeClip = hitClip;
                _activeClipBeforeSnapshot = hitClip.Clone();
                _activeClipSourceTrack = track;
                _linkedAudioClip = FindLinkedCompanionClip(track, hitClip);
                _dragStartPoint = pt;
                _dragOrigStart = hitClip.TimelineStart;
                _dragOrigDuration = hitClip.Duration;
                _dragOrigSourceIn = hitClip.SourceIn;
                _dragOrigSourceOut = hitClip.SourceOut;

                e.Pointer.Capture(this);

                if (ActiveTool == NleTimelineTool.Slip && !hitClip.IsTransitionClip)
                {
                    _dragMode = DragInteraction.SlipClipContent;
                    return;
                }

                double clipLeftX = TimeToX(hitClip.TimelineStart);
                double clipRightX = TimeToX(hitClip.TimelineStart + hitClip.Duration);

                // Check transition trim handles on regular clips
                if (!hitClip.IsTransitionClip && hitClip.InTransition != null && hitClip.InTransition.Duration > 0.04)
                {
                    double transInW = Math.Min((clipRightX - clipLeftX) * 0.48, hitClip.InTransition.Duration * PixelsPerSecond);
                    double handleX = clipLeftX + transInW;
                    if (Math.Abs(pt.X - handleX) <= 6.0)
                    {
                        _dragMode = DragInteraction.TrimTransitionIn;
                        _dragOrigTransitionDur = hitClip.InTransition.Duration;
                        return;
                    }
                }

                if (!hitClip.IsTransitionClip && hitClip.OutTransition != null && hitClip.OutTransition.Duration > 0.04)
                {
                    double transOutW = Math.Min((clipRightX - clipLeftX) * 0.48, hitClip.OutTransition.Duration * PixelsPerSecond);
                    double handleX = clipRightX - transOutW;
                    if (Math.Abs(pt.X - handleX) <= 6.0)
                    {
                        _dragMode = DragInteraction.TrimTransitionOut;
                        _dragOrigTransitionDur = hitClip.OutTransition.Duration;
                        return;
                    }
                }

                if (Math.Abs(pt.X - clipLeftX) <= TrimHandleWidth)
                    _dragMode = DragInteraction.TrimClipLeft;
                else if (Math.Abs(pt.X - clipRightX) <= TrimHandleWidth)
                    _dragMode = DragInteraction.TrimClipRight;
                else
                    _dragMode = DragInteraction.MoveClip;

                return;
            }
            else if (pointProps.IsRightButtonPressed && pt.X >= HeaderWidth)
            {
                // Right-click on empty track space opens the Section 3.2 EmptyTrackContextMenu
                ShowEmptySpaceContextMenu(track, clickTime);
                e.Handled = true;
                return;
            }
        }
        else if (pointProps.IsRightButtonPressed && pt.X >= HeaderWidth)
        {
            ShowEmptySpaceContextMenu(null, XToTime(pt.X));
            e.Handled = true;
            return;
        }

        SelectClip(null);
        _dragMode = DragInteraction.ScrubPlayhead;
        PlayheadTime = XToTime(pt.X);
        e.Pointer.Capture(this);
        PlayheadScrubbed?.Invoke(PlayheadTime);
        InvalidateVisual();
    }

    private void ShowClipContextMenu(TimelineTrack track, TimelineClip clip, double clickTime)
    {
        var menu = new ContextMenu();

        MenuItem AddAction(string header, string actionCode)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => ContextMenuActionRequested?.Invoke(actionCode, clickTime);
            menu.Items.Add(item);
            return item;
        }

        AddAction("Cut (Ctrl+X)", "Cut");
        AddAction("Copy (Ctrl+C)", "Copy");
        AddAction("Paste (Ctrl+V)", "Paste");
        AddAction("Paste Attributes (Ctrl+Alt+V)", "PasteAttributes");
        AddAction("Duplicate Clip (Ctrl+D)", "Duplicate");
        menu.Items.Add(new Separator());

        AddAction("Split at Playhead (Ctrl+B)", "SplitAtPlayhead");
        AddAction("Delete (Leave Gap) (Del)", "LiftDelete");
        AddAction("Ripple Delete (Close Gap) (Shift+Del)", "RippleDelete");
        menu.Items.Add(new Separator());

        AddAction("Speed / Duration... (Ctrl+R)", "SpeedDurationModal");
        AddAction("Add Freeze Frame (3s)", "FreezeFrame");
        AddAction("Detach Audio to Audio Track", "DetachAudio");
        AddAction(_document?.LinkVideoAndAudio == true ? "✓ Link Video & Audio" : "Link Video & Audio", "ToggleLinkAudioVideo");
        menu.Items.Add(new Separator());

        var transSub = new MenuItem { Header = "Add Transition ▸" };
        void AddTransOption(string label, string code)
        {
            var sub = new MenuItem { Header = label };
            sub.Click += (_, _) => ContextMenuActionRequested?.Invoke(code, clickTime);
            transSub.Items.Add(sub);
        }
        AddTransOption("Cross Dissolve (0.5s)", "Trans:CrossDissolve");
        AddTransOption("Dip to Black (0.5s)", "Trans:DipToBlack");
        AddTransOption("Dip to White (0.5s)", "Trans:DipToWhite");
        AddTransOption("Wipe (0.5s)", "Trans:Wipe");
        AddTransOption("Slide (0.5s)", "Trans:Slide");
        AddTransOption("Zoom In Blur (0.5s)", "Trans:ZoomBlur");
        AddTransOption("Flash Cut (0.3s)", "Trans:FlashCut");
        transSub.Items.Add(new Separator());
        AddTransOption("Remove Transitions", "Trans:None");
        menu.Items.Add(transSub);

        AddAction("+ Standalone Transition Block (Ctrl+D)", "AddStandaloneTransitionBlock");
        menu.Items.Add(new Separator());

        AddAction("Clear All Keyframes on Clip", "ClearKeyframes");
        AddAction("Reset All Effects & Color", "ResetAllEffectsAndColor");
        AddAction("Reveal File in Explorer", "RevealInExplorer");

        ContextMenu = menu;
        menu.Open(this);
    }

    private void ShowEmptySpaceContextMenu(TimelineTrack? track, double clickTime)
    {
        var menu = new ContextMenu();

        void AddAction(string header, string actionCode)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => ContextMenuActionRequested?.Invoke(actionCode, clickTime);
            menu.Items.Add(item);
        }

        AddAction("Paste Clip Here (Ctrl+V)", "PasteAtCursor");
        AddAction("Close Gap (Ripple Delete Blank Space)", "CloseGapHere");
        AddAction("Insert Text / Title Here", "InsertTextHere");
        AddAction("Insert Transition Block Here", "AddStandaloneTransitionBlockHere");
        menu.Items.Add(new Separator());
        AddAction("Add Video Track", "AddVideoTrack");
        AddAction("Add Audio Track", "AddAudioTrack");
        menu.Items.Add(new Separator());
        AddAction("Fit Timeline to View (\\)", "FitTimeline");

        ContextMenu = menu;
        menu.Open(this);
    }

    private void ShowTrackHeaderContextMenu(TimelineTrack track)
    {
        var menu = new ContextMenu();
        void AddAction(string header, string actionCode)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => ContextMenuActionRequested?.Invoke(actionCode, PlayheadTime);
            menu.Items.Add(item);
        }

        AddAction("Add Video Track Above", "AddVideoTrack");
        AddAction("Add Audio Track Below", "AddAudioTrack");
        menu.Items.Add(new Separator());
        AddAction($"Delete Track ({track.Name})", $"DeleteTrack:{track.Id}");

        ContextMenu = menu;
        menu.Open(this);
    }

    private TimelineClip? FindLinkedCompanionClip(TimelineTrack sourceTrack, TimelineClip clip)
    {
        if (_document == null || !_document.LinkVideoAndAudio)
            return null;

        if (clip.LinkedClipId.HasValue)
        {
            foreach (var track in _document.Tracks.Where(t => !t.IsLocked))
            {
                var explicitMatch = track.Clips.FirstOrDefault(c => c.Id == clip.LinkedClipId.Value);
                if (explicitMatch != null)
                    return explicitMatch;
            }
        }

        if (clip.MediaAssetId == Guid.Empty)
            return null;

        TrackType companionType = sourceTrack.Type == TrackType.Video ? TrackType.Audio : TrackType.Video;
        foreach (var track in _document.Tracks.Where(t => t.Type == companionType && !t.IsLocked))
        {
            var match = track.Clips.FirstOrDefault(c =>
                c.MediaAssetId == clip.MediaAssetId &&
                Math.Abs(c.TimelineStart - clip.TimelineStart) < 0.08 &&
                Math.Abs(c.Duration - clip.Duration) < 0.08);
            if (match != null)
                return match;
        }
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_document == null) return;

        var pt = e.GetPosition(this);

        if (_dragMode == DragInteraction.None)
        {
            if (ActiveTool == NleTimelineTool.Razor)
            {
                _razorHoverTime = pt.X >= HeaderWidth ? XToTime(pt.X) : null;
                InvalidateVisual();
                return;
            }

            if (ActiveTool == NleTimelineTool.Selection && pt.X >= HeaderWidth && pt.Y > RulerHeight)
            {
                int hoverTrackIdx = (int)((pt.Y - RulerHeight) / (TrackHeight + TrackGap));
                if (hoverTrackIdx >= 0 && hoverTrackIdx < _document.Tracks.Count)
                {
                    var track = _document.Tracks[hoverTrackIdx];
                    double hoverTime = XToTime(pt.X);
                    var hoverClip = FindClipAtTime(track, hoverTime);
                    if (hoverClip != null)
                    {
                        double lx = TimeToX(hoverClip.TimelineStart);
                        double rx = TimeToX(hoverClip.TimelineStart + hoverClip.Duration);

                        bool isTransHandle = false;
                        if (!hoverClip.IsTransitionClip && hoverClip.InTransition != null && hoverClip.InTransition.Duration > 0.04)
                        {
                            double transInW = Math.Min((rx - lx) * 0.48, hoverClip.InTransition.Duration * PixelsPerSecond);
                            if (Math.Abs(pt.X - (lx + transInW)) <= 6.0) isTransHandle = true;
                        }
                        if (!hoverClip.IsTransitionClip && hoverClip.OutTransition != null && hoverClip.OutTransition.Duration > 0.04)
                        {
                            double transOutW = Math.Min((rx - lx) * 0.48, hoverClip.OutTransition.Duration * PixelsPerSecond);
                            if (Math.Abs(pt.X - (rx - transOutW)) <= 6.0) isTransHandle = true;
                        }

                        if (Math.Abs(pt.X - lx) <= TrimHandleWidth || Math.Abs(pt.X - rx) <= TrimHandleWidth || isTransHandle)
                        {
                            Cursor = new Cursor(StandardCursorType.SizeWestEast);
                            return;
                        }
                    }
                }
                Cursor = Cursor.Default;
            }
            return;
        }

        if (_dragMode == DragInteraction.PanTimeline)
        {
            double deltaSec = (pt.X - _dragStartPoint.X) / Math.Max(2.0, PixelsPerSecond);
            SetScrollOffsetSeconds(_dragOrigScrollSeconds - deltaSec);
            return;
        }

        if (_dragMode == DragInteraction.ScrubPlayhead)
        {
            PlayheadTime = XToTime(pt.X);
            PlayheadScrubbed?.Invoke(PlayheadTime);
            InvalidateVisual();
            return;
        }

        if (_activeClip == null) return;
        double deltaSeconds = (pt.X - _dragStartPoint.X) / PixelsPerSecond;

        if (_dragMode == DragInteraction.MoveClip)
        {
            double candidateStart = Math.Max(0, _dragOrigStart + deltaSeconds);
            candidateStart = SnapTimeIfEnabled(candidateStart, _activeClip);

            double rounded = Math.Round(candidateStart, 3);
            _activeClip.TimelineStart = rounded;
            if (_linkedAudioClip != null)
                _linkedAudioClip.TimelineStart = rounded;

            // Cross-track vertical dragging (e.g., V1 <-> V2 or A1 <-> A2)
            int targetTrackIdx = (int)((pt.Y - RulerHeight) / (TrackHeight + TrackGap));
            if (_activeClipSourceTrack != null &&
                targetTrackIdx >= 0 &&
                targetTrackIdx < _document.Tracks.Count)
            {
                var targetTrack = _document.Tracks[targetTrackIdx];
                if (targetTrack != _activeClipSourceTrack &&
                    !targetTrack.IsLocked &&
                    targetTrack.Type == _activeClipSourceTrack.Type)
                {
                    _activeClipSourceTrack.Clips.Remove(_activeClip);
                    targetTrack.Clips.Add(_activeClip);
                    _activeClipSourceTrack = targetTrack;
                }
            }

            ClampScrollAndNotify();
            TimelineMutated?.Invoke();
            InvalidateVisual();
        }
        else if (_dragMode == DragInteraction.TrimClipLeft)
        {
            double maxDelta = _dragOrigDuration - 0.15;
            double clampedDelta = Math.Clamp(deltaSeconds, -_dragOrigStart, maxDelta);
            double newStart = Math.Round(_dragOrigStart + clampedDelta, 3);
            double newDur = Math.Round(_dragOrigDuration - clampedDelta, 3);
            double newIn = Math.Max(0, Math.Round(_dragOrigSourceIn + clampedDelta * _activeClip.Speed, 3));

            _activeClip.TimelineStart = newStart;
            _activeClip.Duration = newDur;
            _activeClip.SourceIn = newIn;

            if (_linkedAudioClip != null)
            {
                _linkedAudioClip.TimelineStart = newStart;
                _linkedAudioClip.Duration = newDur;
                _linkedAudioClip.SourceIn = newIn;
            }

            TimelineMutated?.Invoke();
            InvalidateVisual();
        }
        else if (_dragMode == DragInteraction.TrimClipRight)
        {
            double newDuration = Math.Max(0.15, _dragOrigDuration + deltaSeconds);
            double roundedDur = Math.Round(newDuration, 3);
            double roundedOut = Math.Round(_activeClip.SourceIn + roundedDur * _activeClip.Speed, 3);

            _activeClip.Duration = roundedDur;
            _activeClip.SourceOut = roundedOut;

            if (_linkedAudioClip != null)
            {
                _linkedAudioClip.Duration = roundedDur;
                _linkedAudioClip.SourceOut = roundedOut;
            }

            ClampScrollAndNotify();
            TimelineMutated?.Invoke();
            InvalidateVisual();
        }
        else if (_dragMode == DragInteraction.SlipClipContent)
        {
            double newSourceIn = Math.Max(0, _dragOrigSourceIn - deltaSeconds);
            _activeClip.SourceIn = Math.Round(newSourceIn, 3);
            _activeClip.SourceOut = Math.Round(newSourceIn + _activeClip.Duration * _activeClip.Speed, 3);
            TimelineMutated?.Invoke();
            InvalidateVisual();
        }
        else if (_dragMode == DragInteraction.TrimTransitionIn)
        {
            if (_activeClip.InTransition != null)
            {
                double maxDur = Math.Max(0.1, _activeClip.Duration * 0.48);
                double newDur = Math.Clamp(_dragOrigTransitionDur + deltaSeconds, 0.05, maxDur);
                double rounded = Math.Round(newDur, 3);
                _activeClip.InTransition.Duration = rounded;
                _activeClip.FadeInDuration = rounded;
                ClipSelected?.Invoke(_activeClip);
                TimelineMutated?.Invoke();
                InvalidateVisual();
            }
        }
        else if (_dragMode == DragInteraction.TrimTransitionOut)
        {
            if (_activeClip.OutTransition != null)
            {
                double maxDur = Math.Max(0.1, _activeClip.Duration * 0.48);
                double newDur = Math.Clamp(_dragOrigTransitionDur - deltaSeconds, 0.05, maxDur);
                double rounded = Math.Round(newDur, 3);
                _activeClip.OutTransition.Duration = rounded;
                _activeClip.FadeOutDuration = rounded;
                ClipSelected?.Invoke(_activeClip);
                TimelineMutated?.Invoke();
                InvalidateVisual();
            }
        }
    }

    private static TimelineClip? FindClipAtTime(TimelineTrack track, double timeSec)
    {
        TimelineClip? fallback = null;
        for (int i = track.Clips.Count - 1; i >= 0; i--)
        {
            var clip = track.Clips[i];
            if (timeSec >= clip.TimelineStart && timeSec <= clip.TimelineStart + clip.Duration)
            {
                if (clip.IsTransitionClip)
                    return clip;
                fallback ??= clip;
            }
        }
        return fallback;
    }

    private double SnapTimeIfEnabled(double candidateTime, TimelineClip ignoreClip)
    {
        _activeSnapLineTime = null;
        if (!SnappingEnabled || _document == null)
            return candidateTime;

        double thresholdSec = 8.0 / PixelsPerSecond;
        double bestDist = double.MaxValue;
        double bestSnappedTime = candidateTime;
        double? bestSnapLine = null;

        void TestSnap(double target)
        {
            double dist1 = Math.Abs(candidateTime - target);
            if (dist1 <= thresholdSec && dist1 < bestDist)
            {
                bestDist = dist1;
                bestSnappedTime = target;
                bestSnapLine = target;
            }
            double dist2 = Math.Abs((candidateTime + ignoreClip.Duration) - target);
            if (dist2 <= thresholdSec && dist2 < bestDist)
            {
                bestDist = dist2;
                bestSnappedTime = Math.Max(0, target - ignoreClip.Duration);
                bestSnapLine = target;
            }
        }

        TestSnap(0.0);
        TestSnap(PlayheadTime);

        for (int t = 0; t < _document.Tracks.Count; t++)
        {
            var track = _document.Tracks[t];
            for (int c = 0; c < track.Clips.Count; c++)
            {
                var clip = track.Clips[c];
                if (clip.Id == ignoreClip.Id || clip.Id == _linkedAudioClip?.Id) continue;
                TestSnap(clip.TimelineStart);
                TestSnap(clip.TimelineStart + clip.Duration);
            }
        }

        _activeSnapLineTime = bestSnapLine;
        return bestSnappedTime;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _activeSnapLineTime = null;
        e.Pointer.Capture(null);

        if (_activeClip != null && _undoRedo != null)
        {
            if ((_dragMode == DragInteraction.TrimTransitionIn || _dragMode == DragInteraction.TrimTransitionOut)
                && _activeClipBeforeSnapshot != null)
            {
                _undoRedo.RecordOrCoalesceClipEdit(
                    _activeClip,
                    "TrimTransition",
                    "Trim Transition Duration",
                    _activeClipBeforeSnapshot,
                    _activeClip.Clone(),
                    c => ClipSelected?.Invoke(c));
                ClipSelected?.Invoke(_activeClip);
            }
            else if (_dragMode == DragInteraction.MoveClip && Math.Abs(_activeClip.TimelineStart - _dragOrigStart) > 0.01)
            {
                double finalStart = _activeClip.TimelineStart;
                _activeClip.TimelineStart = _dragOrigStart;
                _undoRedo.ExecuteCommand(new MoveClipCommand(_activeClip, _dragOrigStart, finalStart));
            }
            else if ((_dragMode == DragInteraction.TrimClipLeft || _dragMode == DragInteraction.TrimClipRight || _dragMode == DragInteraction.SlipClipContent)
                     && (Math.Abs(_activeClip.Duration - _dragOrigDuration) > 0.01 || Math.Abs(_activeClip.TimelineStart - _dragOrigStart) > 0.01 || Math.Abs(_activeClip.SourceIn - _dragOrigSourceIn) > 0.01))
            {
                double fStart = _activeClip.TimelineStart;
                double fDur = _activeClip.Duration;
                double fIn = _activeClip.SourceIn;
                double fOut = _activeClip.SourceOut;

                _undoRedo.ExecuteCommand(new TrimClipCommand(
                    _activeClip,
                    _dragOrigStart, _dragOrigDuration, _dragOrigSourceIn, _dragOrigSourceOut,
                    fStart, fDur, fIn, fOut));
            }
        }

        _dragMode = DragInteraction.None;
        _activeClip = null;
        _activeClipBeforeSnapshot = null;
        _linkedAudioClip = null;
        _activeClipSourceTrack = null;
        SetActiveTool(ActiveTool);
        ClampScrollAndNotify();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pt = e.GetPosition(this);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Cursor-centered zoom (Ctrl + MouseWheel)
            double mouseTimeBefore = XToTime(pt.X);
            double factor = e.Delta.Y > 0 ? 1.18 : (1.0 / 1.18);
            double newPps = Math.Clamp(PixelsPerSecond * factor, 2.0, 600.0);
            PixelsPerSecond = newPps;

            if (pt.X > HeaderWidth)
            {
                double newScroll = mouseTimeBefore - ((pt.X - HeaderWidth) / PixelsPerSecond);
                ScrollOffsetSeconds = Math.Clamp(newScroll, 0.0, GetMaxScrollSeconds());
            }

            ClampScrollAndNotify();
            ZoomChanged?.Invoke(PixelsPerSecond);
            InvalidateVisual();
        }
        else
        {
            // Horizontal Scroll (MouseWheel or Shift+MouseWheel)
            double deltaWheel = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
            double scrollStepSec = Math.Max(0.5, 48.0 / Math.Max(4.0, PixelsPerSecond));
            SetScrollOffsetSeconds(ScrollOffsetSeconds - deltaWheel * scrollStepSec);
        }
        e.Handled = true;
    }
}
