namespace AiVideoEditor.Core.Models;

public class TranscriptSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; } = 1.0f;
    public bool IsDeleted { get; set; } = false;
    public List<TranscriptWord> Words { get; set; } = new();
}

public class TranscriptWord
{
    public string Word { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public float Confidence { get; set; } = 1.0f;
    public bool IsFillerOrSilence { get; set; } = false;
}
