namespace AiVideoEditor.Core.Models;

public class TransitionSpec
{
    // Supported types: CrossFade, ZoomIn, Glitch, WipeLeft, Dissolve
    public string Type { get; set; } = "CrossFade";
    public double Duration { get; set; } = 0.5;
}
