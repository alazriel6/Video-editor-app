using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace AiVideoEditor.App.Controls;

/// <summary>
/// Dual-mode Premiere Pro / DaVinci Resolve Scrubbable &amp; Direct-Type Number Box (Section 2.2).
/// - Scrub Mode (default): Cursor is SizeWestEast (↔); drag horizontally to scrub by StepSize * deltaX (Shift=10x, Alt=0.1x).
/// - Direct Type Mode (click without drag or double-click): Opens a borderless TextBox with SelectAll(); Enter/LostFocus commits, Escape cancels.
/// </summary>
public sealed class ScrubbableNumberBox : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ScrubbableNumberBox, double>(
            nameof(Value),
            0.0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ScrubbableNumberBox, double>(nameof(Minimum), -1000.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ScrubbableNumberBox, double>(nameof(Maximum), 1000.0);

    public static readonly StyledProperty<double> StepSizeProperty =
        AvaloniaProperty.Register<ScrubbableNumberBox, double>(nameof(StepSize), 1.0);

    public static readonly StyledProperty<string> FormatStringProperty =
        AvaloniaProperty.Register<ScrubbableNumberBox, string>(nameof(FormatString), "0.0");

    public static readonly StyledProperty<string> SuffixProperty =
        AvaloniaProperty.Register<ScrubbableNumberBox, string>(nameof(Suffix), string.Empty);

    private readonly Border _outerBorder;
    private readonly TextBlock _displayBlock;
    private readonly TextBox _editBox;

    private bool _isPointerDown;
    private bool _isScrubbing;
    private bool _isEditing;
    private Point _pressPoint;
    private double _scrubStartValue;

    public event Action<double, double>? ValueCommitted;

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double StepSize
    {
        get => GetValue(StepSizeProperty);
        set => SetValue(StepSizeProperty, value);
    }

    public string FormatString
    {
        get => GetValue(FormatStringProperty);
        set => SetValue(FormatStringProperty, value);
    }

    public string Suffix
    {
        get => GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public ScrubbableNumberBox()
    {
        Width = 58;
        Height = 22;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);

        _displayBlock = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, JetBrains Mono, Consolas, Monospace"),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 4, 0),
            IsHitTestVisible = false
        };

        _editBox = new TextBox
        {
            FontFamily = new FontFamily("Cascadia Mono, JetBrains Mono, Consolas, Monospace"),
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246)),
            Background = new SolidColorBrush(Color.Parse("#121214")),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0),
            MinHeight = 18,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            IsVisible = false
        };

        _editBox.KeyDown += OnEditBoxKeyDown;
        _editBox.LostFocus += OnEditBoxLostFocus;

        var host = new Grid();
        host.Children.Add(_displayBlock);
        host.Children.Add(_editBox);

        _outerBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#121214")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2E2E34")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Height = 22,
            Child = host
        };

        Content = _outerBorder;
        UpdateFormattedText();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty ||
            change.Property == FormatStringProperty ||
            change.Property == SuffixProperty)
        {
            UpdateFormattedText();
        }
    }

    private void UpdateFormattedText()
    {
        string fmt = string.IsNullOrWhiteSpace(FormatString) ? "0.0" : FormatString;
        string numPart;
        try
        {
            numPart = Value.ToString(fmt, CultureInfo.InvariantCulture);
        }
        catch
        {
            numPart = Value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        _displayBlock.Text = string.IsNullOrEmpty(Suffix) ? numPart : $"{numPart}{Suffix}";
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_isEditing) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;

        if (e.ClickCount >= 2)
        {
            EnterEditMode();
            e.Handled = true;
            return;
        }

        _isPointerDown = true;
        _isScrubbing = false;
        _pressPoint = e.GetPosition(this);
        _scrubStartValue = Value;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isPointerDown || _isEditing) return;

        var pt = e.GetPosition(this);
        double dx = pt.X - _pressPoint.X;

        if (!_isScrubbing && Math.Abs(dx) >= 2.5)
        {
            _isScrubbing = true;
            _outerBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // #3B82F6
        }

        if (_isScrubbing)
        {
            double multiplier = 1.0;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) multiplier = 5.0;
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) multiplier = 0.1;

            double step = StepSize > 0 ? StepSize : 1.0;
            double candidate = _scrubStartValue + (dx * 0.5 * step * multiplier);
            double clamped = Math.Clamp(candidate, Minimum, Maximum);
            Value = clamped;
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isPointerDown) return;

        _isPointerDown = false;
        e.Pointer.Capture(null);

        if (_isScrubbing)
        {
            _isScrubbing = false;
            _outerBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(46, 50, 62));
            if (Math.Abs(Value - _scrubStartValue) > 0.0001)
            {
                ValueCommitted?.Invoke(_scrubStartValue, Value);
            }
        }
        else
        {
            // Single click without drag -> Enter Direct Type Mode!
            EnterEditMode();
        }
        e.Handled = true;
    }

    private void EnterEditMode()
    {
        _isEditing = true;
        _scrubStartValue = Value;
        _outerBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));
        string fmt = string.IsNullOrWhiteSpace(FormatString) ? "0.##" : FormatString.Replace("+", "");
        _editBox.Text = Value.ToString(fmt, CultureInfo.InvariantCulture);
        _displayBlock.IsVisible = false;
        _editBox.IsVisible = true;
        _editBox.Focus();
        _editBox.SelectAll();
    }

    private void CommitEdit(bool applyText)
    {
        if (!_isEditing) return;
        _isEditing = false;

        if (applyText && _editBox.Text != null)
        {
            string cleaned = _editBox.Text
                .Replace("%", "")
                .Replace("dB", "", StringComparison.OrdinalIgnoreCase)
                .Replace("px", "", StringComparison.OrdinalIgnoreCase)
                .Replace("°", "")
                .Replace("s", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
                double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            {
                double clamped = Math.Clamp(parsed, Minimum, Maximum);
                Value = clamped;
                if (Math.Abs(clamped - _scrubStartValue) > 0.0001)
                {
                    ValueCommitted?.Invoke(_scrubStartValue, clamped);
                }
            }
        }
        else
        {
            Value = _scrubStartValue;
        }

        _editBox.IsVisible = false;
        _displayBlock.IsVisible = true;
        _outerBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(46, 50, 62));
        UpdateFormattedText();
    }

    private void OnEditBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            CommitEdit(applyText: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CommitEdit(applyText: false);
            e.Handled = true;
        }
    }

    private void OnEditBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        CommitEdit(applyText: true);
    }
}
