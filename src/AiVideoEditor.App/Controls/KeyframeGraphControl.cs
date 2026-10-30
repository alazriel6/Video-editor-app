using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AiVideoEditor.Core.Models;

namespace AiVideoEditor.App.Controls;

/// <summary>
/// Premiere Pro / DaVinci Resolve style Dual-Pane Keyframe Mini-Timeline inside the Inspector.
/// Includes a Top Mini Time Ruler (seconds/minutes + Sequence Timecode), a synchronized Mini Red Playhead,
/// interactive gold keyframe diamonds (◆), live drag tooltip, right-click easing context menu,
/// and full Undo/Redo commit events.
/// </summary>
public sealed class KeyframeGraphControl : Control
{
    private const double LabelColWidth = 64.0;
    private const double RulerHeight = 20.0;

    private TimelineClip? _clip;
    private double _clipRelativePlayhead;
    private KeyframePoint? _draggingKeyframe;
    private AnimatableProperty? _draggingTrack;
    private string _draggingLabel = string.Empty;
    private TimelineClip? _dragPreEditSnapshot;

    public event Action<double>? PlayheadRequested;
    public event Action? KeyframesMutated;
    public event Action<TimelineClip, TimelineClip, string>? KeyframeCommandCommitted;

    public KeyframeGraphControl()
    {
        ClipToBounds = true;
        Height = 140;
    }

    public void SetPlayheadTime(double globalPlayheadTime)
    {
        if (_clip != null)
        {
            double newRel = Math.Clamp(globalPlayheadTime - _clip.TimelineStart, 0, Math.Max(0.05, _clip.Duration));
            if (Math.Abs(_clipRelativePlayhead - newRel) > 0.001)
            {
                _clipRelativePlayhead = newRel;
                InvalidateVisual();
            }
        }
    }

    public void BindClip(TimelineClip? clip, double globalPlayheadTime)
    {
        if (_clip == clip && clip != null)
        {
            SetPlayheadTime(globalPlayheadTime);
            return;
        }

        _clip = clip;
        _cachedTracks = null;
        if (clip != null)
        {
            _clipRelativePlayhead = Math.Clamp(globalPlayheadTime - clip.TimelineStart, 0, Math.Max(0.05, clip.Duration));
        }
        else
        {
            _clipRelativePlayhead = 0;
        }
        InvalidateVisual();
    }

    private (string Label, AnimatableProperty Track, string Unit)[]? _cachedTracks;

    private (string Label, AnimatableProperty Track, string Unit)[] GetTracks()
    {
        if (_clip == null) return Array.Empty<(string, AnimatableProperty, string)>();
        if (_cachedTracks != null) return _cachedTracks;

        var list = new List<(string, AnimatableProperty, string)>(10)
        {
            ("Scale", _clip.ScaleTrack, "%"),
            ("Pos X", _clip.PosXTrack, "px"),
            ("Pos Y", _clip.PosYTrack, "px"),
            ("Rotation", _clip.RotationTrack, "°"),
            ("Opacity", _clip.OpacityTrack, "%"),
            ("Volume", _clip.VolumeTrack, "dB")
        };

        if (_clip.CropLeftTrack.Keyframes.Count > 0 || _clip.CropRightTrack.Keyframes.Count > 0 ||
            _clip.CropTopTrack.Keyframes.Count > 0 || _clip.CropBottomTrack.Keyframes.Count > 0)
        {
            list.Add(("Crop L", _clip.CropLeftTrack, "%"));
            list.Add(("Crop R", _clip.CropRightTrack, "%"));
            list.Add(("Crop T", _clip.CropTopTrack, "%"));
            list.Add(("Crop B", _clip.CropBottomTrack, "%"));
        }

        _cachedTracks = list.ToArray();
        return _cachedTracks;
    }

    private double TimeToX(double relTime, double width)
    {
        double dur = Math.Max(0.1, _clip?.Duration ?? 5.0);
        double usable = Math.Max(10, width - LabelColWidth - 10);
        return LabelColWidth + Math.Clamp(relTime / dur, 0.0, 1.0) * usable;
    }

    private double XToTime(double x, double width)
    {
        double dur = Math.Max(0.1, _clip?.Duration ?? 5.0);
        double usable = Math.Max(10, width - LabelColWidth - 10);
        return Math.Clamp((x - LabelColWidth) / usable, 0.0, 1.0) * dur;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width <= 10 || bounds.Height <= 10) return;

        // #111215 Canvas & #2D303B Border
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(17, 18, 21)), new Rect(0, 0, bounds.Width, bounds.Height), 3f);
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(45, 48, 59)), 1), new Rect(0, 0, bounds.Width, bounds.Height), 3f);

        if (_clip == null)
        {
            var emptyText = new FormattedText(
                "Select a clip on the timeline to animate keyframes (◆)",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter"),
                11,
                new SolidColorBrush(Color.FromRgb(107, 114, 128)));
            context.DrawText(emptyText, new Point(12, bounds.Height * 0.5 - 7));
            return;
        }

        // 1. Top Mini Time Ruler (#22242C)
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(34, 36, 44)), new Rect(0, 0, bounds.Width, RulerHeight));
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(45, 48, 59)), 1);
        context.DrawLine(borderPen, new Point(0, RulerHeight), new Point(bounds.Width, RulerHeight));
        context.DrawLine(borderPen, new Point(LabelColWidth, 0), new Point(LabelColWidth, bounds.Height));

        // Top-left local clip time readout
        string clipTimeLabel = $"{_clipRelativePlayhead:00.00}s";
        var tcFt = new FormattedText(
            clipTimeLabel,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono, Consolas, Monospace", FontStyle.Normal, FontWeight.Bold),
            9.5,
            new SolidColorBrush(Color.FromRgb(239, 68, 68)));
        context.DrawText(tcFt, new Point(6, 4));

        // Ruler Second/Minute Ticks
        double dur = Math.Max(0.1, _clip.Duration);
        double stepSec = dur <= 3.0 ? 0.5
            : dur <= 8.0 ? 1.0
            : dur <= 20.0 ? 2.0
            : dur <= 60.0 ? 5.0
            : 15.0;

        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(107, 114, 128)), 1);
        var subTickPen = new Pen(new SolidColorBrush(Color.FromRgb(65, 70, 82)), 1);
        var tickLabelBrush = new SolidColorBrush(Color.FromRgb(156, 163, 175));

        for (double t = 0; t <= dur + 0.001; t += stepSec)
        {
            double tx = TimeToX(t, bounds.Width);
            context.DrawLine(tickPen, new Point(tx, RulerHeight - 6), new Point(tx, RulerHeight));

            string tickStr = t >= 60.0
                ? $"{(int)(t / 60):00}:{t % 60:00.0}s"
                : $"{t:0.0}s";
            var tickText = new FormattedText(
                tickStr,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Mono, Consolas, Monospace"),
                8.5,
                tickLabelBrush);
            if (tx + tickText.Width < bounds.Width - 2)
            {
                context.DrawText(tickText, new Point(tx + 2, 2));
            }

            double halfT = t + stepSec * 0.5;
            if (halfT < dur)
            {
                double hx = TimeToX(halfT, bounds.Width);
                context.DrawLine(subTickPen, new Point(hx, RulerHeight - 3), new Point(hx, RulerHeight));
            }
        }

        // 2. Keyframe Property Rows
        var tracks = GetTracks();
        double rowsAreaH = Math.Max(20, bounds.Height - RulerHeight);
        double rowH = rowsAreaH / tracks.Length;
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 32, 40)), 1);
        var goldBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // #F59E0B
        var activeGoldBrush = new SolidColorBrush(Color.FromRgb(253, 224, 71));
        var curvePen = new Pen(new SolidColorBrush(Color.FromArgb(155, 245, 158, 11)), 1.3);

        for (int i = 0; i < tracks.Length; i++)
        {
            double rowTop = RulerHeight + i * rowH;
            double yCenter = rowTop + rowH * 0.5;
            context.DrawLine(gridPen, new Point(0, rowTop + rowH), new Point(bounds.Width, rowTop + rowH));

            var lbl = new FormattedText(
                tracks[i].Label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.Medium),
                9.5,
                new SolidColorBrush(Color.FromRgb(156, 163, 175)));
            context.DrawText(lbl, new Point(6, yCenter - 6));

            var kfs = tracks[i].Track.Keyframes;
            for (int k = 0; k < kfs.Count; k++)
            {
                double kx = TimeToX(kfs[k].TimeOffsetSeconds, bounds.Width);
                if (k + 1 < kfs.Count)
                {
                    double nextX = TimeToX(kfs[k + 1].TimeOffsetSeconds, bounds.Width);
                    context.DrawLine(curvePen, new Point(kx, yCenter), new Point(nextX, yCenter));
                }
                bool isOnPlayhead = Math.Abs(kfs[k].TimeOffsetSeconds - _clipRelativePlayhead) < 0.04;
                DrawDiamond(context, kx, yCenter, isOnPlayhead ? 5.2 : 4.5, isOnPlayhead ? activeGoldBrush : goldBrush);
            }
        }

        // 3. Synchronized Mini Red Playhead (#EF4444)
        double px = TimeToX(_clipRelativePlayhead, bounds.Width);
        var playheadBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        var playheadPen = new Pen(playheadBrush, 1.3);
        context.DrawLine(playheadPen, new Point(px, 0), new Point(px, bounds.Height));

        var headGeo = new StreamGeometry();
        using (var g = headGeo.Open())
        {
            g.BeginFigure(new Point(px - 4.5, 0), true);
            g.LineTo(new Point(px + 4.5, 0));
            g.LineTo(new Point(px, 7));
            g.EndFigure(true);
        }
        context.DrawGeometry(playheadBrush, null, headGeo);

        // 4. Live Drag Tooltip when moving a keyframe diamond
        if (_draggingKeyframe != null)
        {
            double dx = TimeToX(_draggingKeyframe.TimeOffsetSeconds, bounds.Width);
            string valStr = _draggingLabel == "Scale" || _draggingLabel == "Opacity"
                ? $"{_draggingKeyframe.Value * 100f:0}%"
                : $"{_draggingKeyframe.Value:0.##}";
            string tip = $"Time: {_draggingKeyframe.TimeOffsetSeconds:00.00}s | {_draggingLabel}: {valStr} ({_draggingKeyframe.Easing})";

            var tipText = new FormattedText(
                tip,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Mono, Consolas, Monospace", FontStyle.Normal, FontWeight.SemiBold),
                9.5,
                new SolidColorBrush(Color.FromRgb(243, 244, 246)));

            double boxW = tipText.Width + 12;
            double boxX = Math.Clamp(dx - boxW * 0.5, LabelColWidth + 2, Math.Max(LabelColWidth + 4, bounds.Width - boxW - 2));
            var tipRect = new Rect(boxX, 2, boxW, 16);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(235, 24, 25, 30)), tipRect, 3f);
            context.DrawRectangle(new Pen(goldBrush, 1), tipRect, 3f);
            context.DrawText(tipText, new Point(boxX + 6, 4));
        }
    }

    private static void DrawDiamond(DrawingContext context, double cx, double cy, double r, IBrush fill)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(cx, cy - r), true);
            ctx.LineTo(new Point(cx + r, cy));
            ctx.LineTo(new Point(cx, cy + r));
            ctx.LineTo(new Point(cx - r, cy));
            ctx.EndFigure(true);
        }
        context.DrawGeometry(fill, new Pen(Brushes.Black, 0.8), geo);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_clip == null) return;

        var pt = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        // Clicking on the Top Mini Time Ruler scrubs playhead directly
        if (pt.Y <= RulerHeight)
        {
            double rulerRel = XToTime(pt.X, Bounds.Width);
            _clipRelativePlayhead = rulerRel;
            PlayheadRequested?.Invoke(_clip.TimelineStart + rulerRel);
            InvalidateVisual();
            return;
        }

        var tracks = GetTracks();
        double rowsAreaH = Math.Max(20, Bounds.Height - RulerHeight);
        double rowH = rowsAreaH / Math.Max(1, tracks.Length);
        int rowIdx = Math.Clamp((int)((pt.Y - RulerHeight) / rowH), 0, tracks.Length - 1);
        var trackInfo = tracks[rowIdx];
        var track = trackInfo.Track;

        // Hit-test existing keyframe diamond
        KeyframePoint? hitKf = null;
        foreach (var kf in track.Keyframes)
        {
            double kx = TimeToX(kf.TimeOffsetSeconds, Bounds.Width);
            if (Math.Abs(pt.X - kx) <= 7.5)
            {
                hitKf = kf;
                break;
            }
        }

        if (props.IsRightButtonPressed && hitKf != null)
        {
            ShowKeyframeContextMenu(track, hitKf);
            e.Handled = true;
            return;
        }

        if (hitKf != null)
        {
            _dragPreEditSnapshot = _clip.Clone();
            _draggingKeyframe = hitKf;
            _draggingTrack = track;
            _draggingLabel = trackInfo.Label;
            e.Pointer.Capture(this);
            PlayheadRequested?.Invoke(_clip.TimelineStart + hitKf.TimeOffsetSeconds);
            InvalidateVisual();
            return;
        }

        double clickedRel = XToTime(pt.X, Bounds.Width);
        _clipRelativePlayhead = clickedRel;
        PlayheadRequested?.Invoke(_clip.TimelineStart + clickedRel);
        InvalidateVisual();
    }

    private void ShowKeyframeContextMenu(AnimatableProperty track, KeyframePoint kf)
    {
        if (_clip == null) return;
        var menu = new ContextMenu();

        MenuItem MakeEasingItem(string title, EasingType easing)
        {
            var item = new MenuItem
            {
                Header = kf.Easing == easing ? $"✓ {title}" : title
            };
            item.Click += (_, _) =>
            {
                if (_clip == null) return;
                var before = _clip.Clone();
                kf.Easing = easing;
                var after = _clip.Clone();
                KeyframesMutated?.Invoke();
                KeyframeCommandCommitted?.Invoke(before, after, $"Change Keyframe Easing ({easing})");
                InvalidateVisual();
            };
            return item;
        }

        menu.Items.Add(MakeEasingItem("Linear", EasingType.Linear));
        menu.Items.Add(MakeEasingItem("Smooth (Ease In/Out)", EasingType.Smooth));
        menu.Items.Add(MakeEasingItem("Ease In", EasingType.EaseIn));
        menu.Items.Add(MakeEasingItem("Ease Out", EasingType.EaseOut));
        menu.Items.Add(MakeEasingItem("Hold (Step)", EasingType.Hold));
        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete Keyframe (Del)" };
        deleteItem.Click += (_, _) =>
        {
            if (_clip == null) return;
            var before = _clip.Clone();
            _cachedTracks = null;
            track.Keyframes.Remove(kf);
            if (track.Keyframes.Count == 0)
                track.IsAnimated = false;
            var after = _clip.Clone();
            KeyframesMutated?.Invoke();
            KeyframeCommandCommitted?.Invoke(before, after, "Delete Keyframe");
            InvalidateVisual();
        };
        menu.Items.Add(deleteItem);

        ContextMenu = menu;
        menu.Open(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_clip == null || _draggingKeyframe == null || _draggingTrack == null) return;

        var pt = e.GetPosition(this);
        double newRel = Math.Round(XToTime(pt.X, Bounds.Width), 3);
        _draggingKeyframe.TimeOffsetSeconds = Math.Clamp(newRel, 0, _clip.Duration);
        _draggingTrack.Keyframes.Sort((a, b) => a.TimeOffsetSeconds.CompareTo(b.TimeOffsetSeconds));
        _clipRelativePlayhead = _draggingKeyframe.TimeOffsetSeconds;
        PlayheadRequested?.Invoke(_clip.TimelineStart + _clipRelativePlayhead);
        KeyframesMutated?.Invoke();
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_draggingKeyframe != null)
        {
            _draggingKeyframe = null;
            _draggingTrack = null;
            e.Pointer.Capture(null);
            if (_clip != null && _dragPreEditSnapshot != null)
            {
                var after = _clip.Clone();
                KeyframeCommandCommitted?.Invoke(_dragPreEditSnapshot, after, $"Move {_draggingLabel} Keyframe");
            }
            _dragPreEditSnapshot = null;
            InvalidateVisual();
        }
    }
}
