using System.Text.Json.Serialization;

namespace AiVideoEditor.Core.Models;

public enum ClipKind
{
    Media,
    Text,
    Transition,
    AdjustmentLayer,
    CompoundClip
}

public class MaskNode
{
    public float X { get; set; }
    public float Y { get; set; }
    public float InTangentX { get; set; }
    public float InTangentY { get; set; }
    public float OutTangentX { get; set; }
    public float OutTangentY { get; set; }

    public MaskNode() { }

    public MaskNode(float x, float y, float inTx = 0f, float inTy = 0f, float outTx = 0f, float outTy = 0f)
    {
        X = x;
        Y = y;
        InTangentX = inTx;
        InTangentY = inTy;
        OutTangentX = outTx;
        OutTangentY = outTy;
    }

    public MaskNode Clone() => new(X, Y, InTangentX, InTangentY, OutTangentX, OutTangentY);
}

public class MaskPath
{
    public bool IsEnabled { get; set; } = false;
    public bool IsClosed { get; set; } = true;
    public string ShapePreset { get; set; } = "Rectangle"; // Rectangle, Ellipse, Diamond, CustomBezier
    public List<MaskNode> Points { get; set; } = new();
    public float MaskFeather { get; set; } = 0f;   // 0 to 100px
    public float MaskOpacity { get; set; } = 100f; // 0 to 100%
    public bool InvertMask { get; set; } = false;

    public static MaskPath CreateRectangleDefault() => new()
    {
        IsEnabled = false,
        IsClosed = true,
        ShapePreset = "Rectangle",
        MaskFeather = 0f,
        MaskOpacity = 100f,
        InvertMask = false,
        Points = new List<MaskNode>
        {
            new(0.15f, 0.15f),
            new(0.85f, 0.15f),
            new(0.85f, 0.85f),
            new(0.15f, 0.85f)
        }
    };

    public static MaskPath CreateEllipseDefault()
    {
        const float r = 0.35f;
        const float k = 0.55228475f * r;
        return new MaskPath
        {
            IsEnabled = true,
            IsClosed = true,
            ShapePreset = "Ellipse",
            MaskFeather = 12f,
            MaskOpacity = 100f,
            InvertMask = false,
            Points = new List<MaskNode>
            {
                new(0.5f, 0.5f - r, -k, 0f, k, 0f),
                new(0.5f + r, 0.5f, 0f, -k, 0f, k),
                new(0.5f, 0.5f + r, k, 0f, -k, 0f),
                new(0.5f - r, 0.5f, 0f, k, 0f, -k)
            }
        };
    }

    public static MaskPath CreateDiamondDefault() => new()
    {
        IsEnabled = true,
        IsClosed = true,
        ShapePreset = "Diamond",
        MaskFeather = 8f,
        MaskOpacity = 100f,
        InvertMask = false,
        Points = new List<MaskNode>
        {
            new(0.50f, 0.10f),
            new(0.90f, 0.50f),
            new(0.50f, 0.90f),
            new(0.10f, 0.50f)
        }
    };

    public void ApplyPresetShape(string preset)
    {
        ShapePreset = preset;
        if (string.Equals(preset, "Ellipse", StringComparison.OrdinalIgnoreCase))
        {
            const float r = 0.35f;
            const float k = 0.55228475f * r;
            Points = new List<MaskNode>
            {
                new(0.5f, 0.5f - r, -k, 0f, k, 0f),
                new(0.5f + r, 0.5f, 0f, -k, 0f, k),
                new(0.5f, 0.5f + r, k, 0f, -k, 0f),
                new(0.5f - r, 0.5f, 0f, k, 0f, -k)
            };
        }
        else if (string.Equals(preset, "Diamond", StringComparison.OrdinalIgnoreCase))
        {
            Points = new List<MaskNode>
            {
                new(0.50f, 0.10f),
                new(0.90f, 0.50f),
                new(0.50f, 0.90f),
                new(0.10f, 0.50f)
            };
        }
        else
        {
            Points = new List<MaskNode>
            {
                new(0.15f, 0.15f),
                new(0.85f, 0.15f),
                new(0.85f, 0.85f),
                new(0.15f, 0.85f)
            };
        }
    }

    public MaskPath Clone() => new()
    {
        IsEnabled = IsEnabled,
        IsClosed = IsClosed,
        ShapePreset = ShapePreset,
        MaskFeather = MaskFeather,
        MaskOpacity = MaskOpacity,
        InvertMask = InvertMask,
        Points = Points.Select(p => p.Clone()).ToList()
    };
}

public enum PropertyType
{
    PositionX,
    PositionY,
    Scale,
    ScaleX,
    ScaleY,
    Rotation,
    Opacity,
    CropLeft,
    CropRight,
    CropTop,
    CropBottom,
    Volume
}

public class CurveControlPoint
{
    public float X { get; set; }
    public float Y { get; set; }

    public CurveControlPoint() { }
    public CurveControlPoint(float x, float y)
    {
        X = x;
        Y = y;
    }
}

public class TimelineClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MediaAssetId { get; set; }
    public Guid? LinkedClipId { get; set; }
    public ClipKind Kind { get; set; } = ClipKind.Media;
    public string Label { get; set; } = string.Empty;

    // Standalone Transition Block properties (Premiere Pro style)
    public string? TransitionEffectType { get; set; }
    public bool IsAudioTransition { get; set; } = false;
    public double TransitionSpeed { get; set; } = 1.0;
    public string TransitionEasing { get; set; } = "EaseInOut";
    public string TransitionDirection { get; set; } = "Left";
    public float TransitionStartOpacity { get; set; } = 0.0f;
    public float TransitionEndOpacity { get; set; } = 1.0f;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTransitionClip
    {
        get => Kind == ClipKind.Transition || !string.IsNullOrEmpty(TransitionEffectType);
        set
        {
            if (value)
            {
                Kind = ClipKind.Transition;
                if (string.IsNullOrEmpty(TransitionEffectType))
                    TransitionEffectType = "Cross Dissolve";
            }
            else
            {
                Kind = ClipKind.Media;
                TransitionEffectType = null;
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? TransitionType
    {
        get => TransitionEffectType;
        set => TransitionEffectType = value;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public double TransitionDuration
    {
        get => Duration;
        set => Duration = value;
    }

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

    // Transform & Cropping (Universal for Video, Image, and Text)
    public float Opacity { get; set; } = 1.0f;
    public float Scale { get; set; } = 1.0f;
    public float ScaleX { get; set; } = 100.0f;
    public float ScaleY { get; set; } = 100.0f;
    public float PositionX { get; set; } = 0f;
    public float PositionY { get; set; } = 0f;
    public float Rotation { get; set; } = 0f;
    public float CropLeft { get; set; } = 0f;   // 0% to 100%
    public float CropRight { get; set; } = 0f;  // 0% to 100%
    public float CropTop { get; set; } = 0f;    // 0% to 100%
    public float CropBottom { get; set; } = 0f; // 0% to 100%
    public float CornerRadius { get; set; } = 0f;
    public string BlendMode { get; set; } = "Normal"; // Normal, Screen, Multiply, Overlay, Add
    public bool FlipHorizontal { get; set; } = false;
    public bool FlipVertical { get; set; } = false;
    public bool DropShadowEnabled { get; set; } = false;

    // Video Effects Stack
    public float CameraShakeAmount { get; set; } = 0f;         // 0 to 100
    public float GaussianBlurSigma { get; set; } = 0f;         // 0 to 30
    public float SharpenAmount { get; set; } = 0f;             // 0 to 100
    public float ChromaticAberrationAmount { get; set; } = 0f; // 0 to 100
    public float FilmGrainAmount { get; set; } = 0f;           // 0 to 100

    // Audio Channel Strip & DSP Rack
    public float GainDb { get; set; } = 0.0f;
    public float Pan { get; set; } = 0.0f;
    public bool DialogueIsolateEnabled { get; set; } = false;
    public string EqPreset { get; set; } = "Flat";
    public bool CompressorEnabled { get; set; } = false;
    public string ReverbPreset { get; set; } = "None";
    public float ReverbDryWet { get; set; } = 0.25f;
    public float PitchSemitones { get; set; } = 0f;

    [JsonIgnore]
    public float Volume
    {
        get => GainDb <= -59.0f ? 0f : MathF.Pow(10f, GainDb / 20f);
        set => GainDb = value <= 0.001f ? -60f : 20f * MathF.Log10(value);
    }

    public double FadeInDuration { get; set; } = 0.0;
    public double FadeOutDuration { get; set; } = 0.0;

    // Color Grading Suite (Rec.709 Color Science, Bezier Curves, HSL Secondary, 3-Way Color Wheels)
    public string ColorFilterPreset { get; set; } = "Rec.709 Reset";
    public float Exposure { get; set; } = 0.0f;    // -4.0 to +4.0 EV (0 = neutral)
    public float Brightness { get; set; } = 0.0f;  // -100 to +100 (0 = neutral)
    public float Contrast { get; set; } = 0.0f;    // -100 to +100 (0 = neutral 1.0x slope around 0.5 pivot)
    public float Highlights { get; set; } = 0.0f;  // -100 to +100
    public float Shadows { get; set; } = 0.0f;     // -100 to +100
    public float Whites { get; set; } = 0.0f;      // -100 to +100
    public float Blacks { get; set; } = 0.0f;      // -100 to +100
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

    // Creative RGB & Luma Bezier Curves (Master, Red, Green, Blue)
    public List<CurveControlPoint> MasterCurvePoints { get; set; } = CreateDefaultCurve();
    public List<CurveControlPoint> RedCurvePoints { get; set; } = CreateDefaultCurve();
    public List<CurveControlPoint> GreenCurvePoints { get; set; } = CreateDefaultCurve();
    public List<CurveControlPoint> BlueCurvePoints { get; set; } = CreateDefaultCurve();

    // HSL Secondary / Color Mixer (8 Hue Ranges: 0=Red, 1=Orange, 2=Yellow, 3=Green, 4=Aqua, 5=Blue, 6=Purple, 7=Magenta)
    public float[] HslHueShift { get; set; } = new float[8];
    public float[] HslSatShift { get; set; } = new float[8];
    public float[] HslLumShift { get; set; } = new float[8];

    public static List<CurveControlPoint> CreateDefaultCurve() => new()
    {
        new CurveControlPoint(0f, 0f),
        new CurveControlPoint(1f, 1f)
    };

    // Keyframe Animation Tracks
    public AnimatableProperty ScaleTrack { get; set; } = new() { PropertyName = "Scale", DefaultValue = 1.0f };
    public AnimatableProperty PosXTrack { get; set; } = new() { PropertyName = "PosX", DefaultValue = 0.0f };
    public AnimatableProperty PosYTrack { get; set; } = new() { PropertyName = "PosY", DefaultValue = 0.0f };
    public AnimatableProperty RotationTrack { get; set; } = new() { PropertyName = "Rotation", DefaultValue = 0.0f };
    public AnimatableProperty OpacityTrack { get; set; } = new() { PropertyName = "Opacity", DefaultValue = 1.0f };
    public AnimatableProperty VolumeTrack { get; set; } = new() { PropertyName = "Volume", DefaultValue = 0.0f };
    public AnimatableProperty CropLeftTrack { get; set; } = new() { PropertyName = "CropLeft", DefaultValue = 0.0f };
    public AnimatableProperty CropRightTrack { get; set; } = new() { PropertyName = "CropRight", DefaultValue = 0.0f };
    public AnimatableProperty CropTopTrack { get; set; } = new() { PropertyName = "CropTop", DefaultValue = 0.0f };
    public AnimatableProperty CropBottomTrack { get; set; } = new() { PropertyName = "CropBottom", DefaultValue = 0.0f };

    // Remove Background (Subject Matte) & Transitions
    public bool SubjectMatteEnabled { get; set; } = false;

    [JsonIgnore]
    public bool RemoveBackgroundAi
    {
        get => SubjectMatteEnabled;
        set => SubjectMatteEnabled = value;
    }

    // 3D LUT (.cube) & 16-bit Half-Float Color Pipeline
    public string LutFilePath { get; set; } = string.Empty;
    public float LutIntensity { get; set; } = 100.0f; // 0% to 100%
    public bool Enable16BitColorPipeline { get; set; } = true;

    // Compound Clip (Nested Sequence) & Bezier Masking
    public List<TimelineTrack> NestedTracks { get; set; } = new();
    public MaskPath Mask { get; set; } = MaskPath.CreateRectangleDefault();

    public TransitionSpec? InTransition { get; set; }
    public TransitionSpec? OutTransition { get; set; }
    public TextOverlaySpec? TextData { get; set; }

    [JsonIgnore]
    public TextOverlaySpec? TextProperties
    {
        get => TextData;
        set => TextData = value;
    }

    public float GetEvaluatedValue(PropertyType prop, double clipLocalTime)
    {
        return prop switch
        {
            PropertyType.PositionX => EvaluatePosX(clipLocalTime),
            PropertyType.PositionY => EvaluatePosY(clipLocalTime),
            PropertyType.Scale => EvaluateScale(clipLocalTime),
            PropertyType.ScaleX => EvaluateScale(clipLocalTime) * 100f,
            PropertyType.ScaleY => EvaluateScale(clipLocalTime) * 100f,
            PropertyType.Rotation => EvaluateRotation(clipLocalTime),
            PropertyType.Opacity => EvaluateOpacity(clipLocalTime),
            PropertyType.CropLeft => EvaluateCropLeft(clipLocalTime),
            PropertyType.CropRight => EvaluateCropRight(clipLocalTime),
            PropertyType.CropTop => EvaluateCropTop(clipLocalTime),
            PropertyType.CropBottom => EvaluateCropBottom(clipLocalTime),
            PropertyType.Volume => EvaluateGainDb(clipLocalTime),
            _ => 0f
        };
    }

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

    public float EvaluateCropLeft(double clipLocalTime)
    {
        CropLeftTrack.DefaultValue = CropLeft;
        return CropLeftTrack.IsKeyframingEnabled ? CropLeftTrack.EvaluateAt(clipLocalTime) : CropLeft;
    }

    public float EvaluateCropRight(double clipLocalTime)
    {
        CropRightTrack.DefaultValue = CropRight;
        return CropRightTrack.IsKeyframingEnabled ? CropRightTrack.EvaluateAt(clipLocalTime) : CropRight;
    }

    public float EvaluateCropTop(double clipLocalTime)
    {
        CropTopTrack.DefaultValue = CropTop;
        return CropTopTrack.IsKeyframingEnabled ? CropTopTrack.EvaluateAt(clipLocalTime) : CropTop;
    }

    public float EvaluateCropBottom(double clipLocalTime)
    {
        CropBottomTrack.DefaultValue = CropBottom;
        return CropBottomTrack.IsKeyframingEnabled ? CropBottomTrack.EvaluateAt(clipLocalTime) : CropBottom;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasAnyKeyframes =>
        (ScaleTrack.IsKeyframingEnabled && ScaleTrack.Keyframes.Count > 0) ||
        (PosXTrack.IsKeyframingEnabled && PosXTrack.Keyframes.Count > 0) ||
        (PosYTrack.IsKeyframingEnabled && PosYTrack.Keyframes.Count > 0) ||
        (RotationTrack.IsKeyframingEnabled && RotationTrack.Keyframes.Count > 0) ||
        (OpacityTrack.IsKeyframingEnabled && OpacityTrack.Keyframes.Count > 0) ||
        (VolumeTrack.IsKeyframingEnabled && VolumeTrack.Keyframes.Count > 0) ||
        (CropLeftTrack.IsKeyframingEnabled && CropLeftTrack.Keyframes.Count > 0) ||
        (CropRightTrack.IsKeyframingEnabled && CropRightTrack.Keyframes.Count > 0) ||
        (CropTopTrack.IsKeyframingEnabled && CropTopTrack.Keyframes.Count > 0) ||
        (CropBottomTrack.IsKeyframingEnabled && CropBottomTrack.Keyframes.Count > 0);

    private static readonly List<double> EmptyKeyframeTimes = new(0);

    public List<double> GetAllKeyframeRelativeTimes()
    {
        if (!HasAnyKeyframes)
            return EmptyKeyframeTimes;

        var times = new List<double>();
        if (ScaleTrack.IsKeyframingEnabled && ScaleTrack.Keyframes.Count > 0)
        {
            for (int i = 0; i < ScaleTrack.Keyframes.Count; i++) times.Add(ScaleTrack.Keyframes[i].ClipRelativeTime);
        }
        if (PosXTrack.IsKeyframingEnabled && PosXTrack.Keyframes.Count > 0)
        {
            for (int i = 0; i < PosXTrack.Keyframes.Count; i++) times.Add(PosXTrack.Keyframes[i].ClipRelativeTime);
        }
        if (PosYTrack.IsKeyframingEnabled && PosYTrack.Keyframes.Count > 0)
        {
            for (int i = 0; i < PosYTrack.Keyframes.Count; i++) times.Add(PosYTrack.Keyframes[i].ClipRelativeTime);
        }
        if (RotationTrack.IsKeyframingEnabled && RotationTrack.Keyframes.Count > 0)
        {
            for (int i = 0; i < RotationTrack.Keyframes.Count; i++) times.Add(RotationTrack.Keyframes[i].ClipRelativeTime);
        }
        if (OpacityTrack.IsKeyframingEnabled && OpacityTrack.Keyframes.Count > 0)
        {
            for (int i = 0; i < OpacityTrack.Keyframes.Count; i++) times.Add(OpacityTrack.Keyframes[i].ClipRelativeTime);
        }
        if (VolumeTrack.IsKeyframingEnabled && VolumeTrack.Keyframes.Count > 0)
        {
            for (int i = 0; i < VolumeTrack.Keyframes.Count; i++) times.Add(VolumeTrack.Keyframes[i].ClipRelativeTime);
        }
        if (CropLeftTrack.IsKeyframingEnabled && CropLeftTrack.Keyframes.Count > 0)
        {
            for (int i = 0; i < CropLeftTrack.Keyframes.Count; i++) times.Add(CropLeftTrack.Keyframes[i].ClipRelativeTime);
        }
        if (CropRightTrack.IsKeyframingEnabled && CropRightTrack.Keyframes.Count > 0)
        {
            for (int i = 0; i < CropRightTrack.Keyframes.Count; i++) times.Add(CropRightTrack.Keyframes[i].ClipRelativeTime);
        }
        if (CropTopTrack.IsKeyframingEnabled && CropTopTrack.Keyframes.Count > 0)
        {
            for (int i = 0; i < CropTopTrack.Keyframes.Count; i++) times.Add(CropTopTrack.Keyframes[i].ClipRelativeTime);
        }
        if (CropBottomTrack.IsKeyframingEnabled && CropBottomTrack.Keyframes.Count > 0)
        {
            for (int i = 0; i < CropBottomTrack.Keyframes.Count; i++) times.Add(CropBottomTrack.Keyframes[i].ClipRelativeTime);
        }

        times.Sort();
        if (times.Count > 1)
        {
            int writeIdx = 1;
            for (int readIdx = 1; readIdx < times.Count; readIdx++)
            {
                if (Math.Abs(times[readIdx] - times[writeIdx - 1]) > 0.0001)
                {
                    times[writeIdx] = times[readIdx];
                    writeIdx++;
                }
            }
            if (writeIdx < times.Count)
            {
                times.RemoveRange(writeIdx, times.Count - writeIdx);
            }
        }
        return times;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public int ColorGradingVersion { get; set; } = 0;

    public void InvalidateColorFilter() => ColorGradingVersion++;

    public void CopyAttributesFrom(TimelineClip source)
    {
        Scale = source.Scale;
        ScaleX = source.ScaleX;
        ScaleY = source.ScaleY;
        PositionX = source.PositionX;
        PositionY = source.PositionY;
        Rotation = source.Rotation;
        Opacity = source.Opacity;
        CropLeft = source.CropLeft;
        CropRight = source.CropRight;
        CropTop = source.CropTop;
        CropBottom = source.CropBottom;
        CornerRadius = source.CornerRadius;
        BlendMode = source.BlendMode;
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
        Whites = source.Whites;
        Blacks = source.Blacks;
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

        MasterCurvePoints = source.MasterCurvePoints.Select(p => new CurveControlPoint(p.X, p.Y)).ToList();
        RedCurvePoints = source.RedCurvePoints.Select(p => new CurveControlPoint(p.X, p.Y)).ToList();
        GreenCurvePoints = source.GreenCurvePoints.Select(p => new CurveControlPoint(p.X, p.Y)).ToList();
        BlueCurvePoints = source.BlueCurvePoints.Select(p => new CurveControlPoint(p.X, p.Y)).ToList();

        HslHueShift = (float[])source.HslHueShift.Clone();
        HslSatShift = (float[])source.HslSatShift.Clone();
        HslLumShift = (float[])source.HslLumShift.Clone();

        LutFilePath = source.LutFilePath ?? string.Empty;
        LutIntensity = source.LutIntensity;
        Enable16BitColorPipeline = source.Enable16BitColorPipeline;
        Mask = source.Mask != null ? source.Mask.Clone() : MaskPath.CreateRectangleDefault();

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
        CropLeftTrack = source.CropLeftTrack.Clone();
        CropRightTrack = source.CropRightTrack.Clone();
        CropTopTrack = source.CropTopTrack.Clone();
        CropBottomTrack = source.CropBottomTrack.Clone();
    }

    public void CopyStateFrom(TimelineClip source)
    {
        Label = source.Label;
        Kind = source.Kind;
        TransitionEffectType = source.TransitionEffectType;
        IsAudioTransition = source.IsAudioTransition;
        TransitionSpeed = source.TransitionSpeed;
        TransitionEasing = source.TransitionEasing;
        TransitionDirection = source.TransitionDirection;
        TransitionStartOpacity = source.TransitionStartOpacity;
        TransitionEndOpacity = source.TransitionEndOpacity;
        TimelineStart = source.TimelineStart;
        Duration = source.Duration;
        SourceIn = source.SourceIn;
        SourceOut = source.SourceOut;
        Speed = source.Speed;
        ReversePlayback = source.ReversePlayback;
        IsFreezeFrame = source.IsFreezeFrame;
        FreezeSourceTimestamp = source.FreezeSourceTimestamp;
        FadeInDuration = source.FadeInDuration;
        FadeOutDuration = source.FadeOutDuration;

        CopyAttributesFrom(source);

        NestedTracks = source.NestedTracks != null
            ? source.NestedTracks.Select(t => t.Clone()).ToList()
            : new List<TimelineTrack>();

        InTransition = source.InTransition?.Clone();
        OutTransition = source.OutTransition?.Clone();
        TextData = source.TextData?.Clone();
        ColorGradingVersion++;
    }

    public void ResetCurvesAndHsl()
    {
        MasterCurvePoints = CreateDefaultCurve();
        RedCurvePoints = CreateDefaultCurve();
        GreenCurvePoints = CreateDefaultCurve();
        BlueCurvePoints = CreateDefaultCurve();

        if (HslHueShift != null) Array.Clear(HslHueShift, 0, HslHueShift.Length);
        if (HslSatShift != null) Array.Clear(HslSatShift, 0, HslSatShift.Length);
        if (HslLumShift != null) Array.Clear(HslLumShift, 0, HslLumShift.Length);
    }

    public void ResetAllEffectsAndColor()
    {
        Scale = 1.0f;
        ScaleX = 100.0f;
        ScaleY = 100.0f;
        PositionX = 0f;
        PositionY = 0f;
        Rotation = 0f;
        Opacity = 1.0f;
        CropLeft = 0f;
        CropRight = 0f;
        CropTop = 0f;
        CropBottom = 0f;
        CornerRadius = 0f;
        BlendMode = "Normal";
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
        Whites = 0f;
        Blacks = 0f;
        Saturation = 100f;
        Vibrance = 0f;
        Temperature = 0f;
        Tint = 0f;
        Vignette = 0f;

        LutFilePath = string.Empty;
        LutIntensity = 100f;
        Mask = MaskPath.CreateRectangleDefault();

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

        MasterCurvePoints = CreateDefaultCurve();
        RedCurvePoints = CreateDefaultCurve();
        GreenCurvePoints = CreateDefaultCurve();
        BlueCurvePoints = CreateDefaultCurve();

        Array.Clear(HslHueShift, 0, HslHueShift.Length);
        Array.Clear(HslSatShift, 0, HslSatShift.Length);
        Array.Clear(HslLumShift, 0, HslLumShift.Length);

        SubjectMatteEnabled = false;
        DialogueIsolateEnabled = false;
        EqPreset = "Flat";
        CompressorEnabled = false;
        ReverbPreset = "None";
        PitchSemitones = 0f;
    }

    public TimelineClip Clone()
    {
        var copy = new TimelineClip
        {
            Id = Guid.NewGuid(),
            MediaAssetId = MediaAssetId,
            LinkedClipId = LinkedClipId
        };
        copy.CopyStateFrom(this);
        return copy;
    }
}

public class TextOverlaySpec
{
    public string Text { get; set; } = "Title Text";
    public string FontFamily { get; set; } = "Inter";
    public string FontWeightStyle { get; set; } = "Bold"; // Regular, Bold, Black, Italic
    public float FontSize { get; set; } = 48f;
    public float LetterSpacing { get; set; } = 0f; // -20px to +100px
    public float LineHeight { get; set; } = 1.2f;  // 0.5x to 3.0x
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

    // BUG FIX 1: 100% Transparent Background by Default (EnableBackground = false)
    public bool BackgroundBoxEnabled { get; set; } = false;
    public string BackgroundColorHex { get; set; } = "#D8111215";
    public float BackgroundOpacity { get; set; } = 85f;
    public float BackgroundCornerRadius { get; set; } = 8f;
    public float BackgroundPaddingX { get; set; } = 20f;
    public float BackgroundPaddingY { get; set; } = 10f;
    public string TextAnimation { get; set; } = "None";
    public string StylePreset { get; set; } = "CleanFloating";
    public float YOffset { get; set; } = 0f;
    public float VerticalPositionRatio { get; set; } = 0.85f;
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; } = false;

    [JsonIgnore]
    public bool HasBackgroundBox
    {
        get => BackgroundBoxEnabled;
        set => BackgroundBoxEnabled = value;
    }

    [JsonIgnore]
    public bool EnableBackground
    {
        get => BackgroundBoxEnabled;
        set => BackgroundBoxEnabled = value;
    }

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
        VerticalPositionRatio = VerticalPositionRatio,
        Bold = Bold,
        Italic = Italic
    };
}
