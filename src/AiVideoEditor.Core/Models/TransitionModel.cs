using System;

namespace AiVideoEditor.Core.Models;

/// <summary>
/// Professional NLE Transition Specification (Premiere Pro &amp; DaVinci Resolve style).
/// Defines transition effect type, timing, speed multiplier, direction, easing curve, and opacity bounds.
/// </summary>
public class TransitionSpec
{
    // Supported types: None, CrossDissolve, DipToBlack, DipToWhite, FadeIn, FadeOut, DirectionalWipe, PushLeft, PushRight, PushUp, PushDown, WhipZoom, Glitch, LumaFade
    public string Type { get; set; } = "CrossDissolve";
    public double Duration { get; set; } = 0.8;
    public double StartOffset { get; set; } = 0.0;
    public double Speed { get; set; } = 1.0;
    public string Easing { get; set; } = "EaseInOut";
    public string Direction { get; set; } = "Left";
    public float StartOpacity { get; set; } = 0.0f;
    public float EndOpacity { get; set; } = 1.0f;
    public bool IsEnabled { get; set; } = true;

    public TransitionSpec Clone()
    {
        return new TransitionSpec
        {
            Type = Type,
            Duration = Duration,
            StartOffset = StartOffset,
            Speed = Speed,
            Easing = Easing,
            Direction = Direction,
            StartOpacity = StartOpacity,
            EndOpacity = EndOpacity,
            IsEnabled = IsEnabled
        };
    }
}

/// <summary>
/// Pure mathematical easing solver for transition progress [0..1].
/// </summary>
public static class TransitionEasingHelper
{
    public static float ApplyEasing(float linearProgress, string? easing, double speed = 1.0)
    {
        float p = Math.Clamp(linearProgress, 0f, 1f);
        if (speed > 0.05 && Math.Abs(speed - 1.0) > 0.001)
        {
            p = Math.Clamp((float)(p * speed), 0f, 1f);
        }

        string cleanEasing = (easing ?? "EaseInOut").Trim().Replace(" ", "").ToLowerInvariant();

        return cleanEasing switch
        {
            "linear" => p,
            "easein" => p * p,
            "easeout" => 1f - (1f - p) * (1f - p),
            "easeinout" or "smoothstep" => p * p * (3f - 2f * p),
            "cubic" or "easeincubic" => p * p * p,
            "easeoutcubic" => 1f - MathF.Pow(1f - p, 3f),
            _ => p * p * (3f - 2f * p)
        };
    }
}
