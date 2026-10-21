using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AiVideoEditor.App.Controls;

/// <summary>
/// Real-Time RGB Histogram &amp; Luma Waveform Scope (Section 3.3).
/// Renders 64-bin subsampled histograms computed in &lt; 0.4ms by ColorScienceProcessor.
/// </summary>
public sealed class VideoScopeControl : Control
{
    private int[] _rBins = new int[64];
    private int[] _gBins = new int[64];
    private int[] _bBins = new int[64];
    private int[] _lumaBins = new int[64];
    private bool _lumaOnlyMode = false;

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#0B0C0F"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#22252E")), 1);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#181A22")), 1);
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#6E7385"));
    private static readonly IBrush BadgeBrush = new SolidColorBrush(Color.Parse("#181A20"));

    private static readonly IPen RedPen = new Pen(new SolidColorBrush(Color.FromArgb(210, 239, 68, 68)), 1.5);
    private static readonly IPen GreenPen = new Pen(new SolidColorBrush(Color.FromArgb(210, 16, 185, 129)), 1.5);
    private static readonly IPen BluePen = new Pen(new SolidColorBrush(Color.FromArgb(210, 59, 130, 246)), 1.5);
    private static readonly IPen LumaPen = new Pen(new SolidColorBrush(Color.FromArgb(220, 226, 228, 236)), 1.6);

    private static readonly IBrush RedFill = new SolidColorBrush(Color.FromArgb(42, 239, 68, 68));
    private static readonly IBrush GreenFill = new SolidColorBrush(Color.FromArgb(42, 16, 185, 129));
    private static readonly IBrush BlueFill = new SolidColorBrush(Color.FromArgb(42, 59, 130, 246));
    private static readonly IBrush LumaFill = new SolidColorBrush(Color.FromArgb(55, 226, 228, 236));

    public VideoScopeControl()
    {
        Height = 76;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Live Rec.709 Video Scope — Click to toggle RGB Histogram / Luma Waveform");
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
        _lumaOnlyMode = !_lumaOnlyMode;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (rect.Width <= 10 || rect.Height <= 10) return;

        context.DrawRectangle(BgBrush, BorderPen, rect, 4, 4);

        // IRE Horizontal Gridlines (25%, 50%, 75%)
        for (int i = 1; i <= 3; i++)
        {
            double gy = rect.Height * (i / 4.0);
            context.DrawLine(GridPen, new Point(2, gy), new Point(rect.Width - 2, gy));
        }

        // Vertical Quartile Gridlines
        for (int i = 1; i <= 3; i++)
        {
            double gx = rect.Width * (i / 4.0);
            context.DrawLine(GridPen, new Point(gx, 2), new Point(gx, rect.Height - 2));
        }

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
            if (_lumaOnlyMode)
            {
                DrawChannelCurve(context, _lumaBins, maxVal, rect, LumaPen, LumaFill);
            }
            else
            {
                DrawChannelCurve(context, _bBins, maxVal, rect, BluePen, BlueFill);
                DrawChannelCurve(context, _gBins, maxVal, rect, GreenPen, GreenFill);
                DrawChannelCurve(context, _rBins, maxVal, rect, RedPen, RedFill);
            }
        }

        // Scope Mode Badge
        string modeLabel = _lumaOnlyMode ? "LUMA IRE" : "RGB HISTOGRAM";
        var ft = new FormattedText(
            modeLabel,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter, Segoe UI"),
            8.5,
            TextBrush);

        context.DrawRectangle(BadgeBrush, null, new Rect(6, 4, ft.Width + 8, 13), 2, 2);
        context.DrawText(ft, new Point(10, 5));
    }

    private static void DrawChannelCurve(
        DrawingContext context,
        int[] bins,
        int maxVal,
        Rect bounds,
        IPen strokePen,
        IBrush fillBrush)
    {
        double w = bounds.Width - 8;
        double h = bounds.Height - 10;
        double left = 4;
        double bottom = bounds.Height - 4;

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(left, bottom), true);
            for (int i = 0; i < 64; i++)
            {
                double x = left + (i / 63.0) * w;
                double norm = Math.Sqrt((double)bins[i] / maxVal); // soft log-like visual scaling
                double y = bottom - Math.Clamp(norm, 0.0, 1.0) * h;
                ctx.LineTo(new Point(x, y));
            }
            ctx.LineTo(new Point(left + w, bottom));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(fillBrush, strokePen, geom);
    }
}
