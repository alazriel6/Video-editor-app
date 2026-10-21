using System.Text.Json.Serialization;

namespace AiVideoEditor.Core.Models;

public class TimelineClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MediaAssetId { get; set; }
    public Guid? LinkedClipId { get; set; }
    public string Label { get; set; } = string.Empty;

    // Timeline Position & Duration (seconds)
    public double TimelineStart { get; set; }
    public double Duration { get; set; }

    // Source Trim Range (seconds)
    public double SourceIn { get; set; }
    public double SourceOut { get; set; }
    public double Speed { get; set; } = 1.0;
    public bool ReversePlayback { get; set; } = false;
    public bool IsFreezeFrame { get; set; } = false;
    public double FreezeSourceTimestamp { get; set; } = 0.0;

    // Transform & Cropping
    public float Opacity { get; set; } = 1.0f;
    public float Scale { get; set; } = 1.0f;
    public float PositionX { get; set; } = 0f;
    public float PositionY { get; set; } = 0f;
    public float Rotation { get; set; } = 0f;
    public float CropLeft { get; set; } = 0f;
    public float CropRight { get; set; } = 0f;
    public float CropTop { get; set; } = 0f;
    public float CropBottom { get; set; } = 0f;
    public bool FlipHorizontal { get; set; } = false;
    public bool FlipVertical { get; set; } = false;
    public bool DropShadowEnabled { get; set; } = false;

    // Video Effects Stack (Section 5.2)
    public float CameraShakeAmount { get; set; } = 0f;         // 0 to 100
    public float GaussianBlurSigma { get; set; } = 0f;         // 0 to 30
    public float SharpenAmount { get; set; } = 0f;             // 0 to 100
    public float ChromaticAberrationAmount { get; set; } = 0f; // 0 to 100
    public float FilmGrainAmount { get; set; } = 0f;           // 0 to 100

    // Audio Channel Strip & DSP Rack (Section 5.4)
    public float GainDb { get; set; } = 0.0f;
    public float Pan { get; set; } = 0.0f;
    public bool DialogueIsolateEnabled { get; set; } = false;
    public string EqPreset { get; set; } = "Flat";
    public bool CompressorEnabled { get; set; } = false;
    public string ReverbPreset { get; set; } = "None";
    public float ReverbDryWet { get; set; } = 20f;
    public float PitchSemitones { get; set; } = 0f;

    [JsonIgnore]
    public float Volume
    {
        get => GainDb <= -59.0f ? 0f : MathF.Pow(10f, GainDb / 20f);
        set => GainDb = value <= 0.001f ? -60f : 20f * MathF.Log10(value);
    }

    public double FadeInDuration { get; set; } = 0.0;
    public double FadeOutDuration { get; set; } = 0.0;

    // Color Grading Suite (Section 3.1 — Rec.709 Color Science & 3-Way Color Wheels)
    public string ColorFilterPreset { get; set; } = "Rec.709 Reset";
    public float Exposure { get; set; } = 0.0f;    // -4.0 to +4.0 EV (0 = neutral)
    public float Brightness { get; set; } = 0.0f;  // -100 to +100 (0 = neutral)
    public float Contrast { get; set; } = 0.0f;    // -100 to +100 (0 = neutral 1.0x slope)
    public float Highlights { get; set; } = 0.0f;  // -100 to +100
    public float Shadows { get; set; } = 0.0f;     // -100 to +100
    public float Saturation { get; set; } = 100.0f;// 0% to 200% (100% = neutral 1.0x)
    public float Vibrance { get; set; } = 0.0f;    // -100 to +100
    public float Temperature { get; set; } = 0.0f; // -100 (Cool) to +100 (Warm)
    public float Tint { get; set; } = 0.0f;        // -100 (Green) to +100 (Magenta)
    public float Vignette { get; set; } = 0.0f;    // 0 to 100%

    // 3-Way Color Wheels: Lift (Shadows), Gamma (Midtones), Gain (Highlights)
    public float LiftWheelRed { get; set; } = 0.0f;
    public float LiftWheelGreen { get; set; } = 0.0f;
    public float LiftWheelBlue { get; set; } = 0.0f;
    public float LiftWheelLum { get; set; } = 0.0f;

    public float GammaWheelRed { get; set; } = 0.0f;
    public float GammaWheelGreen { get; set; } = 0.0f;
    public float GammaWheelBlue { get; set; } = 0.0f;
    public float GammaWheelLum { get; set; } = 0.0f;

    public float GainWheelRed { get; set; } = 0.0f;
    public float GainWheelGreen { get; set; } = 0.0f;
    public float GainWheelBlue { get; set; } = 0.0f;
    public float GainWheelLum { get; set; } = 0.0f;

    // Keyframe Animation Tracks (Section 4)
    public AnimatableProperty ScaleTrack { get; set; } = new() { PropertyName = "Scale", DefaultValue = 1.0f };
    public AnimatableProperty PosXTrack { get; set; } = new() { PropertyName = "PosX", DefaultValue = 0.0f };
    public AnimatableProperty PosYTrack { get; set; } = new() { PropertyName = "PosY", DefaultValue = 0.0f };
    public AnimatableProperty RotationTrack { get; set; } = new() { PropertyName = "Rotation", DefaultValue = 0.0f };
    public AnimatableProperty OpacityTrack { get; set; } = new() { PropertyName = "Opacity", DefaultValue = 1.0f };
    public AnimatableProperty VolumeTrack { get; set; } = new() { PropertyName = "Volume", DefaultValue = 0.0f };

    // Remove Background (Subject Matte) & Transitions
    public bool SubjectMatteEnabled { get; set; } = false;

    [JsonIgnore]
    public bool RemoveBackgroundAi
    {
        get => SubjectMatteEnabled;
        set => SubjectMatteEnabled = value;
    }

    public TransitionSpec? InTransition { get; set; }
    public TransitionSpec? OutTransition { get; set; }
    public TextOverlaySpec? TextData { get; set; }

    public float EvaluateScale(double clipLocalTime)
    {
        ScaleTrack.DefaultValue = Scale;
        return ScaleTrack.IsKeyframingEnabled ? ScaleTrack.EvaluateAt(clipLocalTime) : Scale;
    }

    public float EvaluatePosX(double clipLocalTime)
    {
        PosXTrack.DefaultValue = PositionX;
        return PosXTrack.IsKeyframingEnabled ? PosXTrack.EvaluateAt(clipLocalTime) : PositionX;
    }

    public float EvaluatePosY(double clipLocalTime)
    {
        PosYTrack.DefaultValue = PositionY;
        return PosYTrack.IsKeyframingEnabled ? PosYTrack.EvaluateAt(clipLocalTime) : PositionY;
    }

    public float EvaluateRotation(double clipLocalTime)
    {
        RotationTrack.DefaultValue = Rotation;
        return RotationTrack.IsKeyframingEnabled ? RotationTrack.EvaluateAt(clipLocalTime) : Rotation;
    }

    public float EvaluateOpacity(double clipLocalTime)
    {
        OpacityTrack.DefaultValue = Opacity;
        return OpacityTrack.IsKeyframingEnabled ? OpacityTrack.EvaluateAt(clipLocalTime) : Opacity;
    }

    public float EvaluateGainDb(double clipLocalTime)
    {
        VolumeTrack.DefaultValue = GainDb;
        return VolumeTrack.IsKeyframingEnabled ? VolumeTrack.EvaluateAt(clipLocalTime) : GainDb;
    }

    public List<double> GetAllKeyframeRelativeTimes()
    {
        var times = new List<double>();
        if (ScaleTrack.IsKeyframingEnabled) times.AddRange(ScaleTrack.Keyframes.Select(k => k.ClipRelativeTime));
        if (PosXTrack.IsKeyframingEnabled) times.AddRange(PosXTrack.Keyframes.Select(k => k.ClipRelativeTime));
        if (PosYTrack.IsKeyframingEnabled) times.AddRange(PosYTrack.Keyframes.Select(k => k.ClipRelativeTime));
        if (RotationTrack.IsKeyframingEnabled) times.AddRange(RotationTrack.Keyframes.Select(k => k.ClipRelativeTime));
        if (OpacityTrack.IsKeyframingEnabled) times.AddRange(OpacityTrack.Keyframes.Select(k => k.ClipRelativeTime));
        if (VolumeTrack.IsKeyframingEnabled) times.AddRange(VolumeTrack.Keyframes.Select(k => k.ClipRelativeTime));
        return times.Distinct().OrderBy(t => t).ToList();
    }

    public void CopyAttributesFrom(TimelineClip source)
    {
        Scale = source.Scale;
        PositionX = source.PositionX;
        PositionY = source.PositionY;
        Rotation = source.Rotation;
        Opacity = source.Opacity;
        CropLeft = source.CropLeft;
        CropRight = source.CropRight;
        CropTop = source.CropTop;
        CropBottom = source.CropBottom;
        FlipHorizontal = source.FlipHorizontal;
        FlipVertical = source.FlipVertical;
        DropShadowEnabled = source.DropShadowEnabled;

        CameraShakeAmount = source.CameraShakeAmount;
        GaussianBlurSigma = source.GaussianBlurSigma;
        SharpenAmount = source.SharpenAmount;
        ChromaticAberrationAmount = source.ChromaticAberrationAmount;
        FilmGrainAmount = source.FilmGrainAmount;

        ColorFilterPreset = source.ColorFilterPreset;
        Exposure = source.Exposure;
        Brightness = source.Brightness;
        Contrast = source.Contrast;
        Highlights = source.Highlights;
        Shadows = source.Shadows;
        Saturation = source.Saturation;
        Vibrance = source.Vibrance;
        Temperature = source.Temperature;
        Tint = source.Tint;
        Vignette = source.Vignette;

        LiftWheelRed = source.LiftWheelRed;
        LiftWheelGreen = source.LiftWheelGreen;
        LiftWheelBlue = source.LiftWheelBlue;
        LiftWheelLum = source.LiftWheelLum;
        GammaWheelRed = source.GammaWheelRed;
        GammaWheelGreen = source.GammaWheelGreen;
        GammaWheelBlue = source.GammaWheelBlue;
        GammaWheelLum = source.GammaWheelLum;
        GainWheelRed = source.GainWheelRed;
        GainWheelGreen = source.GainWheelGreen;
        GainWheelBlue = source.GainWheelBlue;
        GainWheelLum = source.GainWheelLum;

        GainDb = source.GainDb;
        Pan = source.Pan;
        DialogueIsolateEnabled = source.DialogueIsolateEnabled;
        EqPreset = source.EqPreset;
        CompressorEnabled = source.CompressorEnabled;
        ReverbPreset = source.ReverbPreset;
        ReverbDryWet = source.ReverbDryWet;
        PitchSemitones = source.PitchSemitones;
        SubjectMatteEnabled = source.SubjectMatteEnabled;

        ScaleTrack = source.ScaleTrack.Clone();
        PosXTrack = source.PosXTrack.Clone();
        PosYTrack = source.PosYTrack.Clone();
        RotationTrack = source.RotationTrack.Clone();
        OpacityTrack = source.OpacityTrack.Clone();
        VolumeTrack = source.VolumeTrack.Clone();
    }

    public void ResetAllEffectsAndColor()
    {
        Scale = 1.0f;
        PositionX = 0f;
        PositionY = 0f;
        Rotation = 0f;
        Opacity = 1.0f;
        CropLeft = 0f;
        CropRight = 0f;
        CropTop = 0f;
        CropBottom = 0f;
        FlipHorizontal = false;
        FlipVertical = false;
        DropShadowEnabled = false;

        CameraShakeAmount = 0f;
        GaussianBlurSigma = 0f;
        SharpenAmount = 0f;
        ChromaticAberrationAmount = 0f;
        FilmGrainAmount = 0f;

        ColorFilterPreset = "Rec.709 Reset";
        Exposure = 0f;
        Brightness = 0f;
        Contrast = 0f;
        Highlights = 0f;
        Shadows = 0f;
        Saturation = 100f;
        Vibrance = 0f;
        Temperature = 0f;
        Tint = 0f;
        Vignette = 0f;

        LiftWheelRed = 0f;
        LiftWheelGreen = 0f;
        LiftWheelBlue = 0f;
        LiftWheelLum = 0f;
        GammaWheelRed = 0f;
        GammaWheelGreen = 0f;
        GammaWheelBlue = 0f;
        GammaWheelLum = 0f;
        GainWheelRed = 0f;
        GainWheelGreen = 0f;
        GainWheelBlue = 0f;
        GainWheelLum = 0f;

        SubjectMatteEnabled = false;
        DialogueIsolateEnabled = false;
        EqPreset = "Flat";
        CompressorEnabled = false;
        ReverbPreset = "None";
        PitchSemitones = 0f;
    }

    public TimelineClip Clone()
    {
        return new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaAssetId = MediaAssetId,
            LinkedClipId = LinkedClipId,
            Label = Label,
            TimelineStart = TimelineStart,
            Duration = Duration,
            SourceIn = SourceIn,
            SourceOut = SourceOut,
            Speed = Speed,
            ReversePlayback = ReversePlayback,
            IsFreezeFrame = IsFreezeFrame,
            FreezeSourceTimestamp = FreezeSourceTimestamp,
            Opacity = Opacity,
            GainDb = GainDb,
            Pan = Pan,
            DialogueIsolateEnabled = DialogueIsolateEnabled,
            EqPreset = EqPreset,
            CompressorEnabled = CompressorEnabled,
            ReverbPreset = ReverbPreset,
            ReverbDryWet = ReverbDryWet,
            PitchSemitones = PitchSemitones,
            FadeInDuration = FadeInDuration,
            FadeOutDuration = FadeOutDuration,
            Scale = Scale,
            PositionX = PositionX,
            PositionY = PositionY,
            Rotation = Rotation,
            CropLeft = CropLeft,
            CropRight = CropRight,
            CropTop = CropTop,
            CropBottom = CropBottom,
            FlipHorizontal = FlipHorizontal,
            FlipVertical = FlipVertical,
            DropShadowEnabled = DropShadowEnabled,
            CameraShakeAmount = CameraShakeAmount,
            GaussianBlurSigma = GaussianBlurSigma,
            SharpenAmount = SharpenAmount,
            ChromaticAberrationAmount = ChromaticAberrationAmount,
            FilmGrainAmount = FilmGrainAmount,
            ColorFilterPreset = ColorFilterPreset,
            Exposure = Exposure,
            Brightness = Brightness,
            Contrast = Contrast,
            Highlights = Highlights,
            Shadows = Shadows,
            Saturation = Saturation,
            Vibrance = Vibrance,
            Temperature = Temperature,
            Tint = Tint,
            Vignette = Vignette,
            LiftWheelRed = LiftWheelRed,
            LiftWheelGreen = LiftWheelGreen,
            LiftWheelBlue = LiftWheelBlue,
            LiftWheelLum = LiftWheelLum,
            GammaWheelRed = GammaWheelRed,
            GammaWheelGreen = GammaWheelGreen,
            GammaWheelBlue = GammaWheelBlue,
            GammaWheelLum = GammaWheelLum,
            GainWheelRed = GainWheelRed,
            GainWheelGreen = GainWheelGreen,
            GainWheelBlue = GainWheelBlue,
            GainWheelLum = GainWheelLum,
            ScaleTrack = ScaleTrack.Clone(),
            PosXTrack = PosXTrack.Clone(),
            PosYTrack = PosYTrack.Clone(),
            RotationTrack = RotationTrack.Clone(),
            OpacityTrack = OpacityTrack.Clone(),
            VolumeTrack = VolumeTrack.Clone(),
            SubjectMatteEnabled = SubjectMatteEnabled,
            InTransition = InTransition != null ? new TransitionSpec { Type = InTransition.Type, Duration = InTransition.Duration } : null,
            OutTransition = OutTransition != null ? new TransitionSpec { Type = OutTransition.Type, Duration = OutTransition.Duration } : null,
            TextData = TextData?.Clone()
        };
    }
}

public class TextOverlaySpec
{
    public string Text { get; set; } = "Title Text";
    public string FontFamily { get; set; } = "Inter";
    public string FontWeightStyle { get; set; } = "Bold"; // Regular, Bold, Black, Italic
    public float FontSize { get; set; } = 48f;
    public float LetterSpacing { get; set; } = 0f;
    public float LineHeight { get; set; } = 1.2f;
    public string Alignment { get; set; } = "Center"; // Left, Center, Right
    public string TextColorHex { get; set; } = "#FFFFFFFF";
    public bool StrokeEnabled { get; set; } = true;
    public string OutlineColorHex { get; set; } = "#FF000000";
    public float StrokeWidth { get; set; } = 3.0f;
    public bool ShadowEnabled { get; set; } = true;
    public string ShadowColorHex { get; set; } = "#AA000000";
    public float ShadowBlur { get; set; } = 6.0f;
    public float ShadowOffsetX { get; set; } = 2.0f;
    public float ShadowOffsetY { get; set; } = 3.0f;
    public bool BackgroundBoxEnabled { get; set; } = true;
    public string BackgroundColorHex { get; set; } = "#D8111215";
    public float BackgroundOpacity { get; set; } = 85f;
    public float BackgroundCornerRadius { get; set; } = 8f;
    public float BackgroundPaddingX { get; set; } = 20f;
    public float BackgroundPaddingY { get; set; } = 10f;
    public string TextAnimation { get; set; } = "None"; // None, Typewriter, Fade Up By Word, Pop / Bounce In, Slide In, Karaoke Word Highlight
    public string StylePreset { get; set; } = "BroadcastBox";
    public float YOffset { get; set; } = 0f;
    public float VerticalPositionRatio { get; set; } = 0.85f;
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; } = false;

    [JsonIgnore]
    public string StrokeColorHex
    {
        get => OutlineColorHex;
        set => OutlineColorHex = value;
    }

    [JsonIgnore]
    public string BoxColorHex
    {
        get => BackgroundColorHex;
        set => BackgroundColorHex = value;
    }

    [JsonIgnore]
    public float BoxPadding
    {
        get => BackgroundPaddingX;
        set { BackgroundPaddingX = value; BackgroundPaddingY = value * 0.5f; }
    }

    [JsonIgnore]
    public float BoxCornerRadius
    {
        get => BackgroundCornerRadius;
        set => BackgroundCornerRadius = value;
    }

    public TextOverlaySpec Clone() => new()
    {
        Text = Text,
        FontFamily = FontFamily,
        FontWeightStyle = FontWeightStyle,
        FontSize = FontSize,
        LetterSpacing = LetterSpacing,
        LineHeight = LineHeight,
        Alignment = Alignment,
        TextColorHex = TextColorHex,
        StrokeEnabled = StrokeEnabled,
        OutlineColorHex = OutlineColorHex,
        StrokeWidth = StrokeWidth,
        ShadowEnabled = ShadowEnabled,
        ShadowColorHex = ShadowColorHex,
        ShadowBlur = ShadowBlur,
        ShadowOffsetX = ShadowOffsetX,
        ShadowOffsetY = ShadowOffsetY,
        BackgroundBoxEnabled = BackgroundBoxEnabled,
        BackgroundColorHex = BackgroundColorHex,
        BackgroundOpacity = BackgroundOpacity,
        BackgroundCornerRadius = BackgroundCornerRadius,
        BackgroundPaddingX = BackgroundPaddingX,
        BackgroundPaddingY = BackgroundPaddingY,
        TextAnimation = TextAnimation,
        StylePreset = StylePreset,
        YOffset = YOffset,
        Bold = Bold,
        Italic = Italic
    };
}
