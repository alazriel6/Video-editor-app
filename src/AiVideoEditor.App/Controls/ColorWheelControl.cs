using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace AiVideoEditor.App.Controls;

/// <summary>
/// Studio 3-Way Color Wheels (Lift, Gamma, Gain) with interactive Hue/Sat Puck,
/// Master Luminance Bar, and individual [↺] Reset buttons (Section 3.3).
/// </summary>
public sealed class ColorWheelControl : Control
{
    public static readonly StyledProperty<double> LiftRedProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(LiftRed), 0.0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> LiftGreenProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(LiftGreen), 0.0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> LiftBlueProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(LiftBlue), 0.0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> LiftLumProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(LiftLum), 0.0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> GammaRedProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(GammaRed), 0.0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> GammaGreenProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(GammaGreen), 0.0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> GammaBlueProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(GammaBlue), 0.0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> GammaLumProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(GammaLum), 0.0, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> GainRedProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(GainRed), 0.0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> GainGreenProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(GainGreen), 0.0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> GainBlueProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(GainBlue), 0.0, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> GainLumProperty =
        AvaloniaProperty.Register<ColorWheelControl, double>(nameof(GainLum), 0.0, defaultBindingMode: BindingMode.TwoWay);

    public double LiftRed { get => GetValue(LiftRedProperty); set => SetValue(LiftRedProperty, value); }
    public double LiftGreen { get => GetValue(LiftGreenProperty); set => SetValue(LiftGreenProperty, value); }
    public double LiftBlue { get => GetValue(LiftBlueProperty); set => SetValue(LiftBlueProperty, value); }
    public double LiftLum { get => GetValue(LiftLumProperty); set => SetValue(LiftLumProperty, value); }

    public double GammaRed { get => GetValue(GammaRedProperty); set => SetValue(GammaRedProperty, value); }
    public double GammaGreen { get => GetValue(GammaGreenProperty); set => SetValue(GammaGreenProperty, value); }
    public double GammaBlue { get => GetValue(GammaBlueProperty); set => SetValue(GammaBlueProperty, value); }
    public double GammaLum { get => GetValue(GammaLumProperty); set => SetValue(GammaLumProperty, value); }

    public double GainRed { get => GetValue(GainRedProperty); set => SetValue(GainRedProperty, value); }
    public double GainGreen { get => GetValue(GainGreenProperty); set => SetValue(GainGreenProperty, value); }
    public double GainBlue { get => GetValue(GainBlueProperty); set => SetValue(GainBlueProperty, value); }
    public double GainLum { get => GetValue(GainLumProperty); set => SetValue(GainLumProperty, value); }

    private int _activeWheelIndex = -1; // 0 = Lift, 1 = Gamma, 2 = Gain
    private bool _draggingLuma = false;

    private static readonly IBrush WheelBgBrush = new SolidColorBrush(Color.Parse("#111318"));
    private static readonly IPen WheelBorderPen = new Pen(new SolidColorBrush(Color.Parse("#2D303B")), 1.2);
    private static readonly IPen CrosshairPen = new Pen(new SolidColorBrush(Color.Parse("#22252F")), 1);
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#9EA3B5"));
    private static readonly IBrush ResetActiveBrush = new SolidColorBrush(Color.Parse("#E2E4EC"));
    private static readonly IBrush ResetDimBrush = new SolidColorBrush(Color.Parse("#4E5260"));
    private static readonly IBrush PuckFill = new SolidColorBrush(Color.Parse("#E2E4EC"));
    private static readonly IPen PuckStroke = new Pen(new SolidColorBrush(Color.Parse("#0B0C0F")), 1.5);
    private static readonly IBrush LumaTrackBrush = new SolidColorBrush(Color.Parse("#0E1014"));
    private static readonly IBrush LumaThumbBrush = new SolidColorBrush(Color.Parse("#3B82F6"));

    static ColorWheelControl()
    {
        AffectsRender<ColorWheelControl>(
            LiftRedProperty, LiftGreenProperty, LiftBlueProperty, LiftLumProperty,
            GammaRedProperty, GammaGreenProperty, GammaBlueProperty, GammaLumProperty,
            GainRedProperty, GainGreenProperty, GainBlueProperty, GainLumProperty);
    }

    public ColorWheelControl()
    {
        Height = 114;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 30 || h <= 30) return;

        double colW = w / 3.0;
        DrawSingleWheelColumn(context, 0, 0, colW, h, "LIFT", LiftRed, LiftGreen, LiftBlue, LiftLum);
        DrawSingleWheelColumn(context, 1, colW, colW, h, "GAMMA", GammaRed, GammaGreen, GammaBlue, GammaLum);
        DrawSingleWheelColumn(context, 2, colW * 2, colW, h, "GAIN", GainRed, GainGreen, GainBlue, GainLum);
    }

    private void DrawSingleWheelColumn(
        DrawingContext context,
        int index,
        double left,
        double width,
        double height,
        string title,
        double r,
        double g,
        double b,
        double lum)
    {
        double cx = left + width * 0.5;
        double cy = 53;
        double radius = Math.Min(width * 0.40, 32.0);

        bool isModified = Math.Abs(r) > 0.005 || Math.Abs(g) > 0.005 || Math.Abs(b) > 0.005 || Math.Abs(lum) > 0.005;

        // Title & Reset [↺]
        var titleText = new FormattedText(
            title,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter, Segoe UI", FontStyle.Normal, FontWeight.SemiBold),
            9.5,
            LabelBrush);
        context.DrawText(titleText, new Point(cx - radius, 3));

        var resetText = new FormattedText(
            "↺",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter, Segoe UI"),
            11,
            isModified ? ResetActiveBrush : ResetDimBrush);
        context.DrawText(resetText, new Point(cx + radius - 10, 1));

        // Wheel Background + Subtle Color Ring Quadrants
        context.DrawEllipse(WheelBgBrush, WheelBorderPen, new Point(cx, cy), radius, radius);

        // Draw subtle color axis indicators (Red top-left/right, Green bottom-left, Blue bottom-right)
        DrawHueDots(context, cx, cy, radius - 3.5);

        // Crosshair
        context.DrawLine(CrosshairPen, new Point(cx - radius + 4, cy), new Point(cx + radius - 4, cy));
        context.DrawLine(CrosshairPen, new Point(cx, cy - radius + 4), new Point(cx, cy + radius - 4));

        // Convert (r, g, b) offsets back to 2D puck position (dx, dy) in [-1..+1]
        var (pxNorm, pyNorm) = RgbToWheelXY(r, g, b);
        double puckX = cx + pxNorm * (radius - 5);
        double puckY = cy + pyNorm * (radius - 5);

        // Line from center to puck if offset
        if (Math.Abs(pxNorm) > 0.02 || Math.Abs(pyNorm) > 0.02)
        {
            var tintColor = Color.FromArgb(
                180,
                (byte)Math.Clamp(128 + r * 110, 40, 255),
                (byte)Math.Clamp(128 + g * 110, 40, 255),
                (byte)Math.Clamp(128 + b * 110, 40, 255));
            context.DrawLine(new Pen(new SolidColorBrush(tintColor), 1.5), new Point(cx, cy), new Point(puckX, puckY));
        }

        context.DrawEllipse(PuckFill, PuckStroke, new Point(puckX, puckY), 4.2, 4.2);

        // Master Luminance Slider Bar underneath wheel
        double barW = radius * 2.0;
        double barX = cx - radius;
        double barY = cy + radius + 9;
        var barRect = new Rect(barX, barY, barW, 7);
        context.DrawRectangle(LumaTrackBrush, WheelBorderPen, barRect, 3, 3);

        // Center tick
        context.DrawLine(CrosshairPen, new Point(cx, barY + 1), new Point(cx, barY + 6));

        // Luminance thumb
        double lumClamped = Math.Clamp(lum, -1.0, 1.0);
        double thumbX = cx + lumClamped * (radius - 3);
        context.DrawEllipse(LumaThumbBrush, PuckStroke, new Point(thumbX, barY + 3.5), 4.0, 4.0);
    }

    private static void DrawHueDots(DrawingContext context, double cx, double cy, double r)
    {
        // 6 subtle reference dots around perimeter so user sees color orientation immediately
        (double angleDeg, string hex)[] refs =
        {
            (-90, "#EF4444"), // Red (Top)
            (-30, "#F59E0B"), // Yellow/Warm (Top-Right)
            (30,  "#10B981"), // Green (Bottom-Right)
            (90,  "#06B6D4"), // Cyan (Bottom)
            (150, "#3B82F6"), // Blue (Bottom-Left)
            (-150,"#EC4899"), // Magenta (Top-Left)
        };

        foreach (var (deg, hex) in refs)
        {
            double rad = deg * Math.PI / 180.0;
            double dx = cx + Math.Cos(rad) * r;
            double dy = cy + Math.Sin(rad) * r;
            var brush = new SolidColorBrush(Color.Parse(hex), 0.65);
            context.DrawEllipse(brush, null, new Point(dx, dy), 2.0, 2.0);
        }
    }

    private static (double X, double Y) RgbToWheelXY(double r, double g, double b)
    {
        // Inverse of WheelXYToRgb:
        // R axis is at -90 deg (0, -1), G is at +30 deg (0.866, 0.5), B is at +150 deg (-0.866, 0.5)
        double x = (g - b) * 0.57735;
        double y = (0.5 * (g + b) - r) * 0.66667;
        double mag = Math.Sqrt(x * x + y * y);
        if (mag > 1.0)
        {
            x /= mag;
            y /= mag;
        }
        return (x, y);
    }

    private static (double R, double G, double B) WheelXYToRgb(double nx, double ny)
    {
        double mag = Math.Sqrt(nx * nx + ny * ny);
        if (mag > 1.0)
        {
            nx /= mag;
            ny /= mag;
        }

        // Zero-sum RGB vector projection from 2D wheel coordinates
        double r = -ny;
        double g = 0.5 * ny + 0.8660254 * nx;
        double b = 0.5 * ny - 0.8660254 * nx;
        return (Math.Clamp(r, -1.0, 1.0), Math.Clamp(g, -1.0, 1.0), Math.Clamp(b, -1.0, 1.0));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetPosition(this);
        double w = Bounds.Width;
        if (w <= 30) return;

        double colW = w / 3.0;
        int col = Math.Clamp((int)(pt.X / colW), 0, 2);
        double left = col * colW;
        double cx = left + colW * 0.5;
        double cy = 53;
        double radius = Math.Min(colW * 0.40, 32.0);

        // Check if Reset [↺] header area clicked or double-click on wheel
        if (pt.Y < 18 && pt.X >= cx)
        {
            ResetWheel(col);
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2)
        {
            ResetWheel(col);
            e.Handled = true;
            return;
        }

        double barY = cy + radius + 9;
        _activeWheelIndex = col;
        _draggingLuma = pt.Y >= barY - 4;

        e.Pointer.Capture(this);
        UpdateWheelFromPointer(pt);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_activeWheelIndex < 0) return;
        UpdateWheelFromPointer(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_activeWheelIndex >= 0)
        {
            _activeWheelIndex = -1;
            _draggingLuma = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void UpdateWheelFromPointer(Point pt)
    {
        double colW = Bounds.Width / 3.0;
        double left = _activeWheelIndex * colW;
        double cx = left + colW * 0.5;
        double cy = 53;
        double radius = Math.Min(colW * 0.40, 32.0);

        if (_draggingLuma)
        {
            double lum = Math.Clamp((pt.X - cx) / Math.Max(1.0, radius - 3.0), -1.0, 1.0);
            if (_activeWheelIndex == 0) LiftLum = Math.Round(lum, 3);
            else if (_activeWheelIndex == 1) GammaLum = Math.Round(lum, 3);
            else if (_activeWheelIndex == 2) GainLum = Math.Round(lum, 3);
        }
        else
        {
            double nx = (pt.X - cx) / Math.Max(1.0, radius - 5.0);
            double ny = (pt.Y - cy) / Math.Max(1.0, radius - 5.0);
            var (r, g, b) = WheelXYToRgb(nx, ny);
            if (_activeWheelIndex == 0)
            {
                LiftRed = Math.Round(r, 3);
                LiftGreen = Math.Round(g, 3);
                LiftBlue = Math.Round(b, 3);
            }
            else if (_activeWheelIndex == 1)
            {
                GammaRed = Math.Round(r, 3);
                GammaGreen = Math.Round(g, 3);
                GammaBlue = Math.Round(b, 3);
            }
            else if (_activeWheelIndex == 2)
            {
                GainRed = Math.Round(r, 3);
                GainGreen = Math.Round(g, 3);
                GainBlue = Math.Round(b, 3);
            }
        }
    }

    public void ResetWheel(int col)
    {
        if (col == 0)
        {
            LiftRed = 0; LiftGreen = 0; LiftBlue = 0; LiftLum = 0;
        }
        else if (col == 1)
        {
            GammaRed = 0; GammaGreen = 0; GammaBlue = 0; GammaLum = 0;
        }
        else if (col == 2)
        {
            GainRed = 0; GainGreen = 0; GainBlue = 0; GainLum = 0;
        }
    }
}
