namespace AiVideoEditor.Core.Models;

public enum EasingType
{
    Linear,
    Smooth,
    EaseIn,
    EaseOut,
    EaseInOut,
    EaseInCubic,
    EaseOutCubic,
    EaseInOutCubic,
    Hold
}

public class KeyframePoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Time in seconds relative to the start of the clip (0.0 to Clip.Duration)
    public double ClipRelativeTime { get; set; }
    public float Value { get; set; }
    public EasingType Easing { get; set; } = EasingType.EaseInOutCubic;

    [System.Text.Json.Serialization.JsonIgnore]
    public double TimeOffsetSeconds
    {
        get => ClipRelativeTime;
        set => ClipRelativeTime = value;
    }

    public KeyframePoint Clone() => new()
    {
        Id = Guid.NewGuid(),
        ClipRelativeTime = ClipRelativeTime,
        Value = Value,
        Easing = Easing
    };
}

public class AnimatableProperty
{
    public string PropertyName { get; set; } = string.Empty; // "Scale", "PosX", "PosY", "Rotation", "Opacity", "Volume"
    public float DefaultValue { get; set; }
    public bool IsKeyframingEnabled { get; set; }
    public List<KeyframePoint> Keyframes { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAnimated
    {
        get => IsKeyframingEnabled;
        set => IsKeyframingEnabled = value;
    }

    public float EvaluateAt(double clipLocalTime)
    {
        if (!IsKeyframingEnabled || Keyframes.Count == 0)
            return DefaultValue;

        var sorted = Keyframes.OrderBy(k => k.ClipRelativeTime).ToList();
        if (clipLocalTime <= sorted[0].ClipRelativeTime) return sorted[0].Value;
        if (clipLocalTime >= sorted[^1].ClipRelativeTime) return sorted[^1].Value;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var k1 = sorted[i];
            var k2 = sorted[i + 1];
            if (clipLocalTime >= k1.ClipRelativeTime && clipLocalTime <= k2.ClipRelativeTime)
            {
                double duration = k2.ClipRelativeTime - k1.ClipRelativeTime;
                if (duration <= 0.0001) return k2.Value;
                float t = (float)((clipLocalTime - k1.ClipRelativeTime) / duration);
                t = ApplyEasing(t, k1.Easing);
                return k1.Value + (k2.Value - k1.Value) * t;
            }
        }
        return DefaultValue;
    }

    public void AddOrUpdateKeyframe(double clipLocalTime, double value, EasingType easing = EasingType.EaseInOutCubic)
    {
        AddOrUpdateKeyframe(clipLocalTime, (float)value, easing);
    }

    public void AddOrUpdateKeyframe(double clipLocalTime, float value, EasingType easing = EasingType.EaseInOutCubic)
    {
        IsKeyframingEnabled = true;
        clipLocalTime = Math.Max(0, Math.Round(clipLocalTime, 3));
        var existing = Keyframes.FirstOrDefault(k => Math.Abs(k.ClipRelativeTime - clipLocalTime) < 0.04);
        if (existing != null)
        {
            existing.Value = value;
            existing.Easing = easing;
        }
        else
        {
            Keyframes.Add(new KeyframePoint
            {
                ClipRelativeTime = clipLocalTime,
                Value = value,
                Easing = easing
            });
            Keyframes = Keyframes.OrderBy(k => k.ClipRelativeTime).ToList();
        }
    }

    public bool ToggleKeyframeAt(double clipLocalTime, double currentValue, EasingType easing = EasingType.EaseInOutCubic)
    {
        return ToggleKeyframeAt(clipLocalTime, (float)currentValue, easing);
    }

    public bool ToggleKeyframeAt(double clipLocalTime, float currentValue, EasingType easing = EasingType.EaseInOutCubic)
    {
        var existing = Keyframes.FirstOrDefault(k => Math.Abs(k.ClipRelativeTime - clipLocalTime) < 0.05);
        if (existing != null)
        {
            Keyframes.Remove(existing);
            if (Keyframes.Count == 0)
                IsKeyframingEnabled = false;
            return false;
        }

        AddOrUpdateKeyframe(clipLocalTime, currentValue, easing);
        return true;
    }

    public bool RemoveKeyframeNear(double clipLocalTime, double tolerance = 0.05)
    {
        var existing = Keyframes.FirstOrDefault(k => Math.Abs(k.ClipRelativeTime - clipLocalTime) <= tolerance);
        if (existing != null)
        {
            Keyframes.Remove(existing);
            if (Keyframes.Count == 0)
                IsKeyframingEnabled = false;
            return true;
        }
        return false;
    }

    public void ClearKeyframes()
    {
        Keyframes.Clear();
        IsKeyframingEnabled = false;
    }

    public bool HasKeyframeNear(double clipLocalTime, double tolerance = 0.05)
    {
        return IsKeyframingEnabled && Keyframes.Any(k => Math.Abs(k.ClipRelativeTime - clipLocalTime) <= tolerance);
    }

    public double? GetPreviousKeyframeTime(double clipLocalTime)
    {
        var prev = Keyframes
            .Where(k => k.ClipRelativeTime < clipLocalTime - 0.04)
            .OrderByDescending(k => k.ClipRelativeTime)
            .FirstOrDefault();
        return prev?.ClipRelativeTime;
    }

    public double? GetNextKeyframeTime(double clipLocalTime)
    {
        var next = Keyframes
            .Where(k => k.ClipRelativeTime > clipLocalTime + 0.04)
            .OrderBy(k => k.ClipRelativeTime)
            .FirstOrDefault();
        return next?.ClipRelativeTime;
    }

    public AnimatableProperty Clone()
    {
        return new AnimatableProperty
        {
            PropertyName = PropertyName,
            DefaultValue = DefaultValue,
            IsKeyframingEnabled = IsKeyframingEnabled,
            Keyframes = Keyframes.Select(k => k.Clone()).ToList()
        };
    }

    private static float ApplyEasing(float t, EasingType easing) => easing switch
    {
        EasingType.EaseIn or EasingType.EaseInCubic => t * t * t,
        EasingType.EaseOut or EasingType.EaseOutCubic => 1f - MathF.Pow(1f - t, 3f),
        EasingType.Smooth or EasingType.EaseInOut or EasingType.EaseInOutCubic => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f,
        EasingType.Hold => 0f,
        _ => t
    };
}
