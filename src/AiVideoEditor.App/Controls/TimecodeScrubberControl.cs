using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AiVideoEditor.Core.Services;

namespace AiVideoEditor.App.Controls;

public sealed class TimecodeScrubberControl : Control
{
    private double _timeSeconds;
    private double _fps = 24.0;
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartSeconds;

    public event Action<double>? TimeScrubbed;

    public TimecodeScrubberControl()
    {
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
    }

    public void SetTimecode(double timeSeconds, double fps)
    {
        _timeSeconds = Math.Max(0, timeSeconds);
        _fps = fps > 0 ? fps : 24.0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width <= 4 || bounds.Height <= 4) return;

        context.FillRectangle(new SolidColorBrush(Color.FromRgb(24, 24, 28)), new Rect(0, 0, bounds.Width, bounds.Height));
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(46, 46, 52)), 1), new Rect(0, 0, bounds.Width, bounds.Height));

        string smpte = TimecodeCalculator.SecondsToSmpte(_timeSeconds, _fps);
        var ft = new FormattedText(
            smpte,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono, JetBrains Mono, Consolas, Monospace", FontStyle.Normal, FontWeight.SemiBold),
            13,
            new SolidColorBrush(Color.FromRgb(59, 130, 246)));

        double x = (bounds.Width - ft.Width) * 0.5;
        double y = (bounds.Height - ft.Height) * 0.5;
        context.DrawText(ft, new Point(Math.Max(4, x), Math.Max(1, y)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isDragging = true;
        _dragStartPoint = e.GetPosition(this);
        _dragStartSeconds = _timeSeconds;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;
        var pt = e.GetPosition(this);
        double deltaFrames = (pt.X - _dragStartPoint.X) * 0.5;
        double newTime = Math.Max(0, _dragStartSeconds + deltaFrames / Math.Max(1.0, _fps));
        _timeSeconds = newTime;
        TimeScrubbed?.Invoke(_timeSeconds);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
    }
}
