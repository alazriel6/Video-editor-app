using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AiVideoEditor.App.Controls;

/// <summary>
/// 26px 5-column Precision Parameter Row (DaVinci Resolve / Premiere Pro style — Section 2.1).
/// Contains: [Label]  [<][◇][>]  [Slim Slider]  [ScrubbableNumberBox]  [Reset ↺]
/// </summary>
public partial class PrecisionParameterRow : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, string>(nameof(Label), "Parameter");

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, double>(
            nameof(Value),
            0.0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, double>(nameof(Minimum), 0.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, double>(nameof(Maximum), 100.0);

    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, double>(nameof(DefaultValue), 0.0);

    public static readonly StyledProperty<double> StepSizeProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, double>(nameof(StepSize), 1.0);

    public static readonly StyledProperty<string> FormatStringProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, string>(nameof(FormatString), "0.0");

    public static readonly StyledProperty<string> SuffixProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, string>(nameof(Suffix), string.Empty);

    public static readonly StyledProperty<bool> ShowKeyframeControlsProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, bool>(nameof(ShowKeyframeControls), false);

    public static readonly StyledProperty<string> KeyframePropertyKeyProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, string>(nameof(KeyframePropertyKey), string.Empty);

    public static readonly StyledProperty<string> DiamondIconProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, string>(nameof(DiamondIcon), "◇");

    public static readonly StyledProperty<ICommand?> PrevKeyframeCommandProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, ICommand?>(nameof(PrevKeyframeCommand));

    public static readonly StyledProperty<ICommand?> ToggleKeyframeCommandProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, ICommand?>(nameof(ToggleKeyframeCommand));

    public static readonly StyledProperty<ICommand?> NextKeyframeCommandProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, ICommand?>(nameof(NextKeyframeCommand));

    public static readonly StyledProperty<ICommand?> ResetCommandProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, ICommand?>(nameof(ResetCommand));

    public static readonly StyledProperty<string?> ResetCommandParameterProperty =
        AvaloniaProperty.Register<PrecisionParameterRow, string?>(nameof(ResetCommandParameter));

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

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

    public double DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    public double StepSize
    {
        get => GetValue(StepSizeProperty);
        set => SetValue(StepSizeProperty, value);
    }

    public double Step
    {
        get => StepSize;
        set => StepSize = value;
    }

    public int Decimals
    {
        get => FormatString.Contains('.') ? FormatString.Split('.')[1].Length : 0;
        set => FormatString = value <= 0 ? "0" : "0." + new string('0', Math.Clamp(value, 1, 4));
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

    public string Unit
    {
        get => Suffix;
        set => Suffix = value;
    }

    public bool ShowKeyframeControls
    {
        get => GetValue(ShowKeyframeControlsProperty);
        set => SetValue(ShowKeyframeControlsProperty, value);
    }

    public string KeyframePropertyKey
    {
        get => GetValue(KeyframePropertyKeyProperty);
        set => SetValue(KeyframePropertyKeyProperty, value);
    }

    public string KeyframeParameter
    {
        get => KeyframePropertyKey;
        set => KeyframePropertyKey = value;
    }

    public static readonly StyledProperty<string> KeyframeDiamondIconProperty =
        DiamondIconProperty.AddOwner<PrecisionParameterRow>();

    public string KeyframeDiamondIcon
    {
        get => GetValue(DiamondIconProperty);
        set => SetValue(DiamondIconProperty, value);
    }

    public string DiamondIcon
    {
        get => GetValue(DiamondIconProperty);
        set => SetValue(DiamondIconProperty, value);
    }

    public ICommand? PrevKeyframeCommand
    {
        get => GetValue(PrevKeyframeCommandProperty);
        set => SetValue(PrevKeyframeCommandProperty, value);
    }

    public ICommand? ToggleKeyframeCommand
    {
        get => GetValue(ToggleKeyframeCommandProperty);
        set => SetValue(ToggleKeyframeCommandProperty, value);
    }

    public ICommand? NextKeyframeCommand
    {
        get => GetValue(NextKeyframeCommandProperty);
        set => SetValue(NextKeyframeCommandProperty, value);
    }

    public ICommand? ResetCommand
    {
        get => GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    public string? ResetCommandParameter
    {
        get => GetValue(ResetCommandParameterProperty);
        set => SetValue(ResetCommandParameterProperty, value);
    }

    public PrecisionParameterRow()
    {
        InitializeComponent();
        UpdateResetButtonAppearance();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == DefaultValueProperty)
        {
            UpdateResetButtonAppearance();
        }
    }

    private void UpdateResetButtonAppearance()
    {
        if (BtnReset == null) return;
        bool isModified = Math.Abs(Value - DefaultValue) > 0.005;
        BtnReset.Foreground = isModified
            ? new SolidColorBrush(Color.FromRgb(226, 228, 236)) // #E2E4EC bright when modified
            : new SolidColorBrush(Color.FromRgb(78, 82, 96));   // #4E5260 dimmed at default
    }

    private void ExecuteReset()
    {
        Value = DefaultValue;
        if (ResetCommand != null && ResetCommand.CanExecute(ResetCommandParameter))
        {
            ResetCommand.Execute(ResetCommandParameter);
        }
        UpdateResetButtonAppearance();
    }

    private void OnResetButtonClick(object? sender, RoutedEventArgs e)
    {
        ExecuteReset();
    }

    private void OnSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        ExecuteReset();
        e.Handled = true;
    }
}
