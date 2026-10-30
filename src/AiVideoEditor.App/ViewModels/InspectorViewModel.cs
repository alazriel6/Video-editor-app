using Avalonia.Media;
using AiVideoEditor.Core.Commands;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiVideoEditor.App.ViewModels;

public partial class InspectorViewModel : ViewModelBase
{
    private readonly Action _onPropertyChangedRefresh;
    private readonly Action<string, double> _applyTransitionToAllCallback;
    private readonly Func<double> _getPlayheadTime;
    private readonly Action<double> _setPlayheadTime;
    private readonly Action<string> _setStatus;
    private readonly UndoRedoManager? _undoRedo;
    private readonly Action<string, double>? _insertStandaloneTransitionCallback;
    private readonly Action? _startPlaybackCallback;
    private TimelineClip? _clip;
    private bool _suppressUpdates;

    public TimelineClip? BoundClip => _clip;

    [ObservableProperty]
    private bool _hasSelectedClip;

    [ObservableProperty]
    private string _clipTitle = "No Clip Selected";

    [ObservableProperty]
    private string _keyframeSummaryText = "0 Keyframes (◇)";

    [ObservableProperty]
    private string _selectedEasingType = "Smooth";

    // Sub-Tabs in Inspector: 0=Video & Transform, 1=Color & Curves, 2=Text Studio, 3=Audio, 4=Effects
    [ObservableProperty]
    private int _selectedInspectorTabIndex = 0;

    // Collapsible Accordion Sections
    [ObservableProperty]
    private bool _isTransformExpanded = true;

    [ObservableProperty]
    private bool _isCropExpanded = true;

    [ObservableProperty]
    private bool _isMaskExpanded = true;

    [ObservableProperty]
    private bool _isBasicColorExpanded = true;

    [ObservableProperty]
    private bool _isCurvesExpanded = true;

    [ObservableProperty]
    private bool _isColorWheelsExpanded = true;

    [ObservableProperty]
    private bool _isHslExpanded = false;

    [ObservableProperty]
    private bool _isTextExpanded = true;

    [ObservableProperty]
    private bool _isAudioExpanded = true;

    [ObservableProperty]
    private bool _isVideoEffectsExpanded = true;

    [ObservableProperty]
    private bool _isTransitionsExpanded = true;

    public string TransformArrow => IsTransformExpanded ? "▼" : "▶";
    public string CropArrow => IsCropExpanded ? "▼" : "▶";
    public string MaskArrow => IsMaskExpanded ? "▼" : "▶";
    public string BasicColorArrow => IsBasicColorExpanded ? "▼" : "▶";
    public string CurvesArrow => IsCurvesExpanded ? "▼" : "▶";
    public string ColorWheelsArrow => IsColorWheelsExpanded ? "▼" : "▶";
    public string HslArrow => IsHslExpanded ? "▼" : "▶";
    public string TextArrow => IsTextExpanded ? "▼" : "▶";
    public string AudioArrow => IsAudioExpanded ? "▼" : "▶";
    public string EffectsArrow => IsVideoEffectsExpanded ? "▼" : "▶";
    public string TransitionsArrow => IsTransitionsExpanded ? "▼" : "▶";

    // Diamond Icon Indicators for each property ("◇" vs "◆")
    [ObservableProperty]
    private string _scaleDiamondIcon = "◇";

    [ObservableProperty]
    private string _positionDiamondIcon = "◇";

    [ObservableProperty]
    private string _rotationDiamondIcon = "◇";

    [ObservableProperty]
    private string _opacityDiamondIcon = "◇";

    [ObservableProperty]
    private string _gainDiamondIcon = "◇";

    [ObservableProperty]
    private string _cropLeftDiamondIcon = "◇";

    [ObservableProperty]
    private string _cropRightDiamondIcon = "◇";

    [ObservableProperty]
    private string _cropTopDiamondIcon = "◇";

    [ObservableProperty]
    private string _cropBottomDiamondIcon = "◇";

    // Transform & Universal Crop (0% to 100%)
    [ObservableProperty]
    private float _scale = 1.0f;

    [ObservableProperty]
    private float _scalePercent = 100.0f;

    [ObservableProperty]
    private float _positionX;

    [ObservableProperty]
    private float _positionY;

    [ObservableProperty]
    private float _rotation;

    [ObservableProperty]
    private float _opacity = 1.0f;

    [ObservableProperty]
    private float _opacityPercent = 100.0f;

    [ObservableProperty]
    private float _cropLeft;

    [ObservableProperty]
    private float _cropRight;

    [ObservableProperty]
    private float _cropTop;

    [ObservableProperty]
    private float _cropBottom;

    [ObservableProperty]
    private float _cornerRadius;

    [ObservableProperty]
    private string _selectedBlendMode = "Normal";

    [ObservableProperty]
    private bool _flipHorizontal;

    [ObservableProperty]
    private bool _flipVertical;

    [ObservableProperty]
    private bool _dropShadowEnabled;

    // Bezier & Shape Masking (MaskPath)
    [ObservableProperty]
    private bool _maskEnabled;

    [ObservableProperty]
    private string _selectedMaskShapePreset = "Rectangle";

    [ObservableProperty]
    private float _maskFeather; // 0 to 100 px

    [ObservableProperty]
    private float _maskOpacity = 100f; // 0 to 100%

    [ObservableProperty]
    private bool _invertMask;

    // Video Effects
    [ObservableProperty]
    private bool _subjectMatteEnabled;

    [ObservableProperty]
    private float _cameraShakeAmount;

    [ObservableProperty]
    private float _gaussianBlurSigma;

    [ObservableProperty]
    private float _sharpenAmount;

    [ObservableProperty]
    private float _chromaticAberrationAmount;

    [ObservableProperty]
    private float _filmGrainAmount;

    // Color Grading Suite — Basic Correction & 3D LUT (.cube) + 16-Bit HDR Pipeline
    [ObservableProperty]
    private string _selectedColorFilter = "Rec.709 Reset";

    [ObservableProperty]
    private string _lutFilePath = string.Empty;

    [ObservableProperty]
    private string _lutDisplayName = "None (.cube)";

    [ObservableProperty]
    private float _lutIntensity = 100f; // 0 to 100%

    [ObservableProperty]
    private bool _enable16BitColorPipeline = true;

    [ObservableProperty]
    private float _temperature = 0f; // -100 to +100

    [ObservableProperty]
    private float _tint = 0f; // -100 to +100

    [ObservableProperty]
    private float _exposure = 0f; // -4.0 to +4.0 EV

    [ObservableProperty]
    private float _brightness = 0f; // -100 to +100

    [ObservableProperty]
    private float _contrast = 0f; // -100 to +100

    [ObservableProperty]
    private float _highlights = 0f; // -100 to +100

    [ObservableProperty]
    private float _shadows = 0f; // -100 to +100

    [ObservableProperty]
    private float _whites = 0f; // -100 to +100

    [ObservableProperty]
    private float _blacks = 0f; // -100 to +100

    [ObservableProperty]
    private float _saturation = 100f; // 0 to 200% (100% = neutral)

    [ObservableProperty]
    private float _vibrance = 0f; // -100 to +100

    [ObservableProperty]
    private float _vignette = 0f; // 0 to 100%

    // HSL Secondary Sliders (8 Color Ranges: 0=Red, 1=Orange/Skin, 2=Yellow, 3=Green, 4=Aqua, 5=Blue, 6=Purple, 7=Magenta)
    [ObservableProperty]
    private int _selectedHslChannelIndex = 0;

    [ObservableProperty]
    private string _selectedHslChannelName = "Red";

    [ObservableProperty]
    private float _activeHslHue = 0f;

    [ObservableProperty]
    private float _activeHslSat = 0f;

    [ObservableProperty]
    private float _activeHslLum = 0f;

    // 3-Way Color Wheels (Lift, Gamma, Gain in -1.0 .. +1.0)
    [ObservableProperty]
    private double _liftWheelRed;

    [ObservableProperty]
    private double _liftWheelGreen;

    [ObservableProperty]
    private double _liftWheelBlue;

    [ObservableProperty]
    private double _liftWheelLum;

    [ObservableProperty]
    private double _gammaWheelRed;

    [ObservableProperty]
    private double _gammaWheelGreen;

    [ObservableProperty]
    private double _gammaWheelBlue;

    [ObservableProperty]
    private double _gammaWheelLum;

    [ObservableProperty]
    private double _gainWheelRed;

    [ObservableProperty]
    private double _gainWheelGreen;

    [ObservableProperty]
    private double _gainWheelBlue;

    [ObservableProperty]
    private double _gainWheelLum;

    // Transitions (Standalone Transition Blocks + Clip Transitions)
    [ObservableProperty]
    private bool _isTransitionClip;

    [ObservableProperty]
    private string _selectedTransitionType = "CrossDissolve";

    [ObservableProperty]
    private double _transitionStart;

    [ObservableProperty]
    private double _transitionDuration = 0.8;

    [ObservableProperty]
    private double _transitionEnd = 0.8;

    [ObservableProperty]
    private double _transitionSpeed = 1.0;

    [ObservableProperty]
    private string _selectedTransitionEasing = "EaseInOut";

    [ObservableProperty]
    private string _selectedTransitionDirection = "Left";

    [ObservableProperty]
    private float _transitionStartOpacity = 0.0f;

    [ObservableProperty]
    private float _transitionEndOpacity = 100.0f;

    public bool IsTransitionDirectionSupported =>
        SelectedTransitionType is "Wipe" or "DirectionalWipe" or "WipeLeft" or "Slide" or "Push" or "PushLeft" or "SlideLeft" or "PushRight" or "PushUp" or "PushDown";

    // Independent Fade In (In-Transition)
    [ObservableProperty]
    private bool _fadeInEnabled;

    [ObservableProperty]
    private string _fadeInType = "FadeIn";

    [ObservableProperty]
    private double _fadeInStartOffset;

    [ObservableProperty]
    private double _fadeInDuration = 0.8;

    [ObservableProperty]
    private double _fadeInEndOffset = 0.8;

    [ObservableProperty]
    private double _fadeInSpeed = 1.0;

    [ObservableProperty]
    private string _fadeInEasing = "EaseInOut";

    [ObservableProperty]
    private string _fadeInDirection = "Left";

    [ObservableProperty]
    private float _fadeInStartOpacity = 0.0f;

    [ObservableProperty]
    private float _fadeInEndOpacity = 100.0f;

    public bool IsFadeInDirectionSupported =>
        FadeInType is "Wipe" or "DirectionalWipe" or "WipeLeft" or "Slide" or "Push";

    // Independent Fade Out (Out-Transition)
    [ObservableProperty]
    private bool _fadeOutEnabled;

    [ObservableProperty]
    private string _fadeOutType = "FadeOut";

    [ObservableProperty]
    private double _fadeOutStartOffset;

    [ObservableProperty]
    private double _fadeOutDuration = 0.8;

    [ObservableProperty]
    private double _fadeOutEndOffset = 0.8;

    [ObservableProperty]
    private double _fadeOutSpeed = 1.0;

    [ObservableProperty]
    private string _fadeOutEasing = "EaseInOut";

    [ObservableProperty]
    private string _fadeOutDirection = "Left";

    [ObservableProperty]
    private float _fadeOutStartOpacity = 100.0f;

    [ObservableProperty]
    private float _fadeOutEndOpacity = 0.0f;

    public bool IsFadeOutDirectionSupported =>
        FadeOutType is "Wipe" or "DirectionalWipe" or "WipeLeft" or "Slide" or "Push";

    // Audio Mixing & Studio Effects
    [ObservableProperty]
    private float _gainDb;

    [ObservableProperty]
    private float _pan;

    [ObservableProperty]
    private bool _dialogueIsolateEnabled;

    [ObservableProperty]
    private string _selectedEqPreset = "Flat";

    [ObservableProperty]
    private bool _compressorEnabled;

    [ObservableProperty]
    private string _selectedReverbPreset = "None";

    [ObservableProperty]
    private float _reverbDryWet = 0.25f;

    [ObservableProperty]
    private float _pitchSemitones;

    [ObservableProperty]
    private double _speed = 1.0;

    [ObservableProperty]
    private bool _reversePlayback;

    // Text & Typography Studio (100% Transparent Background by Default)
    [ObservableProperty]
    private bool _isTextClip;

    [ObservableProperty]
    private string _subtitleText = string.Empty;

    [ObservableProperty]
    private string _selectedTextStyle = "CleanFloating";

    [ObservableProperty]
    private string _selectedFontFamily = "Inter";

    [ObservableProperty]
    private string _selectedFontWeightStyle = "Bold";

    [ObservableProperty]
    private float _fontSize = 48f;

    [ObservableProperty]
    private float _letterSpacing = 0.0f;

    [ObservableProperty]
    private float _lineHeight = 1.2f;

    [ObservableProperty]
    private string _selectedTextAlignment = "Center";

    [ObservableProperty]
    private string _textColorHex = "#FFFFFFFF";

    [ObservableProperty]
    private bool _strokeEnabled = true;

    [ObservableProperty]
    private string _strokeColorHex = "#FF000000";

    [ObservableProperty]
    private float _strokeWidth = 3.0f;

    [ObservableProperty]
    private bool _shadowEnabled = true;

    [ObservableProperty]
    private float _shadowBlur = 6.0f;

    [ObservableProperty]
    private bool _backgroundBoxEnabled = false;

    [ObservableProperty]
    private string _boxColorHex = "#CC111215";

    [ObservableProperty]
    private float _backgroundOpacity = 80.0f;

    [ObservableProperty]
    private float _boxPadding = 16.0f;

    [ObservableProperty]
    private float _boxCornerRadius = 8.0f;

    [ObservableProperty]
    private string _selectedTextAnimation = "None";

    public static readonly string[] HslChannelNames =
    {
        "Red",
        "Orange (Skin)",
        "Yellow",
        "Green",
        "Aqua",
        "Blue",
        "Purple",
        "Magenta"
    };

    public IReadOnlyList<string> BlendModeOptions { get; } = new[]
    {
        "Normal",
        "Multiply",
        "Screen",
        "Overlay",
        "SoftLight",
        "HardLight",
        "Add"
    };

    public IReadOnlyList<string> MaskShapePresets { get; } = new[]
    {
        "Rectangle",
        "Ellipse",
        "Diamond"
    };

    public IReadOnlyList<string> EasingOptions { get; } = new[]
    {
        "Linear",
        "Smooth",
        "EaseIn",
        "EaseOut",
        "Hold"
    };

    public IReadOnlyList<string> ColorFilterPresets { get; } = new[]
    {
        "Rec.709 Reset",
        "Teal & Orange",
        "Moody Matte",
        "Golden Hour",
        "Cyberpunk",
        "Kodak 2383",
        "Noir B&W"
    };

    public IReadOnlyList<string> TransitionTypes { get; } = new[]
    {
        "CrossDissolve",
        "FadeIn",
        "FadeOut",
        "DipToBlack",
        "DipToWhite",
        "Wipe",
        "Slide",
        "WhipZoom",
        "Glitch",
        "LumaFade",
        "None"
    };

    public IReadOnlyList<string> FadeInTypes { get; } = new[]
    {
        "FadeIn",
        "CrossDissolve",
        "DipToBlack",
        "DipToWhite",
        "Wipe",
        "Slide",
        "WhipZoom"
    };

    public IReadOnlyList<string> FadeOutTypes { get; } = new[]
    {
        "FadeOut",
        "CrossDissolve",
        "DipToBlack",
        "DipToWhite",
        "Wipe",
        "Slide",
        "WhipZoom"
    };

    public IReadOnlyList<string> TransitionDirections { get; } = new[]
    {
        "Left",
        "Right",
        "Up",
        "Down"
    };

    public IReadOnlyList<string> TransitionEasingOptions { get; } = new[]
    {
        "EaseInOut",
        "Linear",
        "EaseIn",
        "EaseOut"
    };

    public IReadOnlyList<string> EqPresets { get; } = new[]
    {
        "Flat",
        "PodcastVoice",
        "BassBoost",
        "TrebleSparkle"
    };

    public IReadOnlyList<string> ReverbPresets { get; } = new[]
    {
        "None",
        "Room",
        "Studio",
        "Hall"
    };

    public IReadOnlyList<string> TextStyles { get; } = new[]
    {
        "CleanFloating",
        "YellowCinema",
        "HighContrastStroke",
        "BroadcastBox",
        "CleanLowerThird"
    };

    public IReadOnlyList<string> SystemFontFamilies { get; }

    public IReadOnlyList<string> FontWeightStyles { get; } = new[]
    {
        "Regular",
        "Bold",
        "Black",
        "Italic",
        "Bold Italic"
    };

    public IReadOnlyList<string> TextAlignments { get; } = new[]
    {
        "Left",
        "Center",
        "Right"
    };

    public IReadOnlyList<string> TextAnimations { get; } = new[]
    {
        "None",
        "FadeInOut",
        "Pop / Bounce In",
        "Typewriter",
        "Slide In"
    };

    public IReadOnlyList<string> QuickTextColors { get; } = new[]
    {
        "#FFFFFFFF",
        "#FFFACC15",
        "#FF38BDF8",
        "#FF4ADE80",
        "#FFF87171",
        "#FFC084FC"
    };

    public InspectorViewModel(
        Action onPropertyChangedRefresh,
        Action<string, double> applyTransitionToAllCallback,
        Func<double>? getPlayheadTime = null,
        Action<double>? setPlayheadTime = null,
        Action<string>? setStatus = null,
        UndoRedoManager? undoRedo = null,
        Action<string, double>? insertStandaloneTransitionCallback = null,
        Action? startPlaybackCallback = null)
    {
        _onPropertyChangedRefresh = onPropertyChangedRefresh;
        _applyTransitionToAllCallback = applyTransitionToAllCallback;
        _getPlayheadTime = getPlayheadTime ?? (() => 0.0);
        _setPlayheadTime = setPlayheadTime ?? (_ => { });
        _setStatus = setStatus ?? (_ => { });
        _undoRedo = undoRedo;
        _insertStandaloneTransitionCallback = insertStandaloneTransitionCallback;
        _startPlaybackCallback = startPlaybackCallback;

        var fonts = new List<string> { "Inter", "Segoe UI", "Arial", "Impact", "Georgia", "Consolas", "Verdana", "Tahoma", "Trebuchet MS" };
        try
        {
            foreach (var f in FontManager.Current.SystemFonts.Select(x => x.Name).OrderBy(x => x))
            {
                if (!string.IsNullOrWhiteSpace(f) && !fonts.Contains(f, StringComparer.OrdinalIgnoreCase))
                    fonts.Add(f);
            }
        }
        catch { }
        SystemFontFamilies = fonts;
    }

    private double GetClipRelativeTime()
    {
        if (_clip == null) return 0.0;
        return Math.Clamp(_getPlayheadTime() - _clip.TimelineStart, 0.0, Math.Max(0.05, _clip.Duration));
    }

    private EasingType ParseEasing()
    {
        return Enum.TryParse<EasingType>(SelectedEasingType, true, out var e) ? e : EasingType.Smooth;
    }

    public void BindClip(TimelineClip? clip)
    {
        _suppressUpdates = true;
        _clip = clip;
        HasSelectedClip = clip != null;

        if (clip == null)
        {
            ClipTitle = "No Clip Selected";
            KeyframeSummaryText = "0 Keyframes (◇)";
            IsTextClip = false;
            IsTransitionClip = false;
            _suppressUpdates = false;
            return;
        }

        double rel = GetClipRelativeTime();
        ClipTitle = clip.Label;
        IsTransitionClip = clip.Kind == ClipKind.Transition;

        Scale = clip.EvaluateScale(rel);
        ScalePercent = (float)Math.Round(Scale * 100.0f, 1);
        PositionX = clip.EvaluatePosX(rel);
        PositionY = clip.EvaluatePosY(rel);
        Rotation = clip.EvaluateRotation(rel);
        Opacity = clip.EvaluateOpacity(rel);
        OpacityPercent = (float)Math.Round(Opacity * 100.0f, 1);

        CropLeft = clip.EvaluateCropLeft(rel);
        CropRight = clip.EvaluateCropRight(rel);
        CropTop = clip.EvaluateCropTop(rel);
        CropBottom = clip.EvaluateCropBottom(rel);
        CornerRadius = clip.CornerRadius;
        SelectedBlendMode = string.IsNullOrWhiteSpace(clip.BlendMode) ? "Normal" : clip.BlendMode;
        FlipHorizontal = clip.FlipHorizontal;
        FlipVertical = clip.FlipVertical;
        DropShadowEnabled = clip.DropShadowEnabled;

        clip.Mask ??= MaskPath.CreateRectangleDefault();
        MaskEnabled = clip.Mask.IsEnabled;
        SelectedMaskShapePreset = string.IsNullOrWhiteSpace(clip.Mask.ShapePreset) ? "Rectangle" : clip.Mask.ShapePreset;
        MaskFeather = clip.Mask.MaskFeather;
        MaskOpacity = clip.Mask.MaskOpacity;
        InvertMask = clip.Mask.InvertMask;

        SubjectMatteEnabled = clip.SubjectMatteEnabled;
        CameraShakeAmount = clip.CameraShakeAmount;
        GaussianBlurSigma = clip.GaussianBlurSigma;
        SharpenAmount = clip.SharpenAmount;
        ChromaticAberrationAmount = clip.ChromaticAberrationAmount;
        FilmGrainAmount = clip.FilmGrainAmount;

        SelectedColorFilter = string.IsNullOrEmpty(clip.ColorFilterPreset) || clip.ColorFilterPreset == "Rec709Standard" || clip.ColorFilterPreset == "None" || clip.ColorFilterPreset == "Standard (Rec.709)"
            ? "Rec.709 Reset"
            : clip.ColorFilterPreset;
        LutFilePath = clip.LutFilePath ?? string.Empty;
        LutDisplayName = string.IsNullOrWhiteSpace(clip.LutFilePath)
            ? "None (.cube)"
            : Path.GetFileName(clip.LutFilePath);
        LutIntensity = Math.Clamp(clip.LutIntensity, 0f, 100f);
        Enable16BitColorPipeline = clip.Enable16BitColorPipeline;
        Temperature = clip.Temperature;
        Tint = clip.Tint;
        Exposure = clip.Exposure;
        Brightness = clip.Brightness;
        Contrast = clip.Contrast;
        Highlights = clip.Highlights;
        Shadows = clip.Shadows;
        Whites = clip.Whites;
        Blacks = clip.Blacks;
        Saturation = clip.Saturation <= 3.0f && clip.Saturation > 0.01f ? clip.Saturation * 100f : clip.Saturation;
        Vibrance = clip.Vibrance;
        Vignette = clip.Vignette <= 1.0f && clip.Vignette > 0.001f ? clip.Vignette * 100f : clip.Vignette;

        SyncHslSlidersFromClip(clip);

        LiftWheelRed = clip.LiftWheelRed;
        LiftWheelGreen = clip.LiftWheelGreen;
        LiftWheelBlue = clip.LiftWheelBlue;
        LiftWheelLum = clip.LiftWheelLum;
        GammaWheelRed = clip.GammaWheelRed;
        GammaWheelGreen = clip.GammaWheelGreen;
        GammaWheelBlue = clip.GammaWheelBlue;
        GammaWheelLum = clip.GammaWheelLum;
        GainWheelRed = clip.GainWheelRed;
        GainWheelGreen = clip.GainWheelGreen;
        GainWheelBlue = clip.GainWheelBlue;
        GainWheelLum = clip.GainWheelLum;

        if (clip.Kind == ClipKind.Transition)
        {
            IsTransitionClip = true;
            SelectedTransitionType = string.IsNullOrWhiteSpace(clip.TransitionEffectType) ? "CrossDissolve" : clip.TransitionEffectType;
            TransitionStart = Math.Round(clip.TimelineStart, 3);
            TransitionDuration = Math.Round(clip.Duration, 3);
            TransitionEnd = Math.Round(clip.TimelineStart + clip.Duration, 3);
            TransitionSpeed = clip.TransitionSpeed > 0.01 ? clip.TransitionSpeed : 1.0;
            SelectedTransitionEasing = string.IsNullOrWhiteSpace(clip.TransitionEasing) ? "EaseInOut" : clip.TransitionEasing;
            SelectedTransitionDirection = string.IsNullOrWhiteSpace(clip.TransitionDirection) ? "Left" : clip.TransitionDirection;
            TransitionStartOpacity = clip.TransitionStartOpacity * 100f;
            TransitionEndOpacity = clip.TransitionEndOpacity * 100f;
        }
        else
        {
            IsTransitionClip = false;
            SelectedTransitionType = clip.InTransition?.Type ?? clip.OutTransition?.Type ?? "CrossDissolve";
            TransitionDuration = clip.InTransition?.Duration ?? clip.OutTransition?.Duration ?? 0.8;

            if (clip.InTransition != null)
            {
                FadeInEnabled = clip.InTransition.IsEnabled;
                FadeInType = string.IsNullOrWhiteSpace(clip.InTransition.Type) ? "FadeIn" : clip.InTransition.Type;
                FadeInStartOffset = Math.Round(clip.InTransition.StartOffset, 3);
                FadeInDuration = Math.Round(clip.InTransition.Duration, 3);
                FadeInEndOffset = Math.Round(clip.InTransition.StartOffset + clip.InTransition.Duration, 3);
                FadeInSpeed = clip.InTransition.Speed > 0.01 ? clip.InTransition.Speed : 1.0;
                FadeInEasing = string.IsNullOrWhiteSpace(clip.InTransition.Easing) ? "EaseInOut" : clip.InTransition.Easing;
                FadeInDirection = string.IsNullOrWhiteSpace(clip.InTransition.Direction) ? "Left" : clip.InTransition.Direction;
                FadeInStartOpacity = clip.InTransition.StartOpacity * 100f;
                FadeInEndOpacity = clip.InTransition.EndOpacity * 100f;
            }
            else if (clip.FadeInDuration > 0.01)
            {
                FadeInEnabled = true;
                FadeInType = "FadeIn";
                FadeInStartOffset = 0.0;
                FadeInDuration = Math.Round(clip.FadeInDuration, 3);
                FadeInEndOffset = Math.Round(clip.FadeInDuration, 3);
                FadeInSpeed = 1.0;
                FadeInEasing = "EaseInOut";
                FadeInDirection = "Left";
                FadeInStartOpacity = 0f;
                FadeInEndOpacity = 100f;
            }
            else
            {
                FadeInEnabled = false;
                FadeInType = "FadeIn";
                FadeInStartOffset = 0.0;
                FadeInDuration = 0.8;
                FadeInEndOffset = 0.8;
                FadeInSpeed = 1.0;
                FadeInEasing = "EaseInOut";
                FadeInDirection = "Left";
                FadeInStartOpacity = 0f;
                FadeInEndOpacity = 100f;
            }

            if (clip.OutTransition != null)
            {
                FadeOutEnabled = clip.OutTransition.IsEnabled;
                FadeOutType = string.IsNullOrWhiteSpace(clip.OutTransition.Type) ? "FadeOut" : clip.OutTransition.Type;
                FadeOutStartOffset = Math.Round(clip.OutTransition.StartOffset, 3);
                FadeOutDuration = Math.Round(clip.OutTransition.Duration, 3);
                FadeOutEndOffset = Math.Round(clip.OutTransition.StartOffset + clip.OutTransition.Duration, 3);
                FadeOutSpeed = clip.OutTransition.Speed > 0.01 ? clip.OutTransition.Speed : 1.0;
                FadeOutEasing = string.IsNullOrWhiteSpace(clip.OutTransition.Easing) ? "EaseInOut" : clip.OutTransition.Easing;
                FadeOutDirection = string.IsNullOrWhiteSpace(clip.OutTransition.Direction) ? "Left" : clip.OutTransition.Direction;
                FadeOutStartOpacity = clip.OutTransition.StartOpacity * 100f;
                FadeOutEndOpacity = clip.OutTransition.EndOpacity * 100f;
            }
            else if (clip.FadeOutDuration > 0.01)
            {
                FadeOutEnabled = true;
                FadeOutType = "FadeOut";
                FadeOutStartOffset = 0.0;
                FadeOutDuration = Math.Round(clip.FadeOutDuration, 3);
                FadeOutEndOffset = Math.Round(clip.FadeOutDuration, 3);
                FadeOutSpeed = 1.0;
                FadeOutEasing = "EaseInOut";
                FadeOutDirection = "Left";
                FadeOutStartOpacity = 100f;
                FadeOutEndOpacity = 0f;
            }
            else
            {
                FadeOutEnabled = false;
                FadeOutType = "FadeOut";
                FadeOutStartOffset = 0.0;
                FadeOutDuration = 0.8;
                FadeOutEndOffset = 0.8;
                FadeOutSpeed = 1.0;
                FadeOutEasing = "EaseInOut";
                FadeOutDirection = "Left";
                FadeOutStartOpacity = 100f;
                FadeOutEndOpacity = 0f;
            }
        }

        GainDb = clip.EvaluateGainDb(rel);
        Pan = clip.Pan;
        DialogueIsolateEnabled = clip.DialogueIsolateEnabled;
        FadeInDuration = clip.FadeInDuration;
        FadeOutDuration = clip.FadeOutDuration;
        SelectedEqPreset = string.IsNullOrEmpty(clip.EqPreset) ? "Flat" : clip.EqPreset;
        CompressorEnabled = clip.CompressorEnabled;
        SelectedReverbPreset = string.IsNullOrEmpty(clip.ReverbPreset) ? "None" : clip.ReverbPreset;
        ReverbDryWet = clip.ReverbDryWet;
        PitchSemitones = clip.PitchSemitones;
        Speed = clip.Speed;
        ReversePlayback = clip.ReversePlayback;

        IsTextClip = clip.Kind == ClipKind.Text || clip.TextData != null;
        if (clip.TextData != null)
        {
            SubtitleText = clip.TextData.Text;
            SelectedTextStyle = string.IsNullOrWhiteSpace(clip.TextData.StylePreset) ? "CleanFloating" : clip.TextData.StylePreset;
            SelectedFontFamily = clip.TextData.FontFamily;
            SelectedFontWeightStyle = clip.TextData.FontWeightStyle;
            FontSize = clip.TextData.FontSize;
            LetterSpacing = clip.TextData.LetterSpacing;
            LineHeight = clip.TextData.LineHeight;
            SelectedTextAlignment = clip.TextData.Alignment;
            TextColorHex = clip.TextData.TextColorHex;
            StrokeEnabled = clip.TextData.StrokeEnabled;
            StrokeColorHex = clip.TextData.StrokeColorHex;
            StrokeWidth = clip.TextData.StrokeWidth;
            ShadowEnabled = clip.TextData.ShadowEnabled;
            ShadowBlur = clip.TextData.ShadowBlur;
            BackgroundBoxEnabled = clip.TextData.BackgroundBoxEnabled;
            BoxColorHex = clip.TextData.BoxColorHex;
            BackgroundOpacity = clip.TextData.BackgroundOpacity;
            BoxPadding = clip.TextData.BoxPadding;
            BoxCornerRadius = clip.TextData.BoxCornerRadius;
            SelectedTextAnimation = clip.TextData.TextAnimation;
        }

        RefreshKeyframeIndicators();
        _suppressUpdates = false;
    }

    private void SyncHslSlidersFromClip(TimelineClip clip)
    {
        int idx = Math.Clamp(SelectedHslChannelIndex, 0, 7);
        SelectedHslChannelName = HslChannelNames[idx];
        ActiveHslHue = clip.HslHueShift != null && clip.HslHueShift.Length > idx ? clip.HslHueShift[idx] : 0f;
        ActiveHslSat = clip.HslSatShift != null && clip.HslSatShift.Length > idx ? clip.HslSatShift[idx] : 0f;
        ActiveHslLum = clip.HslLumShift != null && clip.HslLumShift.Length > idx ? clip.HslLumShift[idx] : 0f;
    }

    [RelayCommand]
    public void SelectHslChannel(string indexStr)
    {
        if (int.TryParse(indexStr, out int idx))
        {
            SelectedHslChannelIndex = Math.Clamp(idx, 0, 7);
            if (_clip != null)
            {
                _suppressUpdates = true;
                SyncHslSlidersFromClip(_clip);
                _suppressUpdates = false;
            }
        }
    }

    [RelayCommand]
    public void ResetHslSecondary()
    {
        if (_clip == null) return;
        UpdateClip(c =>
        {
            Array.Clear(c.HslHueShift, 0, c.HslHueShift.Length);
            Array.Clear(c.HslSatShift, 0, c.HslSatShift.Length);
            Array.Clear(c.HslLumShift, 0, c.HslLumShift.Length);
        }, "ResetHsl", "Reset HSL Secondary");
        _suppressUpdates = true;
        ActiveHslHue = 0f;
        ActiveHslSat = 0f;
        ActiveHslLum = 0f;
        _suppressUpdates = false;
        _setStatus("Reset all 8 HSL Secondary color ranges.");
    }

    public void RecordCurveCommit(TimelineClip before, TimelineClip after, string description)
    {
        if (_clip == null || _undoRedo == null) return;
        _undoRedo.SealCoalescing();
        _undoRedo.RecordOrCoalesceClipEdit(_clip, before, after, $"Curve:{Guid.NewGuid()}", description, c =>
        {
            BindClip(c);
            _onPropertyChangedRefresh();
        });
        _setStatus($"{description} (Ctrl+Z to Undo).");
    }

    public void RecordKeyframeGraphCommit(TimelineClip before, TimelineClip after, string description)
    {
        if (_clip == null || _undoRedo == null) return;
        _undoRedo.SealCoalescing();
        _undoRedo.RecordOrCoalesceClipEdit(_clip, before, after, $"KfGraph:{Guid.NewGuid()}", description, c =>
        {
            BindClip(c);
            _onPropertyChangedRefresh();
        });
        SyncPlayheadValues();
        _setStatus($"{description} (Ctrl+Z to Undo).");
    }

    public void SyncPlayheadValues()
    {
        if (_clip == null) return;
        _suppressUpdates = true;
        double rel = GetClipRelativeTime();
        Scale = _clip.EvaluateScale(rel);
        ScalePercent = (float)Math.Round(Scale * 100.0f, 1);
        PositionX = _clip.EvaluatePosX(rel);
        PositionY = _clip.EvaluatePosY(rel);
        Rotation = _clip.EvaluateRotation(rel);
        Opacity = _clip.EvaluateOpacity(rel);
        OpacityPercent = (float)Math.Round(Opacity * 100.0f, 1);
        CropLeft = _clip.EvaluateCropLeft(rel);
        CropRight = _clip.EvaluateCropRight(rel);
        CropTop = _clip.EvaluateCropTop(rel);
        CropBottom = _clip.EvaluateCropBottom(rel);
        GainDb = _clip.EvaluateGainDb(rel);
        RefreshKeyframeIndicators();
        _suppressUpdates = false;
    }

    public void RefreshKeyframeIndicators()
    {
        if (_clip == null)
        {
            ScaleDiamondIcon = "◇";
            PositionDiamondIcon = "◇";
            RotationDiamondIcon = "◇";
            OpacityDiamondIcon = "◇";
            GainDiamondIcon = "◇";
            CropLeftDiamondIcon = "◇";
            CropRightDiamondIcon = "◇";
            CropTopDiamondIcon = "◇";
            CropBottomDiamondIcon = "◇";
            KeyframeSummaryText = "0 Keyframes (◇)";
            return;
        }

        double rel = GetClipRelativeTime();
        ScaleDiamondIcon = _clip.ScaleTrack.HasKeyframeNear(rel) ? "◆" : "◇";
        PositionDiamondIcon = (_clip.PosXTrack.HasKeyframeNear(rel) || _clip.PosYTrack.HasKeyframeNear(rel)) ? "◆" : "◇";
        RotationDiamondIcon = _clip.RotationTrack.HasKeyframeNear(rel) ? "◆" : "◇";
        OpacityDiamondIcon = _clip.OpacityTrack.HasKeyframeNear(rel) ? "◆" : "◇";
        GainDiamondIcon = _clip.VolumeTrack.HasKeyframeNear(rel) ? "◆" : "◇";
        CropLeftDiamondIcon = _clip.CropLeftTrack.HasKeyframeNear(rel) ? "◆" : "◇";
        CropRightDiamondIcon = _clip.CropRightTrack.HasKeyframeNear(rel) ? "◆" : "◇";
        CropTopDiamondIcon = _clip.CropTopTrack.HasKeyframeNear(rel) ? "◆" : "◇";
        CropBottomDiamondIcon = _clip.CropBottomTrack.HasKeyframeNear(rel) ? "◆" : "◇";

        int totalKf = _clip.ScaleTrack.Keyframes.Count +
                      _clip.PosXTrack.Keyframes.Count +
                      _clip.PosYTrack.Keyframes.Count +
                      _clip.RotationTrack.Keyframes.Count +
                      _clip.OpacityTrack.Keyframes.Count +
                      _clip.VolumeTrack.Keyframes.Count +
                      _clip.CropLeftTrack.Keyframes.Count +
                      _clip.CropRightTrack.Keyframes.Count +
                      _clip.CropTopTrack.Keyframes.Count +
                      _clip.CropBottomTrack.Keyframes.Count;
        KeyframeSummaryText = totalKf > 0
            ? $"{totalKf} Keyframe(s) ◆"
            : "Click ◇ to add a keyframe";
    }

    private AnimatableProperty? ResolveTrack(string propertyKey, out double currentVal)
    {
        currentVal = 0;
        if (_clip == null) return null;
        return propertyKey switch
        {
            "Scale" => (_clip.ScaleTrack, currentVal = Scale).ScaleTrack,
            "Position" or "PosX" => (_clip.PosXTrack, currentVal = PositionX).PosXTrack,
            "PosY" => (_clip.PosYTrack, currentVal = PositionY).PosYTrack,
            "Rotation" => (_clip.RotationTrack, currentVal = Rotation).RotationTrack,
            "Opacity" => (_clip.OpacityTrack, currentVal = Opacity).OpacityTrack,
            "Gain" or "Volume" => (_clip.VolumeTrack, currentVal = GainDb).VolumeTrack,
            "CropLeft" => (_clip.CropLeftTrack, currentVal = CropLeft).CropLeftTrack,
            "CropRight" => (_clip.CropRightTrack, currentVal = CropRight).CropRightTrack,
            "CropTop" => (_clip.CropTopTrack, currentVal = CropTop).CropTopTrack,
            "CropBottom" => (_clip.CropBottomTrack, currentVal = CropBottom).CropBottomTrack,
            _ => null
        };
    }

    [RelayCommand]
    public void ToggleKeyframe(string propertyKey)
    {
        if (_clip == null) return;
        double rel = GetClipRelativeTime();
        var easing = ParseEasing();
        var before = _clip.Clone();

        if (propertyKey == "Position")
        {
            bool addedX = _clip.PosXTrack.ToggleKeyframeAt(rel, PositionX, easing);
            _clip.PosYTrack.AddOrUpdateKeyframe(rel, PositionY, easing);
            if (!addedX) _clip.PosYTrack.RemoveKeyframeNear(rel);
            _setStatus(addedX
                ? $"◆ Added Position keyframe at +{rel:0.00}s ({PositionX:0}, {PositionY:0})"
                : $"◇ Removed Position keyframe at +{rel:0.00}s");
        }
        else
        {
            var track = ResolveTrack(propertyKey, out double val);
            if (track != null)
            {
                bool added = track.ToggleKeyframeAt(rel, val, easing);
                _setStatus(added
                    ? $"◆ Added {propertyKey} keyframe at +{rel:0.00}s"
                    : $"◇ Removed {propertyKey} keyframe at +{rel:0.00}s");
            }
        }

        var after = _clip.Clone();
        _undoRedo?.SealCoalescing();
        _undoRedo?.RecordOrCoalesceClipEdit(_clip, before, after, $"ToggleKf:{Guid.NewGuid()}", $"Toggle {propertyKey} Keyframe", c =>
        {
            BindClip(c);
            _onPropertyChangedRefresh();
        });

        RefreshKeyframeIndicators();
        _onPropertyChangedRefresh();
    }

    [RelayCommand]
    public void AddMasterKeyframeAtPlayhead()
    {
        if (_clip == null) return;
        double rel = GetClipRelativeTime();
        var easing = ParseEasing();
        var before = _clip.Clone();

        _clip.ScaleTrack.AddOrUpdateKeyframe(rel, Scale, easing);
        _clip.PosXTrack.AddOrUpdateKeyframe(rel, PositionX, easing);
        _clip.PosYTrack.AddOrUpdateKeyframe(rel, PositionY, easing);
        _clip.RotationTrack.AddOrUpdateKeyframe(rel, Rotation, easing);
        _clip.OpacityTrack.AddOrUpdateKeyframe(rel, Opacity, easing);

        var after = _clip.Clone();
        _undoRedo?.SealCoalescing();
        _undoRedo?.RecordOrCoalesceClipEdit(_clip, before, after, $"MasterKf:{Guid.NewGuid()}", "Add Transform Keyframes", c =>
        {
            BindClip(c);
            _onPropertyChangedRefresh();
        });

        RefreshKeyframeIndicators();
        _onPropertyChangedRefresh();
        _setStatus($"◆ Added Transform keyframes at +{rel:0.00}s.");
    }

    [RelayCommand]
    public void PrevKeyframe(string propertyKey)
    {
        if (_clip == null) return;
        double rel = GetClipRelativeTime();
        var track = ResolveTrack(propertyKey, out _);
        double? prev = track?.GetPreviousKeyframeTime(rel);
        if (prev.HasValue)
        {
            _setPlayheadTime(_clip.TimelineStart + prev.Value);
            SyncPlayheadValues();
            _onPropertyChangedRefresh();
        }
    }

    [RelayCommand]
    public void NextKeyframe(string propertyKey)
    {
        if (_clip == null) return;
        double rel = GetClipRelativeTime();
        var track = ResolveTrack(propertyKey, out _);
        double? next = track?.GetNextKeyframeTime(rel);
        if (next.HasValue)
        {
            _setPlayheadTime(_clip.TimelineStart + next.Value);
            SyncPlayheadValues();
            _onPropertyChangedRefresh();
        }
    }

    [RelayCommand]
    public void ClearAllKeyframes()
    {
        if (_clip == null) return;
        var before = _clip.Clone();
        _clip.ScaleTrack.ClearKeyframes();
        _clip.PosXTrack.ClearKeyframes();
        _clip.PosYTrack.ClearKeyframes();
        _clip.RotationTrack.ClearKeyframes();
        _clip.OpacityTrack.ClearKeyframes();
        _clip.VolumeTrack.ClearKeyframes();
        _clip.CropLeftTrack.ClearKeyframes();
        _clip.CropRightTrack.ClearKeyframes();
        _clip.CropTopTrack.ClearKeyframes();
        _clip.CropBottomTrack.ClearKeyframes();
        var after = _clip.Clone();
        _undoRedo?.SealCoalescing();
        _undoRedo?.RecordOrCoalesceClipEdit(_clip, before, after, $"ClearKf:{Guid.NewGuid()}", "Clear All Keyframes", c =>
        {
            BindClip(c);
            _onPropertyChangedRefresh();
        });

        RefreshKeyframeIndicators();
        _onPropertyChangedRefresh();
        _setStatus("Cleared all keyframes on selected clip.");
    }

    /// <summary>
    /// Individual [↺] Reset button command for every slider row in the Inspector.
    /// </summary>
    [RelayCommand]
    public void ResetParameter(string paramName)
    {
        if (_clip == null) return;
        _undoRedo?.SealCoalescing();
        switch (paramName)
        {
            case "Scale": ScalePercent = 100f; break;
            case "PositionX": PositionX = 0f; break;
            case "PositionY": PositionY = 0f; break;
            case "Rotation": Rotation = 0f; break;
            case "Opacity": OpacityPercent = 100f; break;
            case "CropLeft": CropLeft = 0f; break;
            case "CropRight": CropRight = 0f; break;
            case "CropTop": CropTop = 0f; break;
            case "CropBottom": CropBottom = 0f; break;
            case "CornerRadius": CornerRadius = 0f; break;
            case "MaskFeather": MaskFeather = 0f; break;
            case "MaskOpacity": MaskOpacity = 100f; break;
            case "LutIntensity": LutIntensity = 100f; break;
            case "CameraShake": CameraShakeAmount = 0f; break;
            case "GaussianBlur": GaussianBlurSigma = 0f; break;
            case "Sharpen": SharpenAmount = 0f; break;
            case "ChromaticAberration": ChromaticAberrationAmount = 0f; break;
            case "FilmGrain": FilmGrainAmount = 0f; break;
            case "Exposure": Exposure = 0f; break;
            case "Contrast": Contrast = 0f; break;
            case "Highlights": Highlights = 0f; break;
            case "Shadows": Shadows = 0f; break;
            case "Whites": Whites = 0f; break;
            case "Blacks": Blacks = 0f; break;
            case "Brightness": Brightness = 0f; break;
            case "Temperature": Temperature = 0f; break;
            case "Tint": Tint = 0f; break;
            case "Saturation": Saturation = 100f; break;
            case "Vibrance": Vibrance = 0f; break;
            case "Vignette": Vignette = 0f; break;
            case "HslHue": ActiveHslHue = 0f; break;
            case "HslSat": ActiveHslSat = 0f; break;
            case "HslLum": ActiveHslLum = 0f; break;
            case "GainDb": GainDb = 0f; break;
            case "Pan": Pan = 0f; break;
            case "FadeIn": FadeInDuration = 0.0; break;
            case "FadeOut": FadeOutDuration = 0.0; break;
            case "ReverbDryWet": ReverbDryWet = 0.25f; break;
            case "PitchSemitones": PitchSemitones = 0f; break;
            case "Speed": Speed = 1.0; break;
            case "TransitionDuration": TransitionDuration = 0.8; break;
            case "FontSize": FontSize = 48f; break;
            case "LetterSpacing": LetterSpacing = 0.0f; break;
            case "LineHeight": LineHeight = 1.2f; break;
            case "StrokeWidth": StrokeWidth = 3.0f; break;
            case "ShadowBlur": ShadowBlur = 6.0f; break;
            case "BackgroundOpacity": BackgroundOpacity = 80.0f; break;
            case "BoxPadding": BoxPadding = 16.0f; break;
            case "BoxCornerRadius": BoxCornerRadius = 8.0f; break;
        }
        _undoRedo?.SealCoalescing();
    }

    [RelayCommand]
    public void SelectInspectorTab(string indexStr)
    {
        if (int.TryParse(indexStr, out int idx))
            SelectedInspectorTabIndex = Math.Clamp(idx, 0, 4);
    }

    [RelayCommand]
    public void ToggleSection(string sectionName)
    {
        switch (sectionName)
        {
            case "Transform":
                IsTransformExpanded = !IsTransformExpanded;
                OnPropertyChanged(nameof(TransformArrow));
                break;
            case "Crop":
                IsCropExpanded = !IsCropExpanded;
                OnPropertyChanged(nameof(CropArrow));
                break;
            case "Mask":
                IsMaskExpanded = !IsMaskExpanded;
                OnPropertyChanged(nameof(MaskArrow));
                break;
            case "BasicColor":
                IsBasicColorExpanded = !IsBasicColorExpanded;
                OnPropertyChanged(nameof(BasicColorArrow));
                break;
            case "Curves":
                IsCurvesExpanded = !IsCurvesExpanded;
                OnPropertyChanged(nameof(CurvesArrow));
                break;
            case "ColorWheels":
                IsColorWheelsExpanded = !IsColorWheelsExpanded;
                OnPropertyChanged(nameof(ColorWheelsArrow));
                break;
            case "Hsl":
                IsHslExpanded = !IsHslExpanded;
                OnPropertyChanged(nameof(HslArrow));
                break;
            case "Text":
                IsTextExpanded = !IsTextExpanded;
                OnPropertyChanged(nameof(TextArrow));
                break;
            case "Audio":
                IsAudioExpanded = !IsAudioExpanded;
                OnPropertyChanged(nameof(AudioArrow));
                break;
            case "Effects":
                IsVideoEffectsExpanded = !IsVideoEffectsExpanded;
                OnPropertyChanged(nameof(EffectsArrow));
                break;
            case "Transitions":
                IsTransitionsExpanded = !IsTransitionsExpanded;
                OnPropertyChanged(nameof(TransitionsArrow));
                break;
        }
    }

    [RelayCommand]
    public void ResetSection(string sectionName)
    {
        if (_clip == null) return;
        var before = _clip.Clone();

        switch (sectionName)
        {
            case "Transform":
                Scale = 1.0f; ScalePercent = 100.0f;
                PositionX = 0f; PositionY = 0f;
                Rotation = 0f;
                Opacity = 1.0f; OpacityPercent = 100.0f;
                SelectedBlendMode = "Normal";
                FlipHorizontal = false; FlipVertical = false;
                DropShadowEnabled = false;
                _clip.Scale = 1.0f; _clip.PositionX = 0f; _clip.PositionY = 0f;
                _clip.Rotation = 0f; _clip.Opacity = 1.0f; _clip.BlendMode = "Normal";
                _clip.FlipHorizontal = false; _clip.FlipVertical = false; _clip.DropShadowEnabled = false;
                break;

            case "Crop":
                CropLeft = 0f; CropRight = 0f; CropTop = 0f; CropBottom = 0f; CornerRadius = 0f;
                _clip.CropLeft = 0f; _clip.CropRight = 0f; _clip.CropTop = 0f; _clip.CropBottom = 0f; _clip.CornerRadius = 0f;
                break;

            case "Mask":
                MaskEnabled = false;
                SelectedMaskShapePreset = "Rectangle";
                MaskFeather = 0f;
                MaskOpacity = 100f;
                InvertMask = false;
                _clip.Mask = MaskPath.CreateRectangleDefault();
                break;

            case "BasicColor":
                Exposure = 0f; Contrast = 0f; Highlights = 0f; Shadows = 0f;
                Whites = 0f; Blacks = 0f; Brightness = 0f;
                Temperature = 0f; Tint = 0f; Saturation = 100f; Vibrance = 0f; Vignette = 0f;
                LutFilePath = string.Empty; LutDisplayName = "None (.cube)"; LutIntensity = 100f; Enable16BitColorPipeline = true;
                _clip.Exposure = 0f; _clip.Contrast = 0f; _clip.Highlights = 0f; _clip.Shadows = 0f;
                _clip.Whites = 0f; _clip.Blacks = 0f; _clip.Brightness = 0f;
                _clip.Temperature = 0f; _clip.Tint = 0f; _clip.Saturation = 100f; _clip.Vibrance = 0f; _clip.Vignette = 0f;
                _clip.LutFilePath = string.Empty; _clip.LutIntensity = 100f; _clip.Enable16BitColorPipeline = true;
                break;

            case "Curves":
                _clip.MasterCurvePoints = TimelineClip.CreateDefaultCurve();
                _clip.RedCurvePoints = TimelineClip.CreateDefaultCurve();
                _clip.GreenCurvePoints = TimelineClip.CreateDefaultCurve();
                _clip.BlueCurvePoints = TimelineClip.CreateDefaultCurve();
                break;

            case "ColorWheels":
                LiftWheelRed = 0; LiftWheelGreen = 0; LiftWheelBlue = 0; LiftWheelLum = 0;
                GammaWheelRed = 0; GammaWheelGreen = 0; GammaWheelBlue = 0; GammaWheelLum = 0;
                GainWheelRed = 0; GainWheelGreen = 0; GainWheelBlue = 0; GainWheelLum = 0;
                _clip.LiftWheelRed = 0; _clip.LiftWheelGreen = 0; _clip.LiftWheelBlue = 0; _clip.LiftWheelLum = 0;
                _clip.GammaWheelRed = 0; _clip.GammaWheelGreen = 0; _clip.GammaWheelBlue = 0; _clip.GammaWheelLum = 0;
                _clip.GainWheelRed = 0; _clip.GainWheelGreen = 0; _clip.GainWheelBlue = 0; _clip.GainWheelLum = 0;
                break;

            case "Hsl":
                ResetHslSecondary();
                return;

            case "Audio":
                GainDb = 0f; Pan = 0f; FadeInDuration = 0.0; FadeOutDuration = 0.0;
                SelectedEqPreset = "Flat"; SelectedReverbPreset = "None"; ReverbDryWet = 0.25f;
                CompressorEnabled = false; DialogueIsolateEnabled = false; PitchSemitones = 0f;
                _clip.GainDb = 0f; _clip.Pan = 0f; _clip.FadeInDuration = 0.0; _clip.FadeOutDuration = 0.0;
                _clip.EqPreset = "Flat"; _clip.ReverbPreset = "None"; _clip.ReverbDryWet = 0.25f;
                _clip.CompressorEnabled = false; _clip.DialogueIsolateEnabled = false; _clip.PitchSemitones = 0f;
                break;

            case "Effects":
                CameraShakeAmount = 0f; GaussianBlurSigma = 0f; SharpenAmount = 0f;
                ChromaticAberrationAmount = 0f; FilmGrainAmount = 0f; SubjectMatteEnabled = false;
                _clip.CameraShakeAmount = 0f; _clip.GaussianBlurSigma = 0f; _clip.SharpenAmount = 0f;
                _clip.ChromaticAberrationAmount = 0f; _clip.FilmGrainAmount = 0f; _clip.SubjectMatteEnabled = false;
                break;
        }

        var after = _clip.Clone();
        _undoRedo?.SealCoalescing();
        _undoRedo?.RecordOrCoalesceClipEdit(_clip, before, after, $"ResetSection:{sectionName}", $"Reset {sectionName} Settings", c =>
        {
            BindClip(c);
            _onPropertyChangedRefresh();
        });

        BindClip(_clip);
        _onPropertyChangedRefresh();
        _setStatus($"Reset {sectionName} settings (Ctrl+Z to Undo).");
    }

    [RelayCommand]
    public void SnapTextPosition(string gridSpot)
    {
        if (_clip == null) return;
        var (px, py, vRatio) = gridSpot switch
        {
            "TopLeft" => (-280f, -180f, 0.16f),
            "TopCenter" => (0f, -180f, 0.16f),
            "TopRight" => (280f, -180f, 0.16f),
            "MiddleLeft" => (-280f, 0f, 0.50f),
            "Center" => (0f, 0f, 0.50f),
            "MiddleRight" => (280f, 0f, 0.50f),
            "BottomLeft" => (-280f, 180f, 0.85f),
            "BottomRight" => (280f, 180f, 0.85f),
            _ => (0f, 0f, 0.85f) // BottomCenter
        };

        UpdateClip(c =>
        {
            c.PositionX = px;
            if (c.TextData != null)
            {
                c.TextData.VerticalPositionRatio = vRatio;
                c.PositionY = 0f;
            }
            else
            {
                c.PositionY = py;
            }
        }, $"SnapText:{gridSpot}", $"Snap Position ({gridSpot})");

        _suppressUpdates = true;
        PositionX = _clip.PositionX;
        PositionY = _clip.PositionY;
        _suppressUpdates = false;
    }

    [RelayCommand]
    public void SetTextFillColor(string hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
            TextColorHex = hex;
    }

    partial void OnScaleChanged(float value)
    {
        if (_suppressUpdates) return;
        float clamped = Math.Clamp(value, 0.1f, 4.0f);
        _suppressUpdates = true;
        ScalePercent = (float)Math.Round(clamped * 100.0f, 1);
        _suppressUpdates = false;

        UpdateClip(c =>
        {
            c.Scale = clamped;
            if (c.ScaleTrack.IsAnimated || c.ScaleTrack.Keyframes.Count > 0)
                c.ScaleTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), clamped, ParseEasing());
        }, "Scale", "Adjust Scale");
    }

    partial void OnScalePercentChanged(float value)
    {
        if (_suppressUpdates) return;
        float clampedPercent = Math.Clamp(value, 10.0f, 400.0f);
        Scale = clampedPercent / 100.0f;
    }

    partial void OnPositionXChanged(float value) => UpdateClip(c =>
    {
        c.PositionX = value;
        if (c.PosXTrack.IsAnimated || c.PosXTrack.Keyframes.Count > 0)
            c.PosXTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), value, ParseEasing());
    }, "PositionX", "Adjust Position X");

    partial void OnPositionYChanged(float value) => UpdateClip(c =>
    {
        c.PositionY = value;
        if (c.PosYTrack.IsAnimated || c.PosYTrack.Keyframes.Count > 0)
            c.PosYTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), value, ParseEasing());
    }, "PositionY", "Adjust Position Y");

    partial void OnRotationChanged(float value) => UpdateClip(c =>
    {
        c.Rotation = Math.Clamp(value, -180f, 180f);
        if (c.RotationTrack.IsAnimated || c.RotationTrack.Keyframes.Count > 0)
            c.RotationTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), c.Rotation, ParseEasing());
    }, "Rotation", "Adjust Rotation");

    partial void OnOpacityChanged(float value)
    {
        if (_suppressUpdates) return;
        float clamped = Math.Clamp(value, 0f, 1f);
        _suppressUpdates = true;
        OpacityPercent = (float)Math.Round(clamped * 100.0f, 1);
        _suppressUpdates = false;

        UpdateClip(c =>
        {
            c.Opacity = clamped;
            if (c.OpacityTrack.IsAnimated || c.OpacityTrack.Keyframes.Count > 0)
                c.OpacityTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), clamped, ParseEasing());
        }, "Opacity", "Adjust Opacity");
    }

    partial void OnOpacityPercentChanged(float value)
    {
        if (_suppressUpdates) return;
        float clampedPercent = Math.Clamp(value, 0f, 100f);
        Opacity = clampedPercent / 100.0f;
    }

    partial void OnGainDbChanged(float value) => UpdateClip(c =>
    {
        float clamped = Math.Clamp(value, -60f, 12f);
        c.GainDb = clamped;
        if (c.VolumeTrack.IsAnimated || c.VolumeTrack.Keyframes.Count > 0)
            c.VolumeTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), clamped, ParseEasing());
    }, "GainDb", "Adjust Audio Volume");

    // Universal Crop (0% to 100% for both Video and Text clips!)
    partial void OnCropLeftChanged(float value) => UpdateClip(c =>
    {
        float clamped = Math.Clamp(value, 0f, 100f);
        c.CropLeft = clamped;
        if (c.CropLeftTrack.IsAnimated || c.CropLeftTrack.Keyframes.Count > 0)
            c.CropLeftTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), clamped, ParseEasing());
    }, "CropLeft", "Adjust Crop Left");

    partial void OnCropRightChanged(float value) => UpdateClip(c =>
    {
        float clamped = Math.Clamp(value, 0f, 100f);
        c.CropRight = clamped;
        if (c.CropRightTrack.IsAnimated || c.CropRightTrack.Keyframes.Count > 0)
            c.CropRightTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), clamped, ParseEasing());
    }, "CropRight", "Adjust Crop Right");

    partial void OnCropTopChanged(float value) => UpdateClip(c =>
    {
        float clamped = Math.Clamp(value, 0f, 100f);
        c.CropTop = clamped;
        if (c.CropTopTrack.IsAnimated || c.CropTopTrack.Keyframes.Count > 0)
            c.CropTopTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), clamped, ParseEasing());
    }, "CropTop", "Adjust Crop Top");

    partial void OnCropBottomChanged(float value) => UpdateClip(c =>
    {
        float clamped = Math.Clamp(value, 0f, 100f);
        c.CropBottom = clamped;
        if (c.CropBottomTrack.IsAnimated || c.CropBottomTrack.Keyframes.Count > 0)
            c.CropBottomTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), clamped, ParseEasing());
    }, "CropBottom", "Adjust Crop Bottom");

    partial void OnCornerRadiusChanged(float value) => UpdateClip(c => c.CornerRadius = Math.Clamp(value, 0f, 100f), "CornerRadius", "Adjust Corner Radius");
    partial void OnSelectedBlendModeChanged(string value) => UpdateClip(c => c.BlendMode = value, "BlendMode", "Change Blend Mode");
    partial void OnFlipHorizontalChanged(bool value) => UpdateClip(c => c.FlipHorizontal = value, "FlipH", "Toggle Flip Horizontal");
    partial void OnFlipVerticalChanged(bool value) => UpdateClip(c => c.FlipVertical = value, "FlipV", "Toggle Flip Vertical");
    partial void OnDropShadowEnabledChanged(bool value) => UpdateClip(c => c.DropShadowEnabled = value, "DropShadow", "Toggle Drop Shadow");

    partial void OnMaskEnabledChanged(bool value) => UpdateClip(c =>
    {
        c.Mask ??= MaskPath.CreateRectangleDefault();
        c.Mask.IsEnabled = value;
        if (value && c.Mask.Points.Count < 3)
            c.Mask.ApplyPresetShape(SelectedMaskShapePreset);
    }, "MaskEnabled", "Toggle Clip Mask");

    partial void OnSelectedMaskShapePresetChanged(string value) => UpdateClip(c =>
    {
        c.Mask ??= MaskPath.CreateRectangleDefault();
        c.Mask.ApplyPresetShape(value);
    }, "MaskShape", $"Set Mask Shape ({value})");

    partial void OnMaskFeatherChanged(float value) => UpdateClip(c =>
    {
        c.Mask ??= MaskPath.CreateRectangleDefault();
        c.Mask.MaskFeather = Math.Clamp(value, 0f, 100f);
    }, "MaskFeather", "Adjust Mask Feather");

    partial void OnMaskOpacityChanged(float value) => UpdateClip(c =>
    {
        c.Mask ??= MaskPath.CreateRectangleDefault();
        c.Mask.MaskOpacity = Math.Clamp(value, 0f, 100f);
    }, "MaskOpacity", "Adjust Mask Opacity");

    partial void OnInvertMaskChanged(bool value) => UpdateClip(c =>
    {
        c.Mask ??= MaskPath.CreateRectangleDefault();
        c.Mask.InvertMask = value;
    }, "InvertMask", "Toggle Invert Mask");

    partial void OnLutIntensityChanged(float value) => UpdateClip(c =>
    {
        c.LutIntensity = Math.Clamp(value, 0f, 100f);
    }, "LutIntensity", "Adjust 3D LUT Intensity");

    partial void OnEnable16BitColorPipelineChanged(bool value) => UpdateClip(c =>
    {
        c.Enable16BitColorPipeline = value;
    }, "Enable16BitHdr", "Toggle 16-Bit HDR Color Pipeline");

    public void SetLutFilePath(string filePath)
    {
        if (_clip == null || string.IsNullOrWhiteSpace(filePath)) return;
        UpdateClip(c =>
        {
            c.LutFilePath = filePath;
            if (c.LutIntensity <= 0.01f)
                c.LutIntensity = 100f;
        }, $"SetLut:{Guid.NewGuid()}", $"Load 3D LUT ({Path.GetFileName(filePath)})");

        _suppressUpdates = true;
        LutFilePath = _clip.LutFilePath;
        LutDisplayName = Path.GetFileName(filePath);
        LutIntensity = _clip.LutIntensity;
        _suppressUpdates = false;
        _setStatus($"Loaded 3D LUT: {LutDisplayName} (16-Bit Trilinear)");
    }

    [RelayCommand]
    public void ClearLut()
    {
        if (_clip == null) return;
        UpdateClip(c =>
        {
            c.LutFilePath = string.Empty;
        }, $"ClearLut:{Guid.NewGuid()}", "Clear 3D LUT");

        _suppressUpdates = true;
        LutFilePath = string.Empty;
        LutDisplayName = "None (.cube)";
        _suppressUpdates = false;
        _setStatus("Cleared 3D LUT (.cube) from selected clip.");
    }

    partial void OnSubjectMatteEnabledChanged(bool value) => UpdateClip(c => c.SubjectMatteEnabled = value, "SubjectMatte", "Toggle Remove Background");
    partial void OnCameraShakeAmountChanged(float value) => UpdateClip(c => c.CameraShakeAmount = Math.Clamp(value, 0f, 100f), "CameraShake", "Adjust Camera Shake");
    partial void OnGaussianBlurSigmaChanged(float value) => UpdateClip(c => c.GaussianBlurSigma = Math.Clamp(value, 0f, 25f), "GaussianBlur", "Adjust Blur");
    partial void OnSharpenAmountChanged(float value) => UpdateClip(c => c.SharpenAmount = Math.Clamp(value, 0f, 100f), "Sharpen", "Adjust Sharpen");
    partial void OnChromaticAberrationAmountChanged(float value) => UpdateClip(c => c.ChromaticAberrationAmount = Math.Clamp(value, 0f, 100f), "ChromaticAberration", "Adjust Chromatic Aberration");
    partial void OnFilmGrainAmountChanged(float value) => UpdateClip(c => c.FilmGrainAmount = Math.Clamp(value, 0f, 100f), "FilmGrain", "Adjust Film Grain");

    partial void OnSelectedColorFilterChanged(string value) => UpdateClip(c => c.ColorFilterPreset = value, "ColorFilter", "Change Color Look Preset");
    partial void OnTemperatureChanged(float value) => UpdateClip(c => c.Temperature = Math.Clamp(value, -100f, 100f), "Temperature", "Adjust Temperature");
    partial void OnTintChanged(float value) => UpdateClip(c => c.Tint = Math.Clamp(value, -100f, 100f), "Tint", "Adjust Tint");
    partial void OnExposureChanged(float value) => UpdateClip(c => c.Exposure = Math.Clamp(value, -4.0f, 4.0f), "Exposure", "Adjust Exposure");
    partial void OnBrightnessChanged(float value) => UpdateClip(c => c.Brightness = Math.Clamp(value, -100f, 100f), "Brightness", "Adjust Brightness");
    partial void OnContrastChanged(float value) => UpdateClip(c => c.Contrast = Math.Clamp(value, -100f, 100f), "Contrast", "Adjust Contrast");
    partial void OnHighlightsChanged(float value) => UpdateClip(c => c.Highlights = Math.Clamp(value, -100f, 100f), "Highlights", "Adjust Highlights");
    partial void OnShadowsChanged(float value) => UpdateClip(c => c.Shadows = Math.Clamp(value, -100f, 100f), "Shadows", "Adjust Shadows");
    partial void OnWhitesChanged(float value) => UpdateClip(c => c.Whites = Math.Clamp(value, -100f, 100f), "Whites", "Adjust Whites");
    partial void OnBlacksChanged(float value) => UpdateClip(c => c.Blacks = Math.Clamp(value, -100f, 100f), "Blacks", "Adjust Blacks");
    partial void OnSaturationChanged(float value) => UpdateClip(c => c.Saturation = Math.Clamp(value, 0.0f, 200.0f), "Saturation", "Adjust Saturation");
    partial void OnVibranceChanged(float value) => UpdateClip(c => c.Vibrance = Math.Clamp(value, -100f, 100f), "Vibrance", "Adjust Vibrance");
    partial void OnVignetteChanged(float value) => UpdateClip(c => c.Vignette = Math.Clamp(value, 0.0f, 100.0f), "Vignette", "Adjust Vignette");

    partial void OnActiveHslHueChanged(float value) => UpdateClip(c =>
    {
        int idx = Math.Clamp(SelectedHslChannelIndex, 0, 7);
        c.HslHueShift[idx] = Math.Clamp(value, -100f, 100f);
    }, $"HslHue:{SelectedHslChannelIndex}", $"Adjust {SelectedHslChannelName} Hue");

    partial void OnActiveHslSatChanged(float value) => UpdateClip(c =>
    {
        int idx = Math.Clamp(SelectedHslChannelIndex, 0, 7);
        c.HslSatShift[idx] = Math.Clamp(value, -100f, 100f);
    }, $"HslSat:{SelectedHslChannelIndex}", $"Adjust {SelectedHslChannelName} Saturation");

    partial void OnActiveHslLumChanged(float value) => UpdateClip(c =>
    {
        int idx = Math.Clamp(SelectedHslChannelIndex, 0, 7);
        c.HslLumShift[idx] = Math.Clamp(value, -100f, 100f);
    }, $"HslLum:{SelectedHslChannelIndex}", $"Adjust {SelectedHslChannelName} Luminance");

    partial void OnLiftWheelRedChanged(double value) => UpdateClip(c => c.LiftWheelRed = (float)Math.Clamp(value, -1.0, 1.0), "LiftWheel", "Adjust Lift Color Wheel");
    partial void OnLiftWheelGreenChanged(double value) => UpdateClip(c => c.LiftWheelGreen = (float)Math.Clamp(value, -1.0, 1.0), "LiftWheel", "Adjust Lift Color Wheel");
    partial void OnLiftWheelBlueChanged(double value) => UpdateClip(c => c.LiftWheelBlue = (float)Math.Clamp(value, -1.0, 1.0), "LiftWheel", "Adjust Lift Color Wheel");
    partial void OnLiftWheelLumChanged(double value) => UpdateClip(c => c.LiftWheelLum = (float)Math.Clamp(value, -1.0, 1.0), "LiftLum", "Adjust Lift Luminance");

    partial void OnGammaWheelRedChanged(double value) => UpdateClip(c => c.GammaWheelRed = (float)Math.Clamp(value, -1.0, 1.0), "GammaWheel", "Adjust Gamma Color Wheel");
    partial void OnGammaWheelGreenChanged(double value) => UpdateClip(c => c.GammaWheelGreen = (float)Math.Clamp(value, -1.0, 1.0), "GammaWheel", "Adjust Gamma Color Wheel");
    partial void OnGammaWheelBlueChanged(double value) => UpdateClip(c => c.GammaWheelBlue = (float)Math.Clamp(value, -1.0, 1.0), "GammaWheel", "Adjust Gamma Color Wheel");
    partial void OnGammaWheelLumChanged(double value) => UpdateClip(c => c.GammaWheelLum = (float)Math.Clamp(value, -1.0, 1.0), "GammaLum", "Adjust Gamma Luminance");

    partial void OnGainWheelRedChanged(double value) => UpdateClip(c => c.GainWheelRed = (float)Math.Clamp(value, -1.0, 1.0), "GainWheel", "Adjust Gain Color Wheel");
    partial void OnGainWheelGreenChanged(double value) => UpdateClip(c => c.GainWheelGreen = (float)Math.Clamp(value, -1.0, 1.0), "GainWheel", "Adjust Gain Color Wheel");
    partial void OnGainWheelBlueChanged(double value) => UpdateClip(c => c.GainWheelBlue = (float)Math.Clamp(value, -1.0, 1.0), "GainWheel", "Adjust Gain Color Wheel");
    partial void OnGainWheelLumChanged(double value) => UpdateClip(c => c.GainWheelLum = (float)Math.Clamp(value, -1.0, 1.0), "GainLum", "Adjust Gain Luminance");

    partial void OnPanChanged(float value) => UpdateClip(c => c.Pan = Math.Clamp(value, -1f, 1f), "Pan", "Adjust Audio Pan");
    partial void OnDialogueIsolateEnabledChanged(bool value) => UpdateClip(c => c.DialogueIsolateEnabled = value, "VoiceIsolation", "Toggle Voice Isolation");
    partial void OnSelectedEqPresetChanged(string value) => UpdateClip(c => c.EqPreset = value, "EqPreset", "Change Audio EQ Preset");
    partial void OnCompressorEnabledChanged(bool value) => UpdateClip(c => c.CompressorEnabled = value, "Compressor", "Toggle Audio Compressor");
    partial void OnSelectedReverbPresetChanged(string value) => UpdateClip(c => c.ReverbPreset = value, "ReverbPreset", "Change Reverb Preset");
    partial void OnReverbDryWetChanged(float value) => UpdateClip(c => c.ReverbDryWet = Math.Clamp(value, 0f, 1f), "ReverbDryWet", "Adjust Reverb Mix");
    partial void OnPitchSemitonesChanged(float value) => UpdateClip(c => c.PitchSemitones = Math.Clamp(value, -12f, 12f), "PitchSemitones", "Adjust Audio Pitch");
    partial void OnSpeedChanged(double value) => UpdateClip(c => c.Speed = Math.Clamp(value, 0.25, 4.0), "Speed", "Adjust Playback Speed");
    partial void OnReversePlaybackChanged(bool value) => UpdateClip(c => c.ReversePlayback = value, "Reverse", "Toggle Reverse Playback");

    partial void OnSubtitleTextChanged(string value)
    {
        UpdateClip(c =>
        {
            if (c.TextData != null)
            {
                c.TextData.Text = value;
                c.Label = value.Length > 28 ? value[..28] + "..." : value;
            }
        }, "SubtitleText", "Edit Text Content");
    }

    partial void OnSelectedTextStyleChanged(string value)
    {
        UpdateClip(c =>
        {
            if (c.TextData != null)
            {
                c.TextData.StylePreset = value;
                c.TextData.TextColorHex = value switch
                {
                    "YellowCinema" => "#FFFACC15",
                    _ => "#FFFFFFFF"
                };
                // Only enable background box if the user explicitly picks BroadcastBox or CleanLowerThird
                bool enableBox = value is "BroadcastBox" or "CleanLowerThird";
                c.TextData.BackgroundBoxEnabled = enableBox;
            }
        }, "TextStyle", "Change Text Style Preset");

        if (_clip?.TextData != null)
        {
            _suppressUpdates = true;
            BackgroundBoxEnabled = _clip.TextData.BackgroundBoxEnabled;
            TextColorHex = _clip.TextData.TextColorHex;
            _suppressUpdates = false;
        }
    }

    partial void OnSelectedFontFamilyChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.FontFamily = value; }, "FontFamily", "Change Font Family");
    partial void OnSelectedFontWeightStyleChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.FontWeightStyle = value; }, "FontWeight", "Change Font Style");
    partial void OnFontSizeChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.FontSize = Math.Clamp(value, 10f, 200f); }, "FontSize", "Adjust Font Size");
    partial void OnLetterSpacingChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.LetterSpacing = Math.Clamp(value, -20f, 100f); }, "LetterSpacing", "Adjust Letter Spacing");
    partial void OnLineHeightChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.LineHeight = Math.Clamp(value, 0.5f, 3.0f); }, "LineHeight", "Adjust Line Spacing");
    partial void OnSelectedTextAlignmentChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.Alignment = value; }, "TextAlign", "Change Text Alignment");
    partial void OnTextColorHexChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.TextColorHex = value; }, "TextColor", "Change Text Color");
    partial void OnStrokeEnabledChanged(bool value) => UpdateClip(c => { if (c.TextData != null) c.TextData.StrokeEnabled = value; }, "StrokeEnabled", "Toggle Text Outline");
    partial void OnStrokeColorHexChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.StrokeColorHex = value; }, "StrokeColor", "Change Outline Color");
    partial void OnStrokeWidthChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.StrokeWidth = Math.Clamp(value, 0f, 20f); }, "StrokeWidth", "Adjust Outline Width");
    partial void OnShadowEnabledChanged(bool value) => UpdateClip(c => { if (c.TextData != null) c.TextData.ShadowEnabled = value; }, "ShadowEnabled", "Toggle Text Shadow");
    partial void OnShadowBlurChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.ShadowBlur = Math.Clamp(value, 0f, 30f); }, "ShadowBlur", "Adjust Shadow Blur");
    partial void OnBackgroundBoxEnabledChanged(bool value) => UpdateClip(c => { if (c.TextData != null) c.TextData.BackgroundBoxEnabled = value; }, "BackgroundBox", "Toggle Text Background Box");
    partial void OnBoxColorHexChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.BoxColorHex = value; }, "BoxColor", "Change Background Box Color");
    partial void OnBackgroundOpacityChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.BackgroundOpacity = Math.Clamp(value, 0f, 100f); }, "BackgroundOpacity", "Adjust Background Opacity");
    partial void OnBoxPaddingChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.BoxPadding = Math.Clamp(value, 0f, 60f); }, "BoxPadding", "Adjust Background Padding");
    partial void OnBoxCornerRadiusChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.BoxCornerRadius = Math.Clamp(value, 0f, 40f); }, "BoxCornerRadius", "Adjust Background Corner Radius");
    partial void OnSelectedTextAnimationChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.TextAnimation = value; }, "TextAnimation", "Change Text Animation");

    // Standalone Transition Block Change Handlers
    partial void OnSelectedTransitionTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsTransitionDirectionSupported));
        ApplyStandaloneTransitionChange();
    }

    partial void OnTransitionStartChanged(double value)
    {
        if (_suppressUpdates) return;
        _suppressUpdates = true;
        TransitionEnd = Math.Round(value + TransitionDuration, 3);
        _suppressUpdates = false;
        ApplyStandaloneTransitionChange();
    }

    partial void OnTransitionDurationChanged(double value)
    {
        if (_suppressUpdates) return;
        _suppressUpdates = true;
        double clampedDur = Math.Clamp(value, 0.05, 10.0);
        TransitionEnd = Math.Round(TransitionStart + clampedDur, 3);
        _suppressUpdates = false;
        ApplyStandaloneTransitionChange();
    }

    partial void OnTransitionEndChanged(double value)
    {
        if (_suppressUpdates) return;
        double clampedEnd = Math.Max(TransitionStart + 0.05, value);
        _suppressUpdates = true;
        TransitionDuration = Math.Round(clampedEnd - TransitionStart, 3);
        _suppressUpdates = false;
        ApplyStandaloneTransitionChange();
    }

    partial void OnTransitionSpeedChanged(double value) => ApplyStandaloneTransitionChange();
    partial void OnSelectedTransitionEasingChanged(string value) => ApplyStandaloneTransitionChange();
    partial void OnSelectedTransitionDirectionChanged(string value) => ApplyStandaloneTransitionChange();
    partial void OnTransitionStartOpacityChanged(float value) => ApplyStandaloneTransitionChange();
    partial void OnTransitionEndOpacityChanged(float value) => ApplyStandaloneTransitionChange();

    private void ApplyStandaloneTransitionChange()
    {
        if (_suppressUpdates || _clip == null || _clip.Kind != ClipKind.Transition) return;
        UpdateClip(c =>
        {
            string tType = SelectedTransitionType == "None" ? "CrossDissolve" : SelectedTransitionType;
            c.TransitionEffectType = tType;
            c.TimelineStart = Math.Max(0, Math.Round(TransitionStart, 3));
            c.Duration = Math.Clamp(Math.Round(TransitionDuration, 3), 0.05, 10.0);
            c.TransitionSpeed = Math.Clamp(TransitionSpeed, 0.25, 4.0);
            c.TransitionEasing = SelectedTransitionEasing;
            c.TransitionDirection = SelectedTransitionDirection;
            c.TransitionStartOpacity = Math.Clamp(TransitionStartOpacity / 100f, 0f, 1f);
            c.TransitionEndOpacity = Math.Clamp(TransitionEndOpacity / 100f, 0f, 1f);
            c.Label = TimelineService.FormatTransitionTitle(tType);
        }, "TransitionBlockEdit", "Edit Transition Properties");
    }

    // Fade In (In-Transition) Change Handlers
    partial void OnFadeInEnabledChanged(bool value) => ApplyFadeInChange();
    partial void OnFadeInTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsFadeInDirectionSupported));
        ApplyFadeInChange();
    }

    partial void OnFadeInStartOffsetChanged(double value)
    {
        if (_suppressUpdates) return;
        _suppressUpdates = true;
        FadeInEndOffset = Math.Round(Math.Max(0, value) + FadeInDuration, 3);
        _suppressUpdates = false;
        ApplyFadeInChange();
    }

    partial void OnFadeInDurationChanged(double value)
    {
        if (_suppressUpdates) return;
        double maxDur = _clip != null ? Math.Max(0.05, _clip.Duration) : 10.0;
        double clamped = Math.Clamp(value, 0.05, maxDur);
        _suppressUpdates = true;
        FadeInEndOffset = Math.Round(FadeInStartOffset + clamped, 3);
        _suppressUpdates = false;
        ApplyFadeInChange();
    }

    partial void OnFadeInEndOffsetChanged(double value)
    {
        if (_suppressUpdates) return;
        double clampedEnd = Math.Max(FadeInStartOffset + 0.05, value);
        _suppressUpdates = true;
        FadeInDuration = Math.Round(clampedEnd - FadeInStartOffset, 3);
        _suppressUpdates = false;
        ApplyFadeInChange();
    }

    partial void OnFadeInSpeedChanged(double value) => ApplyFadeInChange();
    partial void OnFadeInEasingChanged(string value) => ApplyFadeInChange();
    partial void OnFadeInDirectionChanged(string value) => ApplyFadeInChange();
    partial void OnFadeInStartOpacityChanged(float value) => ApplyFadeInChange();
    partial void OnFadeInEndOpacityChanged(float value) => ApplyFadeInChange();

    private void ApplyFadeInChange()
    {
        if (_suppressUpdates || _clip == null || _clip.Kind == ClipKind.Transition) return;
        UpdateClip(c =>
        {
            if (!FadeInEnabled || FadeInType == "None")
            {
                c.InTransition = null;
                c.FadeInDuration = 0.0;
            }
            else
            {
                double dur = Math.Clamp(FadeInDuration, 0.05, Math.Max(0.05, c.Duration));
                c.InTransition = new TransitionSpec
                {
                    Type = FadeInType,
                    StartOffset = Math.Max(0, FadeInStartOffset),
                    Duration = dur,
                    Speed = Math.Clamp(FadeInSpeed, 0.25, 4.0),
                    Easing = FadeInEasing,
                    Direction = FadeInDirection,
                    StartOpacity = Math.Clamp(FadeInStartOpacity / 100f, 0f, 1f),
                    EndOpacity = Math.Clamp(FadeInEndOpacity / 100f, 0f, 1f),
                    IsEnabled = true
                };
                c.FadeInDuration = dur;
            }
        }, "FadeInEdit", "Edit Fade In Transition");
    }

    // Fade Out (Out-Transition) Change Handlers
    partial void OnFadeOutEnabledChanged(bool value) => ApplyFadeOutChange();
    partial void OnFadeOutTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsFadeOutDirectionSupported));
        ApplyFadeOutChange();
    }

    partial void OnFadeOutStartOffsetChanged(double value)
    {
        if (_suppressUpdates) return;
        _suppressUpdates = true;
        FadeOutEndOffset = Math.Round(Math.Max(0, value) + FadeOutDuration, 3);
        _suppressUpdates = false;
        ApplyFadeOutChange();
    }

    partial void OnFadeOutDurationChanged(double value)
    {
        if (_suppressUpdates) return;
        double maxDur = _clip != null ? Math.Max(0.05, _clip.Duration) : 10.0;
        double clamped = Math.Clamp(value, 0.05, maxDur);
        _suppressUpdates = true;
        FadeOutEndOffset = Math.Round(FadeOutStartOffset + clamped, 3);
        _suppressUpdates = false;
        ApplyFadeOutChange();
    }

    partial void OnFadeOutEndOffsetChanged(double value)
    {
        if (_suppressUpdates) return;
        double clampedEnd = Math.Max(FadeOutStartOffset + 0.05, value);
        _suppressUpdates = true;
        FadeOutDuration = Math.Round(clampedEnd - FadeOutStartOffset, 3);
        _suppressUpdates = false;
        ApplyFadeOutChange();
    }

    partial void OnFadeOutSpeedChanged(double value) => ApplyFadeOutChange();
    partial void OnFadeOutEasingChanged(string value) => ApplyFadeOutChange();
    partial void OnFadeOutDirectionChanged(string value) => ApplyFadeOutChange();
    partial void OnFadeOutStartOpacityChanged(float value) => ApplyFadeOutChange();
    partial void OnFadeOutEndOpacityChanged(float value) => ApplyFadeOutChange();

    private void ApplyFadeOutChange()
    {
        if (_suppressUpdates || _clip == null || _clip.Kind == ClipKind.Transition) return;
        UpdateClip(c =>
        {
            if (!FadeOutEnabled || FadeOutType == "None")
            {
                c.OutTransition = null;
                c.FadeOutDuration = 0.0;
            }
            else
            {
                double dur = Math.Clamp(FadeOutDuration, 0.05, Math.Max(0.05, c.Duration));
                c.OutTransition = new TransitionSpec
                {
                    Type = FadeOutType,
                    StartOffset = Math.Max(0, FadeOutStartOffset),
                    Duration = dur,
                    Speed = Math.Clamp(FadeOutSpeed, 0.25, 4.0),
                    Easing = FadeOutEasing,
                    Direction = FadeOutDirection,
                    StartOpacity = Math.Clamp(FadeOutStartOpacity / 100f, 0f, 1f),
                    EndOpacity = Math.Clamp(FadeOutEndOpacity / 100f, 0f, 1f),
                    IsEnabled = true
                };
                c.FadeOutDuration = dur;
            }
        }, "FadeOutEdit", "Edit Fade Out Transition");
    }

    [RelayCommand]
    public void ResetTransition()
    {
        if (_clip == null) return;
        _suppressUpdates = true;
        if (_clip.Kind == ClipKind.Transition)
        {
            SelectedTransitionType = "CrossDissolve";
            TransitionDuration = 0.8;
            TransitionEnd = Math.Round(TransitionStart + 0.8, 3);
            TransitionSpeed = 1.0;
            SelectedTransitionEasing = "EaseInOut";
            SelectedTransitionDirection = "Left";
            TransitionStartOpacity = 0.0f;
            TransitionEndOpacity = 100.0f;
            _suppressUpdates = false;
            ApplyStandaloneTransitionChange();
        }
        else
        {
            FadeInEnabled = false;
            FadeOutEnabled = false;
            FadeInDuration = 0.8;
            FadeInEndOffset = 0.8;
            FadeInSpeed = 1.0;
            FadeInEasing = "EaseInOut";
            FadeInDirection = "Left";
            FadeInStartOpacity = 0f;
            FadeInEndOpacity = 100f;

            FadeOutDuration = 0.8;
            FadeOutEndOffset = 0.8;
            FadeOutSpeed = 1.0;
            FadeOutEasing = "EaseInOut";
            FadeOutDirection = "Left";
            FadeOutStartOpacity = 100f;
            FadeOutEndOpacity = 0f;
            _suppressUpdates = false;
            ApplyFadeInChange();
            ApplyFadeOutChange();
        }
        _setStatus("Reset transition settings to defaults.");
    }

    [RelayCommand]
    public void PreviewTransition()
    {
        if (_clip == null) return;
        double targetTime;
        if (_clip.Kind == ClipKind.Transition)
        {
            targetTime = Math.Max(0, _clip.TimelineStart - 0.25);
        }
        else if (FadeInEnabled)
        {
            targetTime = Math.Max(0, _clip.TimelineStart - 0.25);
        }
        else if (FadeOutEnabled)
        {
            targetTime = Math.Max(0, _clip.TimelineStart + _clip.Duration - FadeOutDuration - 0.35);
        }
        else
        {
            targetTime = Math.Max(0, _clip.TimelineStart);
        }
        _setPlayheadTime(targetTime);
        _startPlaybackCallback?.Invoke();
        _setStatus($"Previewing transition at {targetTime:0.00}s...");
    }

    [RelayCommand]
    public void InsertStandaloneTransition(string? typeOverride = null)
    {
        string type = !string.IsNullOrWhiteSpace(typeOverride)
            ? typeOverride
            : (SelectedTransitionType == "None" ? "CrossDissolve" : SelectedTransitionType);
        double dur = Math.Clamp(TransitionDuration, 0.1, 5.0);
        _insertStandaloneTransitionCallback?.Invoke(type, dur);
    }

    [RelayCommand]
    public void ResetTransforms()
    {
        if (_clip == null) return;
        var before = _clip.Clone();
        _clip.ResetAllEffectsAndColor();
        var after = _clip.Clone();
        _undoRedo?.SealCoalescing();
        _undoRedo?.RecordOrCoalesceClipEdit(_clip, before, after, $"ResetAll:{Guid.NewGuid()}", "Reset All Transform & Color", c =>
        {
            BindClip(c);
            _onPropertyChangedRefresh();
        });
        BindClip(_clip);
        _onPropertyChangedRefresh();
        _setStatus("Reset all Transform, Crop, Color & Effects on selected clip.");
    }

    [RelayCommand]
    public void ApplyColorPreset(string presetName)
    {
        if (_clip == null) return;
        if (string.IsNullOrWhiteSpace(presetName) || presetName.Contains("Reset", StringComparison.OrdinalIgnoreCase))
        {
            ResetColorGrading();
            return;
        }
        SelectedColorFilter = presetName;
        _setStatus($"Applied Look Preset: {presetName}");
    }

    [RelayCommand]
    public void ResetColorGrading()
    {
        if (_clip == null) return;
        var before = _clip.Clone();
        _clip.Exposure = 0f;
        _clip.Contrast = 0f;
        _clip.Highlights = 0f;
        _clip.Shadows = 0f;
        _clip.Whites = 0f;
        _clip.Blacks = 0f;
        _clip.Brightness = 0f;
        _clip.Temperature = 0f;
        _clip.Tint = 0f;
        _clip.Saturation = 100f;
        _clip.Vibrance = 0f;
        _clip.Vignette = 0f;
        _clip.LiftWheelRed = 0; _clip.LiftWheelGreen = 0; _clip.LiftWheelBlue = 0; _clip.LiftWheelLum = 0;
        _clip.GammaWheelRed = 0; _clip.GammaWheelGreen = 0; _clip.GammaWheelBlue = 0; _clip.GammaWheelLum = 0;
        _clip.GainWheelRed = 0; _clip.GainWheelGreen = 0; _clip.GainWheelBlue = 0; _clip.GainWheelLum = 0;
        _clip.ResetCurvesAndHsl();
        _clip.ColorFilterPreset = "Rec.709 Reset";
        _clip.LutFilePath = string.Empty;
        _clip.LutIntensity = 100f;
        _clip.Enable16BitColorPipeline = true;
        var after = _clip.Clone();

        _undoRedo?.SealCoalescing();
        _undoRedo?.RecordOrCoalesceClipEdit(_clip, before, after, $"ResetColor:{Guid.NewGuid()}", "Reset Color Grading", c =>
        {
            BindClip(c);
            _onPropertyChangedRefresh();
        });

        BindClip(_clip);
        _onPropertyChangedRefresh();
        _setStatus("Reset Color Grading, Curves, HSL & 3-Way Wheels to Rec.709 neutral.");
    }

    [RelayCommand]
    public void ApplyCurrentTransitionToAllClips()
    {
        string type = SelectedTransitionType == "None" ? "CrossDissolve" : SelectedTransitionType;
        _applyTransitionToAllCallback(type, Math.Clamp(TransitionDuration, 0.1, 3.0));
    }

    private void UpdateClip(Action<TimelineClip> mutator, string coalesceKey = "PropertyEdit", string description = "Modify Clip Property")
    {
        if (_suppressUpdates || _clip == null) return;
        var before = _clip.Clone();
        mutator(_clip);
        var after = _clip.Clone();
        _undoRedo?.RecordOrCoalesceClipEdit(_clip, before, after, coalesceKey, description, c =>
        {
            BindClip(c);
            _onPropertyChangedRefresh();
        });
        RefreshKeyframeIndicators();
        _onPropertyChangedRefresh();
    }
}
