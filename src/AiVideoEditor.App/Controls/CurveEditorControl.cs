using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;

namespace AiVideoEditor.App.Controls;

/// <summary>
/// DaVinci Resolve &amp; Adobe Premiere Pro Lumetri style RGB Bezier/Spline Curve Editor.
/// Features:
/// - Fritsch-Carlson Monotonic Cubic Spline interpolation (zero looping, zero overshoot).
/// - Precise Y-axis inversion (Y=0 is top 100% white, Y=height is bottom 0% black).
/// - Click empty line to immediately insert a node and drag it in one motion.
/// - Node clamping preventing interior points from swapping order.
/// - Right-click or double-click to delete interior nodes.
/// - Inactive channels rendered dimmed in background.
/// - Live HUD read-out (In: 0..255 → Out: 0..255) on hover.
/// </summary>
public class CurveEditorControl : Control
{
    private const double HeaderHeight = 24.0;
    private const double Pad = 10.0;

    protected TimelineClip? _clip;
    private int _activeChannel = 0; // 0=Master, 1=Red, 2=Green, 3=Blue
    private int _draggingPointIndex = -1;
    private bool _isDragging;
    private TimelineClip? _dragPreEditSnapshot;
    private int _hoveredPointIndex = -1;
    private Point _hoverMousePos;
    private int[]? _histogramBins;

    public event Action? CurveChanged;
    public event Action<TimelineClip, TimelineClip, string>? CurveCommitted;

    private static readonly IBrush ControlBgBrush = new SolidColorBrush(Color.Parse("#0E1015"));
    private static readonly IBrush GraphBgBrush = new SolidColorBrush(Color.Parse("#0A0B0E"));
    private static readonly IPen ControlBorderPen = new Pen(new SolidColorBrush(Color.Parse("#2B2F3C")), 1);
    private static readonly IPen GraphBorderPen = new Pen(new SolidColorBrush(Color.Parse("#22252E")), 1);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#1A1D24")), 1);
    private static readonly IPen CenterGridPen = new Pen(new SolidColorBrush(Color.Parse("#252934")), 1);
    private static readonly IPen DiagonalPen = new Pen(new SolidColorBrush(Color.Parse("#2B2F3A")), 1, DashStyle.Dash);

    private static readonly IPen MasterPen = new Pen(new SolidColorBrush(Color.FromRgb(243, 244, 246)), 2.0);
    private static readonly IPen RedPen = new Pen(new SolidColorBrush(Color.FromRgb(239, 68, 68)), 2.0);
    private static readonly IPen GreenPen = new Pen(new SolidColorBrush(Color.FromRgb(16, 185, 129)), 2.0);
    private static readonly IPen BluePen = new Pen(new SolidColorBrush(Color.FromRgb(59, 130, 246)), 2.0);

    private static readonly IPen MasterDimPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 243, 244, 246)), 1.2);
    private static readonly IPen RedDimPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 239, 68, 68)), 1.2);
    private static readonly IPen GreenDimPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 16, 185, 129)), 1.2);
    private static readonly IPen BlueDimPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 59, 130, 246)), 1.2);

    public CurveEditorControl()
    {
        Width = 240;
        Height = 240;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        ToolTip.SetTip(this, "Click line to add point & drag • Drag nodes to bend • Right-click or double-click node to delete");
    }

    public void BindClip(TimelineClip? clip)
    {
        _clip = clip;
        _draggingPointIndex = -1;
        _isDragging = false;
        _hoveredPointIndex = -1;
        InvalidateVisual();
    }

    public void SetHistogram(int[]? bins)
    {
        _histogramBins = bins;
        InvalidateVisual();
    }

    public void SetActiveChannel(int channelIndex)
    {
        _activeChannel = Math.Clamp(channelIndex, 0, 3);
        InvalidateVisual();
    }

    public List<CurveControlPoint>? GetChannelPoints(int channel)
    {
        if (_clip == null) return null;
        return channel switch
        {
            1 => _clip.RedCurvePoints,
            2 => _clip.GreenCurvePoints,
            3 => _clip.BlueCurvePoints,
            _ => _clip.MasterCurvePoints
        };
    }

    public Rect GetGraphRect()
    {
        double w = Math.Max(30, Bounds.Width - Pad * 2);
        double h = Math.Max(30, Bounds.Height - HeaderHeight - Pad * 2);
        return new Rect(Pad, HeaderHeight + Pad, w, h);
    }

    public static Point NormToScreen(Rect graph, float nx, float ny)
    {
        double px = graph.Left + Math.Clamp(nx, 0f, 1f) * graph.Width;
        double py = graph.Top + (1.0f - Math.Clamp(ny, 0f, 1f)) * graph.Height;
        return new Point(px, py);
    }

    public static (float X, float Y) ScreenToNorm(Rect graph, Point pt)
    {
        float normX = Math.Clamp((float)((pt.X - graph.Left) / Math.Max(1.0, graph.Width)), 0f, 1f);
        float normY = Math.Clamp(1.0f - (float)((pt.Y - graph.Top) / Math.Max(1.0, graph.Height)), 0f, 1f);
        return (normX, normY);
    }

    public override void Render(DrawingContext context)
    {
        var fullRect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (fullRect.Width <= 30 || fullRect.Height <= 40) return;

        context.DrawRectangle(ControlBgBrush, ControlBorderPen, fullRect, 4, 4);

        // 1. Top Channel Tabs: [Master] [Red] [Green] [Blue] ... [↺]
        DrawChannelHeaderBar(context, fullRect.Width);

        // 2. Main Graph Area
        var graph = GetGraphRect();
        context.DrawRectangle(GraphBgBrush, GraphBorderPen, graph, 2, 2);

        // 3. Subtle background histogram (if available)
        DrawBackgroundHistogram(context, graph);

        // 4. 4x4 Gridlines (0%, 25%, 50%, 75%, 100%)
        for (int i = 1; i <= 3; i++)
        {
            double ratio = i / 4.0;
            double gx = graph.Left + graph.Width * ratio;
            double gy = graph.Top + graph.Height * ratio;
            var pen = i == 2 ? CenterGridPen : GridPen;
            context.DrawLine(pen, new Point(gx, graph.Top), new Point(gx, graph.Bottom));
            context.DrawLine(pen, new Point(graph.Left, gy), new Point(graph.Right, gy));
        }

        // 5. Diagonal Linear Reference Line (Bottom-Left (0,0) to Top-Right (1,1))
        context.DrawLine(DiagonalPen, new Point(graph.Left, graph.Bottom), new Point(graph.Right, graph.Top));

        if (_clip == null) return;

        // 6. Draw inactive channels dimmed in background
        for (int ch = 0; ch < 4; ch++)
        {
            if (ch == _activeChannel) continue;
            var pts = GetChannelPoints(ch);
            if (pts != null && pts.Count >= 2)
            {
                var dimPen = ch switch
                {
                    1 => RedDimPen,
                    2 => GreenDimPen,
                    3 => BlueDimPen,
                    _ => MasterDimPen
                };
                DrawSplineCurve(context, graph, pts, dimPen);
            }
        }

        // 7. Draw active channel curve & control nodes
        var activePts = GetChannelPoints(_activeChannel);
        if (activePts != null && activePts.Count >= 2)
        {
            var activePen = _activeChannel switch
            {
                1 => RedPen,
                2 => GreenPen,
                3 => BluePen,
                _ => MasterPen
            };
            DrawSplineCurve(context, graph, activePts, activePen);

            var nodeFill = _activeChannel switch
            {
                1 => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                2 => new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                3 => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                _ => new SolidColorBrush(Color.FromRgb(243, 244, 246))
            };
            var nodeStroke = new Pen(new SolidColorBrush(Color.Parse("#0A0B0E")), 1.5);
            var hoverRingPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 1.5);

            for (int i = 0; i < activePts.Count; i++)
            {
                var pt = NormToScreen(graph, activePts[i].X, activePts[i].Y);
                double r = (i == _draggingPointIndex) ? 6.0 : 4.5;

                // Highlight ring if hovered or dragging
                if (i == _hoveredPointIndex || i == _draggingPointIndex)
                {
                    context.DrawEllipse(null, hoverRingPen, pt, 8.5, 8.5);
                }

                context.DrawEllipse(nodeFill, nodeStroke, pt, r, r);
            }

            // 8. HUD Readout (In: 0..255 → Out: 0..255)
            DrawHudReadout(context, graph, activePts);
        }
    }

    private void DrawBackgroundHistogram(DrawingContext context, Rect graph)
    {
        if (_histogramBins == null || _histogramBins.Length == 0) return;

        int max = 1;
        for (int i = 0; i < _histogramBins.Length; i++)
        {
            if (_histogramBins[i] > max) max = _histogramBins[i];
        }

        var histBrush = new SolidColorBrush(Color.FromArgb(28, 140, 160, 200));
        double barW = graph.Width / _histogramBins.Length;

        for (int i = 0; i < _histogramBins.Length; i++)
        {
            double barH = (_histogramBins[i] / (double)max) * (graph.Height * 0.75);
            if (barH < 1.0) continue;
            double bx = graph.Left + i * barW;
            double by = graph.Bottom - barH;
            context.FillRectangle(histBrush, new Rect(bx, by, Math.Max(1.0, barW - 0.5), barH));
        }
    }

    private void DrawChannelHeaderBar(DrawingContext context, double width)
    {
        (string Name, Color Accent)[] tabs =
        {
            ("Master", Color.FromRgb(243, 244, 246)),
            ("Red", Color.FromRgb(239, 68, 68)),
            ("Green", Color.FromRgb(16, 185, 129)),
            ("Blue", Color.FromRgb(59, 130, 246))
        };

        double x = Pad;
        for (int i = 0; i < tabs.Length; i++)
        {
            bool active = _activeChannel == i;
            double tabW = i == 0 ? 50 : 42;
            var tabRect = new Rect(x, 3, tabW, 18);
            var bg = active
                ? new SolidColorBrush(Color.FromArgb(50, tabs[i].Accent.R, tabs[i].Accent.G, tabs[i].Accent.B))
                : new SolidColorBrush(Color.Parse("#14161C"));
            var border = new Pen(new SolidColorBrush(active ? tabs[i].Accent : Color.Parse("#252834")), 1);
            context.DrawRectangle(bg, border, tabRect, 3, 3);

            var ft = new FormattedText(
                tabs[i].Name,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter, Segoe UI", FontStyle.Normal, active ? FontWeight.Bold : FontWeight.Medium),
                9.5,
                new SolidColorBrush(active ? tabs[i].Accent : Color.Parse("#8B92A5")));
            context.DrawText(ft, new Point(x + (tabW - ft.Width) * 0.5, 5));
            x += tabW + 4;
        }

        // Reset [↺] button on far right
        var resetRect = new Rect(width - Pad - 22, 3, 22, 18);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#14161C")), GraphBorderPen, resetRect, 3, 3);
        var rft = new FormattedText(
            "↺",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter, Segoe UI"),
            11,
            new SolidColorBrush(Color.Parse("#9CA3AF")));
        context.DrawText(rft, new Point(resetRect.X + 5, 4));
    }

    private static void DrawSplineCurve(DrawingContext context, Rect graph, List<CurveControlPoint> pts, IPen pen)
    {
        float[] lut = MonotonicCubicCurveSolver.SolveLut01(pts);
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            for (int i = 0; i < 256; i++)
            {
                float nx = i / 255f;
                float ny = lut[i];
                var pt = NormToScreen(graph, nx, ny);
                if (i == 0) ctx.BeginFigure(pt, false);
                else ctx.LineTo(pt);
            }
            ctx.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geom);
    }

    private void DrawHudReadout(DrawingContext context, Rect graph, List<CurveControlPoint> pts)
    {
        int targetIdx = _draggingPointIndex >= 0 ? _draggingPointIndex : _hoveredPointIndex;
        if (targetIdx >= 0 && targetIdx < pts.Count)
        {
            int inVal = (int)MathF.Round(pts[targetIdx].X * 255f);
            int outVal = (int)MathF.Round(pts[targetIdx].Y * 255f);
            string text = $"In: {inVal} → Out: {outVal}";

            var ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Cascadia Mono, Consolas, JetBrains Mono, Monospace", FontStyle.Normal, FontWeight.SemiBold),
                10,
                new SolidColorBrush(Color.Parse("#E2E4EC")));

            var badgeRect = new Rect(graph.Left + 6, graph.Top + 6, ft.Width + 8, ft.Height + 4);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(190, 14, 16, 21)), GraphBorderPen, badgeRect, 2, 2);
            context.DrawText(ft, new Point(badgeRect.X + 4, badgeRect.Y + 2));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        var graph = GetGraphRect();

        if (_isDragging && _clip != null && _draggingPointIndex >= 0)
        {
            var pts = GetChannelPoints(_activeChannel);
            if (pts != null && _draggingPointIndex < pts.Count)
            {
                var (normX, normY) = ScreenToNorm(graph, pt);

                if (_draggingPointIndex == 0)
                {
                    // Black Point: allows adjusting Output Y freely (lifting black) and X between 0 and next node - 0.01
                    float maxX = pts.Count > 1 ? pts[1].X - 0.01f : 0.99f;
                    pts[0].X = Math.Clamp(normX, 0f, Math.Max(0f, maxX));
                    pts[0].Y = normY;
                }
                else if (_draggingPointIndex == pts.Count - 1)
                {
                    // White Point: allows adjusting Output Y freely (lowering white) and X between prev node + 0.01 and 1.0
                    float minX = pts.Count > 1 ? pts[^2].X + 0.01f : 0.01f;
                    pts[^1].X = Math.Clamp(normX, minX, 1f);
                    pts[^1].Y = normY;
                }
                else
                {
                    // Interior node: strictly locked between adjacent nodes so nodes can NEVER jump or swap order!
                    float minX = pts[_draggingPointIndex - 1].X + 0.01f;
                    float maxX = pts[_draggingPointIndex + 1].X - 0.01f;
                    pts[_draggingPointIndex].X = Math.Clamp(normX, minX, Math.Max(minX, maxX));
                    pts[_draggingPointIndex].Y = normY;
                }

                CurveChanged?.Invoke();
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        // Hover detection: highlight point when mouse is within 10px
        int oldHover = _hoveredPointIndex;
        _hoveredPointIndex = -1;

        if (_clip != null && graph.Contains(pt))
        {
            var pts = GetChannelPoints(_activeChannel);
            if (pts != null)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    var screenPt = NormToScreen(graph, pts[i].X, pts[i].Y);
                    double dist = Math.Sqrt(Math.Pow(pt.X - screenPt.X, 2) + Math.Pow(pt.Y - screenPt.Y, 2));
                    if (dist <= 10.0)
                    {
                        _hoveredPointIndex = i;
                        _hoverMousePos = pt;
                        break;
                    }
                }
            }
        }

        Cursor = _hoveredPointIndex >= 0 ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Cross);
        if (oldHover != _hoveredPointIndex)
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        // 1. Header Bar: channel pill click or reset click
        if (pt.Y <= HeaderHeight)
        {
            if (pt.X >= Bounds.Width - Pad - 24)
            {
                ResetActiveChannel();
                e.Handled = true;
                return;
            }

            double x = Pad;
            int[] widths = { 50, 42, 42, 42 };
            for (int i = 0; i < 4; i++)
            {
                if (pt.X >= x && pt.X <= x + widths[i])
                {
                    _activeChannel = i;
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
                x += widths[i] + 4;
            }
            return;
        }

        if (_clip == null) return;
        var pts = GetChannelPoints(_activeChannel);
        if (pts == null) return;

        var graph = GetGraphRect();
        if (!graph.Contains(pt) && pt.Y > HeaderHeight) return;

        // 2. Check if clicking on an existing point within 10px
        int hitIdx = -1;
        double bestDist = 10.5;
        for (int i = 0; i < pts.Count; i++)
        {
            var screenPt = NormToScreen(graph, pts[i].X, pts[i].Y);
            double dist = Math.Sqrt(Math.Pow(pt.X - screenPt.X, 2) + Math.Pow(pt.Y - screenPt.Y, 2));
            if (dist <= bestDist)
            {
                bestDist = dist;
                hitIdx = i;
            }
        }

        // 3. Right-Click or Double-Click on an interior node removes it
        if ((props.IsRightButtonPressed || e.ClickCount >= 2) && hitIdx > 0 && hitIdx < pts.Count - 1)
        {
            var before = _clip.Clone();
            pts.RemoveAt(hitIdx);
            var after = _clip.Clone();
            _draggingPointIndex = -1;
            _isDragging = false;
            CurveChanged?.Invoke();
            CurveCommitted?.Invoke(before, after, "Delete Curve Point");
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (props.IsRightButtonPressed) return;

        _dragPreEditSnapshot = _clip.Clone();

        if (hitIdx >= 0)
        {
            // Clicked existing node -> Start dragging immediately
            _draggingPointIndex = hitIdx;
            _isDragging = true;
        }
        else if (pts.Count < 16)
        {
            // Clicked empty area -> Immediately add node and start dragging it in the same motion!
            var (normX, normY) = ScreenToNorm(graph, pt);
            var newPoint = new CurveControlPoint(normX, normY);
            pts.Add(newPoint);
            pts.Sort((a, b) => a.X.CompareTo(b.X));
            _draggingPointIndex = pts.IndexOf(newPoint);
            _isDragging = true;
            CurveChanged?.Invoke();
        }

        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging)
        {
            _isDragging = false;
            _draggingPointIndex = -1;
            e.Pointer.Capture(null);

            if (_clip != null && _dragPreEditSnapshot != null)
            {
                var after = _clip.Clone();
                CurveCommitted?.Invoke(_dragPreEditSnapshot, after, "Adjust RGB Curve");
            }
            _dragPreEditSnapshot = null;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    public void ResetActiveChannel()
    {
        if (_clip == null) return;
        var pts = GetChannelPoints(_activeChannel);
        if (pts == null) return;

        var before = _clip.Clone();
        pts.Clear();
        pts.Add(new CurveControlPoint(0f, 0f));
        pts.Add(new CurveControlPoint(1f, 1f));
        var after = _clip.Clone();

        CurveChanged?.Invoke();
        CurveCommitted?.Invoke(before, after, "Reset RGB Curve");
        InvalidateVisual();
    }
}
