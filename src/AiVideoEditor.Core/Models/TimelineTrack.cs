namespace AiVideoEditor.Core.Models;

public enum TrackType
{
    Video,
    Audio,
    Text,
    Effect
}

public class TimelineTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TrackCode { get; set; } = "V1";
    public string Name { get; set; } = string.Empty;
    public TrackType Type { get; set; }
    public int Index { get; set; }
    public bool IsMuted { get; set; }
    public bool IsSolo { get; set; }
    public bool IsLocked { get; set; }
    public bool IsVisible { get; set; } = true;
    public double TrackHeight { get; set; } = 54.0;
    public float Volume { get; set; } = 1.0f;
    public List<TimelineClip> Clips { get; set; } = new();
}
