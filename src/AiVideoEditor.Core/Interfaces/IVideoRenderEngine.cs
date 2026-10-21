using AiVideoEditor.Core.Models;

namespace AiVideoEditor.Core.Interfaces;

public class ExportSettings
{
    public string OutputPath { get; set; } = string.Empty;
    public string PresetName { get; set; } = "YouTube 1080p MP4";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double Fps { get; set; } = 30.0;
    public string HardwareEncoder { get; set; } = "libx264"; // h264_nvenc, hevc_nvenc, h264_qsv, h264_amf, libx264, libx265, prores_ks
    public int BitrateKbps { get; set; } = 16000;
    public bool UseMarkInOutRange { get; set; } = false;
    public double? RangeStartSeconds { get; set; }
    public double? RangeEndSeconds { get; set; }
    public bool AudioOnly { get; set; } = false;
}

public interface IVideoRenderEngine
{
    Task ExportProjectAsync(
        ProjectDocument document,
        ExportSettings settings,
        IProgress<(double percent, string status, TimeSpan eta)>? progress = null,
        CancellationToken cancellationToken = default);
}
