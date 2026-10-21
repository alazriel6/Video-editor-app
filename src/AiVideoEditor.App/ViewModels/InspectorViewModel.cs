using Avalonia.Media;
using AiVideoEditor.Core.Models;
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

    // Transform & Crop
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
    private bool _flipHorizontal;

    [ObservableProperty]
    private bool _flipVertical;

    [ObservableProperty]
    private bool _dropShadowEnabled;

    // Video Effects (Section 5.2)
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

    // Color Grading Suite (Section 3 — Normalized Rec.709 Pipeline)
    [ObservableProperty]
    private string _selectedColorFilter = "Rec.709 Reset";

    [ObservableProperty]
    private float _exposure = 0f; // -4.0 to +4.0 EV

    [ObservableProperty]
    private float _brightness = 0f; // -100 to +100

    [ObservableProperty]
    private float _contrast = 0f; // -100 to +100 (0 = neutral)

    [ObservableProperty]
    private float _highlights = 0f; // -100 to +100

    [ObservableProperty]
    private float _shadows = 0f; // -100 to +100

    [ObservableProperty]
    private float _saturation = 100f; // 0 to 200% (100% = neutral)

    [ObservableProperty]
    private float _vibrance = 0f; // -100 to +100

    [ObservableProperty]
    private float _temperature = 0f; // -100 to +100

    [ObservableProperty]
    private float _tint = 0f; // -100 to +100

    [ObservableProperty]
    private float _vignette = 0f; // 0 to 100%

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

    // Transitions (Section 5.1)
    [ObservableProperty]
    private string _selectedTransitionType = "None";

    [ObservableProperty]
    private double _transitionDuration = 0.5;

    // Audio Mixing & DSP Effects (Section 5.3)
    [ObservableProperty]
    private float _gainDb;

    [ObservableProperty]
    private float _pan;

    [ObservableProperty]
    private bool _dialogueIsolateEnabled;

    [ObservableProperty]
    private double _fadeInDuration;

    [ObservableProperty]
    private double _fadeOutDuration;

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

    // Text & Typography Studio (Section 7.2)
    [ObservableProperty]
    private bool _isTextClip;

    [ObservableProperty]
    private string _subtitleText = string.Empty;

    [ObservableProperty]
    private string _selectedTextStyle = "BroadcastBox";

    [ObservableProperty]
    private string _selectedFontFamily = "Inter";

    [ObservableProperty]
    private string _selectedFontWeightStyle = "Bold";

    [ObservableProperty]
    private float _fontSize = 48f;

    [ObservableProperty]
    private float _letterSpacing = 0.5f;

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
    private bool _backgroundBoxEnabled = true;

    [ObservableProperty]
    private string _boxColorHex = "#CC111215";

    [ObservableProperty]
    private float _boxPadding = 16.0f;

    [ObservableProperty]
    private float _boxCornerRadius = 8.0f;

    [ObservableProperty]
    private string _selectedTextAnimation = "None";

    public bool RemoveBackgroundAi
    {
        get => SubjectMatteEnabled;
        set => SubjectMatteEnabled = value;
    }

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
        "None",
        "CrossDissolve",
        "DipToBlack",
        "DipToWhite",
        "SlideLeft",
        "ZoomBlur",
        "FlashCut",
        "Glitch"
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
        "BroadcastBox",
        "YellowCinema",
        "CleanLowerThird",
        "HighContrastStroke"
    };

    public IReadOnlyList<string> SystemFontFamilies { get; }

    public IReadOnlyList<string> FontWeightStyles { get; } = new[]
    {
        "Regular",
        "Bold",
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
        "PopBounce",
        "Typewriter",
        "SlideUp"
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
        Action<string>? setStatus = null)
    {
        _onPropertyChangedRefresh = onPropertyChangedRefresh;
        _applyTransitionToAllCallback = applyTransitionToAllCallback;
        _getPlayheadTime = getPlayheadTime ?? (() => 0.0);
        _setPlayheadTime = setPlayheadTime ?? (_ => { });
        _setStatus = setStatus ?? (_ => { });

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
            _suppressUpdates = false;
            return;
        }

        double rel = GetClipRelativeTime();
        // Anti-AI-Slop Rule (Section 1.1): Only display the clean clip name
        ClipTitle = clip.Label;

        Scale = clip.EvaluateScale(rel);
        ScalePercent = (float)Math.Round(Scale * 100.0f, 1);
        PositionX = clip.EvaluatePosX(rel);
        PositionY = clip.EvaluatePosY(rel);
        Rotation = clip.EvaluateRotation(rel);
        Opacity = clip.EvaluateOpacity(rel);
        OpacityPercent = (float)Math.Round(Opacity * 100.0f, 1);

        CropLeft = clip.CropLeft;
        CropRight = clip.CropRight;
        CropTop = clip.CropTop;
        CropBottom = clip.CropBottom;
        FlipHorizontal = clip.FlipHorizontal;
        FlipVertical = clip.FlipVertical;
        DropShadowEnabled = clip.DropShadowEnabled;

        SubjectMatteEnabled = clip.SubjectMatteEnabled;
        CameraShakeAmount = clip.CameraShakeAmount;
        GaussianBlurSigma = clip.GaussianBlurSigma;
        SharpenAmount = clip.SharpenAmount;
        ChromaticAberrationAmount = clip.ChromaticAberrationAmount;
        FilmGrainAmount = clip.FilmGrainAmount;

        SelectedColorFilter = string.IsNullOrEmpty(clip.ColorFilterPreset) || clip.ColorFilterPreset == "Rec709Standard" || clip.ColorFilterPreset == "None" || clip.ColorFilterPreset == "Standard (Rec.709)"
            ? "Rec.709 Reset"
            : clip.ColorFilterPreset;
        Exposure = clip.Exposure;
        Brightness = clip.Brightness;
        Contrast = clip.Contrast;
        Highlights = clip.Highlights;
        Shadows = clip.Shadows;
        Saturation = clip.Saturation <= 3.0f && clip.Saturation > 0.01f ? clip.Saturation * 100f : clip.Saturation;
        Vibrance = clip.Vibrance;
        Temperature = clip.Temperature;
        Tint = clip.Tint;
        Vignette = clip.Vignette <= 1.0f && clip.Vignette > 0.001f ? clip.Vignette * 100f : clip.Vignette;

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

        SelectedTransitionType = clip.InTransition?.Type ?? clip.OutTransition?.Type ?? "None";
        TransitionDuration = clip.InTransition?.Duration ?? clip.OutTransition?.Duration ?? 0.5;

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

        IsTextClip = clip.TextData != null;
        if (clip.TextData != null)
        {
            SubtitleText = clip.TextData.Text;
            SelectedTextStyle = clip.TextData.StylePreset;
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
            BoxPadding = clip.TextData.BoxPadding;
            BoxCornerRadius = clip.TextData.BoxCornerRadius;
            SelectedTextAnimation = clip.TextData.TextAnimation;
        }

        RefreshKeyframeIndicators();
        _suppressUpdates = false;
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
            KeyframeSummaryText = "0 Keyframes (◇)";
            return;
        }

        double rel = GetClipRelativeTime();
        ScaleDiamondIcon = _clip.ScaleTrack.HasKeyframeNear(rel) ? "◆" : "◇";
        PositionDiamondIcon = (_clip.PosXTrack.HasKeyframeNear(rel) || _clip.PosYTrack.HasKeyframeNear(rel)) ? "◆" : "◇";
        RotationDiamondIcon = _clip.RotationTrack.HasKeyframeNear(rel) ? "◆" : "◇";
        OpacityDiamondIcon = _clip.OpacityTrack.HasKeyframeNear(rel) ? "◆" : "◇";
        GainDiamondIcon = _clip.VolumeTrack.HasKeyframeNear(rel) ? "◆" : "◇";

        int totalKf = _clip.ScaleTrack.Keyframes.Count +
                      _clip.PosXTrack.Keyframes.Count +
                      _clip.PosYTrack.Keyframes.Count +
                      _clip.RotationTrack.Keyframes.Count +
                      _clip.OpacityTrack.Keyframes.Count +
                      _clip.VolumeTrack.Keyframes.Count;
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
            _ => null
        };
    }

    [RelayCommand]
    public void ToggleKeyframe(string propertyKey)
    {
        if (_clip == null) return;
        double rel = GetClipRelativeTime();
        var easing = ParseEasing();

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

        RefreshKeyframeIndicators();
        _onPropertyChangedRefresh();
    }

    [RelayCommand]
    public void AddMasterKeyframeAtPlayhead()
    {
        if (_clip == null) return;
        double rel = GetClipRelativeTime();
        var easing = ParseEasing();

        _clip.ScaleTrack.AddOrUpdateKeyframe(rel, Scale, easing);
        _clip.PosXTrack.AddOrUpdateKeyframe(rel, PositionX, easing);
        _clip.PosYTrack.AddOrUpdateKeyframe(rel, PositionY, easing);
        _clip.RotationTrack.AddOrUpdateKeyframe(rel, Rotation, easing);
        _clip.OpacityTrack.AddOrUpdateKeyframe(rel, Opacity, easing);

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
        _clip.ScaleTrack.ClearKeyframes();
        _clip.PosXTrack.ClearKeyframes();
        _clip.PosYTrack.ClearKeyframes();
        _clip.RotationTrack.ClearKeyframes();
        _clip.OpacityTrack.ClearKeyframes();
        _clip.VolumeTrack.ClearKeyframes();
        RefreshKeyframeIndicators();
        _onPropertyChangedRefresh();
        _setStatus("Cleared all keyframes on selected clip.");
    }

    /// <summary>
    /// Individual [↺] Reset button command for every slider row in the Inspector (Section 1.3).
    /// </summary>
    [RelayCommand]
    public void ResetParameter(string paramName)
    {
        if (_clip == null) return;
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
            case "CameraShake": CameraShakeAmount = 0f; break;
            case "GaussianBlur": GaussianBlurSigma = 0f; break;
            case "Sharpen": SharpenAmount = 0f; break;
            case "ChromaticAberration": ChromaticAberrationAmount = 0f; break;
            case "FilmGrain": FilmGrainAmount = 0f; break;
            case "Exposure": Exposure = 0f; break;
            case "Contrast": Contrast = 0f; break;
            case "Highlights": Highlights = 0f; break;
            case "Shadows": Shadows = 0f; break;
            case "Brightness": Brightness = 0f; break;
            case "Temperature": Temperature = 0f; break;
            case "Tint": Tint = 0f; break;
            case "Saturation": Saturation = 100f; break;
            case "Vibrance": Vibrance = 0f; break;
            case "Vignette": Vignette = 0f; break;
            case "GainDb": GainDb = 0f; break;
            case "Pan": Pan = 0f; break;
            case "FadeIn": FadeInDuration = 0.0; break;
            case "FadeOut": FadeOutDuration = 0.0; break;
            case "ReverbDryWet": ReverbDryWet = 0.25f; break;
            case "PitchSemitones": PitchSemitones = 0f; break;
            case "Speed": Speed = 1.0; break;
            case "TransitionDuration": TransitionDuration = 0.5; break;
            case "FontSize": FontSize = 48f; break;
            case "LetterSpacing": LetterSpacing = 0.5f; break;
            case "LineHeight": LineHeight = 1.2f; break;
            case "StrokeWidth": StrokeWidth = 3.0f; break;
            case "ShadowBlur": ShadowBlur = 6.0f; break;
            case "BoxPadding": BoxPadding = 16.0f; break;
            case "BoxCornerRadius": BoxCornerRadius = 8.0f; break;
        }
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

        PositionX = px;
        if (_clip.TextData != null)
        {
            _clip.TextData.VerticalPositionRatio = vRatio;
            PositionY = 0f;
            _onPropertyChangedRefresh();
        }
        else
        {
            PositionY = py;
        }
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
        });
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
    });

    partial void OnPositionYChanged(float value) => UpdateClip(c =>
    {
        c.PositionY = value;
        if (c.PosYTrack.IsAnimated || c.PosYTrack.Keyframes.Count > 0)
            c.PosYTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), value, ParseEasing());
    });

    partial void OnRotationChanged(float value) => UpdateClip(c =>
    {
        c.Rotation = Math.Clamp(value, -180f, 180f);
        if (c.RotationTrack.IsAnimated || c.RotationTrack.Keyframes.Count > 0)
            c.RotationTrack.AddOrUpdateKeyframe(GetClipRelativeTime(), c.Rotation, ParseEasing());
    });

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
        });
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
    });

    partial void OnCropLeftChanged(float value) => UpdateClip(c => c.CropLeft = Math.Clamp(value, 0f, 45f));
    partial void OnCropRightChanged(float value) => UpdateClip(c => c.CropRight = Math.Clamp(value, 0f, 45f));
    partial void OnCropTopChanged(float value) => UpdateClip(c => c.CropTop = Math.Clamp(value, 0f, 45f));
    partial void OnCropBottomChanged(float value) => UpdateClip(c => c.CropBottom = Math.Clamp(value, 0f, 45f));
    partial void OnFlipHorizontalChanged(bool value) => UpdateClip(c => c.FlipHorizontal = value);
    partial void OnFlipVerticalChanged(bool value) => UpdateClip(c => c.FlipVertical = value);
    partial void OnDropShadowEnabledChanged(bool value) => UpdateClip(c => c.DropShadowEnabled = value);

    partial void OnSubjectMatteEnabledChanged(bool value) => UpdateClip(c => c.SubjectMatteEnabled = value);
    partial void OnCameraShakeAmountChanged(float value) => UpdateClip(c => c.CameraShakeAmount = Math.Clamp(value, 0f, 100f));
    partial void OnGaussianBlurSigmaChanged(float value) => UpdateClip(c => c.GaussianBlurSigma = Math.Clamp(value, 0f, 25f));
    partial void OnSharpenAmountChanged(float value) => UpdateClip(c => c.SharpenAmount = Math.Clamp(value, 0f, 100f));
    partial void OnChromaticAberrationAmountChanged(float value) => UpdateClip(c => c.ChromaticAberrationAmount = Math.Clamp(value, 0f, 100f));
    partial void OnFilmGrainAmountChanged(float value) => UpdateClip(c => c.FilmGrainAmount = Math.Clamp(value, 0f, 100f));

    partial void OnSelectedColorFilterChanged(string value) => UpdateClip(c => c.ColorFilterPreset = value);
    partial void OnExposureChanged(float value) => UpdateClip(c => c.Exposure = Math.Clamp(value, -4.0f, 4.0f));
    partial void OnBrightnessChanged(float value) => UpdateClip(c => c.Brightness = Math.Clamp(value, -100f, 100f));
    partial void OnContrastChanged(float value) => UpdateClip(c => c.Contrast = Math.Clamp(value, -100f, 100f));
    partial void OnHighlightsChanged(float value) => UpdateClip(c => c.Highlights = Math.Clamp(value, -100f, 100f));
    partial void OnShadowsChanged(float value) => UpdateClip(c => c.Shadows = Math.Clamp(value, -100f, 100f));
    partial void OnSaturationChanged(float value) => UpdateClip(c => c.Saturation = Math.Clamp(value, 0.0f, 200.0f));
    partial void OnVibranceChanged(float value) => UpdateClip(c => c.Vibrance = Math.Clamp(value, -100f, 100f));
    partial void OnTemperatureChanged(float value) => UpdateClip(c => c.Temperature = Math.Clamp(value, -100f, 100f));
    partial void OnTintChanged(float value) => UpdateClip(c => c.Tint = Math.Clamp(value, -100f, 100f));
    partial void OnVignetteChanged(float value) => UpdateClip(c => c.Vignette = Math.Clamp(value, 0.0f, 100.0f));

    partial void OnLiftWheelRedChanged(double value) => UpdateClip(c => c.LiftWheelRed = (float)Math.Clamp(value, -1.0, 1.0));
    partial void OnLiftWheelGreenChanged(double value) => UpdateClip(c => c.LiftWheelGreen = (float)Math.Clamp(value, -1.0, 1.0));
    partial void OnLiftWheelBlueChanged(double value) => UpdateClip(c => c.LiftWheelBlue = (float)Math.Clamp(value, -1.0, 1.0));
    partial void OnLiftWheelLumChanged(double value) => UpdateClip(c => c.LiftWheelLum = (float)Math.Clamp(value, -1.0, 1.0));

    partial void OnGammaWheelRedChanged(double value) => UpdateClip(c => c.GammaWheelRed = (float)Math.Clamp(value, -1.0, 1.0));
    partial void OnGammaWheelGreenChanged(double value) => UpdateClip(c => c.GammaWheelGreen = (float)Math.Clamp(value, -1.0, 1.0));
    partial void OnGammaWheelBlueChanged(double value) => UpdateClip(c => c.GammaWheelBlue = (float)Math.Clamp(value, -1.0, 1.0));
    partial void OnGammaWheelLumChanged(double value) => UpdateClip(c => c.GammaWheelLum = (float)Math.Clamp(value, -1.0, 1.0));

    partial void OnGainWheelRedChanged(double value) => UpdateClip(c => c.GainWheelRed = (float)Math.Clamp(value, -1.0, 1.0));
    partial void OnGainWheelGreenChanged(double value) => UpdateClip(c => c.GainWheelGreen = (float)Math.Clamp(value, -1.0, 1.0));
    partial void OnGainWheelBlueChanged(double value) => UpdateClip(c => c.GainWheelBlue = (float)Math.Clamp(value, -1.0, 1.0));
    partial void OnGainWheelLumChanged(double value) => UpdateClip(c => c.GainWheelLum = (float)Math.Clamp(value, -1.0, 1.0));

    partial void OnPanChanged(float value) => UpdateClip(c => c.Pan = Math.Clamp(value, -1f, 1f));
    partial void OnDialogueIsolateEnabledChanged(bool value) => UpdateClip(c => c.DialogueIsolateEnabled = value);
    partial void OnFadeInDurationChanged(double value) => UpdateClip(c => c.FadeInDuration = Math.Max(0, value));
    partial void OnFadeOutDurationChanged(double value) => UpdateClip(c => c.FadeOutDuration = Math.Max(0, value));
    partial void OnSelectedEqPresetChanged(string value) => UpdateClip(c => c.EqPreset = value);
    partial void OnCompressorEnabledChanged(bool value) => UpdateClip(c => c.CompressorEnabled = value);
    partial void OnSelectedReverbPresetChanged(string value) => UpdateClip(c => c.ReverbPreset = value);
    partial void OnReverbDryWetChanged(float value) => UpdateClip(c => c.ReverbDryWet = Math.Clamp(value, 0f, 1f));
    partial void OnPitchSemitonesChanged(float value) => UpdateClip(c => c.PitchSemitones = Math.Clamp(value, -12f, 12f));
    partial void OnSpeedChanged(double value) => UpdateClip(c => c.Speed = Math.Clamp(value, 0.25, 4.0));
    partial void OnReversePlaybackChanged(bool value) => UpdateClip(c => c.ReversePlayback = value);

    partial void OnSelectedTransitionTypeChanged(string value) => ApplyClipTransition();
    partial void OnTransitionDurationChanged(double value) => ApplyClipTransition();

    partial void OnSubtitleTextChanged(string value)
    {
        UpdateClip(c =>
        {
            if (c.TextData != null)
            {
                c.TextData.Text = value;
                c.Label = value.Length > 28 ? value[..28] + "..." : value;
            }
        });
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
                c.TextData.BackgroundBoxEnabled = value is "BroadcastBox" or "CleanLowerThird";
            }
        });
    }

    partial void OnSelectedFontFamilyChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.FontFamily = value; });
    partial void OnSelectedFontWeightStyleChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.FontWeightStyle = value; });
    partial void OnFontSizeChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.FontSize = Math.Clamp(value, 16f, 140f); });
    partial void OnLetterSpacingChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.LetterSpacing = Math.Clamp(value, -5f, 30f); });
    partial void OnLineHeightChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.LineHeight = Math.Clamp(value, 0.8f, 2.5f); });
    partial void OnSelectedTextAlignmentChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.Alignment = value; });
    partial void OnTextColorHexChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.TextColorHex = value; });
    partial void OnStrokeEnabledChanged(bool value) => UpdateClip(c => { if (c.TextData != null) c.TextData.StrokeEnabled = value; });
    partial void OnStrokeColorHexChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.StrokeColorHex = value; });
    partial void OnStrokeWidthChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.StrokeWidth = Math.Clamp(value, 0f, 16f); });
    partial void OnShadowEnabledChanged(bool value) => UpdateClip(c => { if (c.TextData != null) c.TextData.ShadowEnabled = value; });
    partial void OnShadowBlurChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.ShadowBlur = Math.Clamp(value, 0f, 24f); });
    partial void OnBackgroundBoxEnabledChanged(bool value) => UpdateClip(c => { if (c.TextData != null) c.TextData.BackgroundBoxEnabled = value; });
    partial void OnBoxColorHexChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.BoxColorHex = value; });
    partial void OnBoxPaddingChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.BoxPadding = Math.Clamp(value, 4f, 40f); });
    partial void OnBoxCornerRadiusChanged(float value) => UpdateClip(c => { if (c.TextData != null) c.TextData.BoxCornerRadius = Math.Clamp(value, 0f, 28f); });
    partial void OnSelectedTextAnimationChanged(string value) => UpdateClip(c => { if (c.TextData != null) c.TextData.TextAnimation = value; });

    private void ApplyClipTransition()
    {
        if (_suppressUpdates || _clip == null) return;
        if (SelectedTransitionType == "None")
        {
            _clip.InTransition = null;
            _clip.OutTransition = null;
        }
        else
        {
            double dur = Math.Clamp(TransitionDuration, 0.1, 2.5);
            _clip.InTransition = new TransitionSpec { Type = SelectedTransitionType, Duration = dur };
            _clip.OutTransition = new TransitionSpec { Type = SelectedTransitionType, Duration = dur };
        }
        _onPropertyChangedRefresh();
    }

    [RelayCommand]
    public void ResetTransforms()
    {
        if (_clip == null) return;
        _clip.ResetAllEffectsAndColor();
        BindClip(_clip);
        _onPropertyChangedRefresh();
        _setStatus("Reset all Transform, Color & Effects on selected clip.");
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
        Exposure = 0f;
        Contrast = 0f;
        Highlights = 0f;
        Shadows = 0f;
        Brightness = 0f;
        Temperature = 0f;
        Tint = 0f;
        Saturation = 100f;
        Vibrance = 0f;
        Vignette = 0f;
        LiftWheelRed = 0; LiftWheelGreen = 0; LiftWheelBlue = 0; LiftWheelLum = 0;
        GammaWheelRed = 0; GammaWheelGreen = 0; GammaWheelBlue = 0; GammaWheelLum = 0;
        GainWheelRed = 0; GainWheelGreen = 0; GainWheelBlue = 0; GainWheelLum = 0;
        SelectedColorFilter = "Rec.709 Reset";
        _setStatus("Reset Color Grading & 3-Way Color Wheels to Rec.709 neutral.");
    }

    [RelayCommand]
    public void ApplyCurrentTransitionToAllClips()
    {
        string type = SelectedTransitionType == "None" ? "CrossDissolve" : SelectedTransitionType;
        _applyTransitionToAllCallback(type, Math.Clamp(TransitionDuration, 0.1, 2.0));
    }

    private void UpdateClip(Action<TimelineClip> mutator)
    {
        if (_suppressUpdates || _clip == null) return;
        mutator(_clip);
        RefreshKeyframeIndicators();
        _onPropertyChangedRefresh();
    }
}
