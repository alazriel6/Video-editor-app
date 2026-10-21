using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AiVideoEditor.App.Controls;

public sealed class AudioVuMeterControl : Control
{
    private float _leftDb = -60.0f;
    private float _rightDb = -60.0f;
    private float _peakHoldLeftDb = -60.0f;
    private float _peakHoldRightDb = -60.0f;

    public void SetLevels(float leftDb, float rightDb)
    {
        _leftDb = Math.Clamp(leftDb, -60f, 3f);
        _rightDb = Math.Clamp(rightDb, -60f, 3f);

        if (_leftDb > _peakHoldLeftDb) _peakHoldLeftDb = _leftDb;
        else _peakHoldLeftDb = Math.Max(-60f, _peakHoldLeftDb - 0.35f);

        if (_rightDb > _peakHoldRightDb) _peakHoldRightDb = _rightDb;
        else _peakHoldRightDb = Math.Max(-60f, _peakHoldRightDb - 0.35f);

        InvalidateVisual();
    }

    public void ResetLevels()
    {
        _leftDb = -60f;
        _rightDb = -60f;
        _peakHoldLeftDb = -60f;
        _peakHoldRightDb = -60f;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width < 16 || bounds.Height < 30) return;

        var bgBrush = new SolidColorBrush(Color.FromRgb(24, 25, 30)); // #18191E
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(45, 48, 59)), 1); // #2D303B
        context.FillRectangle(bgBrush, new Rect(0, 0, bounds.Width, bounds.Height));
        context.DrawRectangle(borderPen, new Rect(0, 0, bounds.Width, bounds.Height));

        double topPad = 10;
        double bottomPad = 18;
        double barAreaHeight = Math.Max(10, bounds.Height - topPad - bottomPad);

        double barWidth = 10;
        double leftBarX = 8;
        double rightBarX = 22;

        DrawChannelBar(context, leftBarX, topPad, barWidth, barAreaHeight, _leftDb, _peakHoldLeftDb);
        DrawChannelBar(context, rightBarX, topPad, barWidth, barAreaHeight, _rightDb, _peakHoldRightDb);

        // Draw dB Scale ticks on the right
        int[] dbTicks = { 0, -6, -12, -24, -36, -48, -60 };
        var tickBrush = new SolidColorBrush(Color.FromRgb(154, 154, 166));
        var tickPen = new Pen(new SolidColorBrush(Color.FromRgb(70, 70, 80)), 1);

        foreach (int db in dbTicks)
        {
            double norm = DbToNormalized(db);
            double y = topPad + barAreaHeight * (1.0 - norm);
            context.DrawLine(tickPen, new Point(rightBarX + barWidth + 2, y), new Point(rightBarX + barWidth + 6, y));

            var ft = new FormattedText(db.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Cascadia Mono, Consolas, Monospace"), 8.5, tickBrush);
            context.DrawText(ft, new Point(rightBarX + barWidth + 8, y - 5));
        }

        // L / R footer labels
        var lFt = new FormattedText("L", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Bold), 9, new SolidColorBrush(Color.FromRgb(230, 230, 235)));
        var rFt = new FormattedText("R", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Inter", FontStyle.Normal, FontWeight.Bold), 9, new SolidColorBrush(Color.FromRgb(230, 230, 235)));
        context.DrawText(lFt, new Point(leftBarX + 2, bounds.Height - 14));
        context.DrawText(rFt, new Point(rightBarX + 2, bounds.Height - 14));
    }

    private static void DrawChannelBar(
        DrawingContext context,
        double x,
        double top,
        double width,
        double height,
        float currentDb,
        float peakHoldDb)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(18, 18, 20)), new Rect(x, top, width, height));

        double normLevel = DbToNormalized(currentDb);
        double normWarnStart = DbToNormalized(-12f); // -12 dB
        double normPeakStart = DbToNormalized(-3f);  // -3 dB

        // Safe zone (-60 dB to -12 dB): #22C55E
        double safeNorm = Math.Min(normLevel, normWarnStart);
        if (safeNorm > 0.005)
        {
            double h = height * safeNorm;
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                new Rect(x, top + height - h, width, h));
        }

        // Warn zone (-12 dB to -3 dB): #EAB308
        if (normLevel > normWarnStart)
        {
            double warnTopNorm = Math.Min(normLevel, normPeakStart);
            double yBottom = top + height * (1.0 - normWarnStart);
            double yTop = top + height * (1.0 - warnTopNorm);
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(234, 179, 8)),
                new Rect(x, yTop, width, Math.Max(1, yBottom - yTop)));
        }

        // Peak zone (-3 dB to 0 dB+): #EF4444
        if (normLevel > normPeakStart)
        {
            double yBottom = top + height * (1.0 - normPeakStart);
            double yTop = top + height * (1.0 - normLevel);
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                new Rect(x, yTop, width, Math.Max(1, yBottom - yTop)));
        }

        // Peak hold indicator line
        double holdNorm = DbToNormalized(peakHoldDb);
        if (holdNorm > 0.02)
        {
            double holdY = top + height * (1.0 - holdNorm);
            var holdColor = peakHoldDb >= -3f
                ? Color.FromRgb(239, 68, 68)
                : peakHoldDb >= -12f
                    ? Color.FromRgb(234, 179, 8)
                    : Color.FromRgb(230, 230, 235);
            context.DrawLine(new Pen(new SolidColorBrush(holdColor), 1.5), new Point(x, holdY), new Point(x + width, holdY));
        }
    }

    private static double DbToNormalized(double db)
    {
        return Math.Clamp((db + 60.0) / 60.0, 0.0, 1.0);
    }
}
