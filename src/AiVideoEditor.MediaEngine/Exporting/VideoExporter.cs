using AiVideoEditor.Core.Interfaces;
using AiVideoEditor.Core.Models;
using AiVideoEditor.MediaEngine.Audio;
using AiVideoEditor.MediaEngine.Rendering;

namespace AiVideoEditor.MediaEngine.Exporting;

public sealed class VideoExporter : IVideoRenderEngine
{
    private readonly FFmpegExportEngine _ffmpegEngine;

    public VideoExporter(CompositorEngine compositor, MultiTrackAudioPlayer audioPlayer)
    {
        _ffmpegEngine = new FFmpegExportEngine(compositor, audioPlayer);
    }

    public Task ExportProjectAsync(
        ProjectDocument document,
        ExportSettings settings,
        IProgress<(double percent, string status, TimeSpan eta)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _ffmpegEngine.ExportProjectAsync(document, settings, progress, cancellationToken);
    }
}
