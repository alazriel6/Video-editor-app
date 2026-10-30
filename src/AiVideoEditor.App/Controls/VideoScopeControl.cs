using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AiVideoEditor.MediaEngine.Shaders;

namespace AiVideoEditor.App.Controls;

/// <summary>
/// Multi-Mode Real-Time Rec.709 Video Scopes (RGB Parade, Vectorscope with Skin-Tone Line, Luma Waveform, RGB Histogram).
/// </summary>
public sealed class VideoScopeControl : Control
{
    public enum ScopeViewMode
    {
        RgbParade = 0,
        Vectorscope = 1,
        LumaWaveform = 2,
        RgbHistogram = 3
    }

    private const double HeaderHeight = 22.0;

    private ScopeRenderData? _scopeData;
    private int[] _rBins = new int[64];
    private int[] _gBins = new int[64];
    private int[] _bBins = new int[64];
    private int[] _lumaBins = new int[64];
    private ScopeViewMode _mode = ScopeViewMode.RgbParade;

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#0B0C0F"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#22252E")), 1);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#181A22")), 1);
    private static readonly IPen SkinTonePen = new Pen(new SolidColorBrush(Color.FromArgb(190, 245, 158, 11)), 1.2, DashStyle.Dash);

    private static readonly IPen RedPen = new Pen(new SolidColorBrush(Color.FromArgb(215, 239, 68, 68)), 1.5);
    private static readonly IPen GreenPen = new Pen(new SolidColorBrush(Color.FromArgb(215, 16, 185, 129)), 1.5);
    private static readonly IPen BluePen = new Pen(new SolidColorBrush(Color.FromArgb(215, 59, 130, 246)), 1.5);
    private static readonly IPen LumaPen = new Pen(new SolidColorBrush(Color.FromArgb(225, 226, 228, 236)), 1.5);

    private static readonly IBrush RedFill = new SolidColorBrush(Color.FromArgb(42, 239, 68, 68));
    private static readonly IBrush GreenFill = new SolidColorBrush(Color.FromArgb(42, 16, 185, 129));
    private static readonly IBrush BlueFill = new SolidColorBrush(Color.FromArgb(42, 59, 130, 246));
    private static readonly IBrush LumaFill = new SolidColorBrush(Color.FromArgb(55, 226, 228, 236));

    public VideoScopeControl()
    {
        Height = 136;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Live Rec.709 Video Scopes — Click tabs to switch between RGB Parade, Vectorscope, Luma Waveform, and Histogram");
    }

    public void UpdateScopeData(ScopeRenderData? scopeData)
    {
        _scopeData = scopeData;
        if (scopeData == null)
        {
            Array.Clear(_rBins, 0, _rBins.Length);
            Array.Clear(_gBins, 0, _gBins.Length);
            Array.Clear(_bBins, 0, _bBins.Length);
            Array.Clear(_lumaBins, 0, _lumaBins.Length);
        }
        else
        {
            _rBins = scopeData.HistR;
            _gBins = scopeData.HistG;
            _bBins = scopeData.HistB;
            _lumaBins = scopeData.HistLuma;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual, Avalonia.Threading.DispatcherPriority.Background);
    }

    public void UpdateScope((int[] R, int[] G, int[] B, int[] Luma)? data)
    {
        if (data == null)
        {
            Array.Clear(_rBins, 0, _rBins.Length);
            Array.Clear(_gBins, 0, _gBins.Length);
            Array.Clear(_bBins, 0, _bBins.Length);
            Array.Clear(_lumaBins, 0, _lumaBins.Length);
        }
        else
        {
            _rBins = data.Value.R;
            _gBins = data.Value.G;
            _bBins = data.Value.B;
            _lumaBins = data.Value.Luma;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual, Avalonia.Threading.DispatcherPriority.Background);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        if (pt.Y <= HeaderHeight)
        {
            double tabW = Math.Max(30, (Bounds.Width - 8) / 4.0);
            int idx = Math.Clamp((int)((pt.X - 4) / tabW), 0, 3);
            _mode = (ScopeViewMode)idx;
        }
        else
        {
            _mode = (ScopeViewMode)(((int)_mode + 1) % 4);
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var fullRect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (fullRect.Width <= 20 || fullRect.Height <= 30) return;

        context.DrawRectangle(BgBrush, BorderPen, fullRect, 4, 4);
        DrawModeTabs(context, fullRect.Width);

        var plotRect = new Rect(4, HeaderHeight + 2, fullRect.Width - 8, fullRect.Height - HeaderHeight - 6);
        if (plotRect.Width <= 10 || plotRect.Height <= 10) return;

        switch (_mode)
        {
            case ScopeViewMode.RgbParade:
                RenderRgbParade(context, plotRect);
                break;
            case ScopeViewMode.Vectorscope:
                RenderVectorscope(context, plotRect);
                break;
            case ScopeViewMode.LumaWaveform:
                RenderLumaWaveform(context, plotRect);
                break;
            default:
                RenderHistogram(context, plotRect);
                break;
        }
    }

    private void DrawModeTabs(DrawingContext context, double width)
    {
        string[] labels = { "RGB Parade", "Vectorscope", "Waveform", "Histogram" };
        double tabW = Math.Max(30, (width - 8) / 4.0);

        for (int i = 0; i < 4; i++)
        {
            bool active = (int)_mode == i;
            var r = new Rect(4 + i * tabW + 1, 3, tabW - 2, HeaderHeight - 5);
            var bg = active
                ? new SolidColorBrush(Color.Parse("#1E293B"))
                : new SolidColorBrush(Color.Parse("#13151B"));
            var pen = new Pen(new SolidColorBrush(active ? Color.Parse("#3B82F6") : Color.Parse("#22252E")), 1);
            context.DrawRectangle(bg, pen, r, 3, 3);

            var ft = new FormattedText(
                labels[i],
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter, Segoe UI", FontStyle.Normal, active ? FontWeight.Bold : FontWeight.Medium),
                8.8,
                new SolidColorBrush(active ? Color.Parse("#F3F4F6") : Color.Parse("#8B92A5")));
            context.DrawText(ft, new Point(r.X + Math.Max(2, (r.Width - ft.Width) * 0.5), 5));
        }
    }

    private void RenderRgbParade(DrawingContext context, Rect plot)
    {
        DrawIreGrid(context, plot);

        double thirdW = plot.Width / 3.0;
        context.DrawLine(GridPen, new Point(plot.Left + thirdW, plot.Top), new Point(plot.Left + thirdW, plot.Bottom));
        context.DrawLine(GridPen, new Point(plot.Left + thirdW * 2, plot.Top), new Point(plot.Left + thirdW * 2, plot.Bottom));

        if (_scopeData == null) return;

        DrawParadeCell(context, _scopeData.ParadeR, new Rect(plot.Left + 2, plot.Top + 2, thirdW - 4, plot.Height - 4), Color.FromRgb(239, 68, 68));
        DrawParadeCell(context, _scopeData.ParadeG, new Rect(plot.Left + thirdW + 2, plot.Top + 2, thirdW - 4, plot.Height - 4), Color.FromRgb(16, 185, 129));
        DrawParadeCell(context, _scopeData.ParadeB, new Rect(plot.Left + thirdW * 2 + 2, plot.Top + 2, thirdW - 4, plot.Height - 4), Color.FromRgb(59, 130, 246));
    }

    private static void DrawParadeCell(DrawingContext context, int[,] grid48, Rect cell, Color baseColor)
    {
        const int cols = 48;
        const int rows = 48;
        double cw = cell.Width / cols;
        double rh = cell.Height / rows;

        for (int x = 0; x < cols; x++)
        {
            double px = cell.Left + x * cw;
            for (int y = 0; y < rows; y++)
            {
                int density = grid48[x, y];
                if (density < 1) continue;
                byte alpha = (byte)Math.Clamp(density * 18 + 40, 40, 245);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                double py = cell.Top + (rows - 1 - y) * rh;
                context.FillRectangle(brush, new Rect(px, py, Math.Max(1.3, cw), Math.Max(1.5, rh)));
            }
        }
    }

    private void RenderLumaWaveform(DrawingContext context, Rect plot)
    {
        DrawIreGrid(context, plot);
        if (_scopeData == null) return;

        DrawParadeCell(context, _scopeData.WaveformLuma, new Rect(plot.Left + 2, plot.Top + 2, plot.Width - 4, plot.Height - 4), Color.FromRgb(226, 232, 240));
    }

    private void RenderVectorscope(DrawingContext context, Rect plot)
    {
        double cx = plot.Center.X;
        double cy = plot.Center.Y;
        double radius = Math.Min(plot.Width, plot.Height) * 0.46;

        // Outer graticule ring + 50% saturation inner ring
        context.DrawEllipse(null, BorderPen, new Point(cx, cy), radius, radius);
        context.DrawEllipse(null, GridPen, new Point(cx, cy), radius * 0.5, radius * 0.5);

        // Crosshairs
        context.DrawLine(GridPen, new Point(cx - radius, cy), new Point(cx + radius, cy));
        context.DrawLine(GridPen, new Point(cx, cy - radius), new Point(cx, cy + radius));

        // Skin-Tone I-Line (~123 deg in standard UV vectorscope graticule: top-left quadrant)
        double skinAngleRad = -123.0 * Math.PI / 180.0;
        double sx = cx + Math.Cos(skinAngleRad) * radius;
        double sy = cy + Math.Sin(skinAngleRad) * radius;
        context.DrawLine(SkinTonePen, new Point(cx, cy), new Point(sx, sy));

        // 6 Primary/Secondary Chroma Target Boxes (R, Mg, B, Cy, G, Yl)
        (string Tag, double AngleDeg, string Hex)[] targets =
        {
            ("R", -104, "#EF4444"),
            ("Mg", -43, "#EC4899"),
            ("B", 13, "#3B82F6"),
            ("Cy", 76, "#06B6D4"),
            ("G", 137, "#10B981"),
            ("Yl", -167, "#F59E0B")
        };

        foreach (var (tag, deg, hex) in targets)
        {
            double rad = deg * Math.PI / 180.0;
            double tx = cx + Math.Cos(rad) * (radius * 0.75);
            double ty = cy + Math.Sin(rad) * (radius * 0.75);
            var pen = new Pen(new SolidColorBrush(Color.Parse(hex), 0.7), 1);
            context.DrawRectangle(null, pen, new Rect(tx - 3, ty - 3, 6, 6));
        }

        if (_scopeData == null) return;

        // Render 64x64 UV density cloud inside the Vectorscope circle
        const int vsSize = 64;
        double diam = radius * 2.0;
        double left = cx - radius;
        double top = cy - radius;
        double cellSz = diam / vsSize;

        for (int vy = 0; vy < vsSize; vy++)
        {
            for (int vx = 0; vx < vsSize; vx++)
            {
                int d = _scopeData.Vectorscope[vx, vy];
                if (d < 1) continue;

                byte alpha = (byte)Math.Clamp(d * 20 + 50, 50, 245);
                var dotBrush = new SolidColorBrush(Color.FromArgb(alpha, 56, 189, 248));
                context.FillRectangle(dotBrush, new Rect(left + vx * cellSz, top + vy * cellSz, Math.Max(1.6, cellSz), Math.Max(1.6, cellSz)));
            }
        }
    }

    private void RenderHistogram(DrawingContext context, Rect plot)
    {
        DrawIreGrid(context, plot);

        int maxVal = 1;
        for (int i = 0; i < 64; i++)
        {
            if (_rBins[i] > maxVal) maxVal = _rBins[i];
            if (_gBins[i] > maxVal) maxVal = _gBins[i];
            if (_bBins[i] > maxVal) maxVal = _bBins[i];
            if (_lumaBins[i] > maxVal) maxVal = _lumaBins[i];
        }

        if (maxVal > 1)
        {
            DrawChannelCurve(context, _bBins, maxVal, plot, BluePen, BlueFill);
            DrawChannelCurve(context, _gBins, maxVal, plot, GreenPen, GreenFill);
            DrawChannelCurve(context, _rBins, maxVal, plot, RedPen, RedFill);
            DrawChannelCurve(context, _lumaBins, maxVal, plot, LumaPen, LumaFill);
        }
    }

    private static void DrawIreGrid(DrawingContext context, Rect plot)
    {
        for (int i = 1; i <= 3; i++)
        {
            double gy = plot.Top + plot.Height * (i / 4.0);
            context.DrawLine(GridPen, new Point(plot.Left, gy), new Point(plot.Right, gy));
        }
    }

    private static void DrawChannelCurve(
        DrawingContext context,
        int[] bins,
        int maxVal,
        Rect bounds,
        IPen strokePen,
        IBrush fillBrush)
    {
        double w = bounds.Width - 4;
        double h = bounds.Height - 4;
        double left = bounds.Left + 2;
        double bottom = bounds.Bottom - 2;

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(left, bottom), true);
            for (int i = 0; i < 64; i++)
            {
                double x = left + (i / 63.0) * w;
                double norm = Math.Sqrt((double)bins[i] / maxVal);
                double y = bottom - Math.Clamp(norm, 0.0, 1.0) * h;
                ctx.LineTo(new Point(x, y));
            }
            ctx.LineTo(new Point(left + w, bottom));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(fillBrush, strokePen, geom);
    }
}
