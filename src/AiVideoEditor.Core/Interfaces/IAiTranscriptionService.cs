using AiVideoEditor.Core.Models;

namespace AiVideoEditor.Core.Interfaces;

public interface IAiTranscriptionService
{
    Task<List<TranscriptSegment>> TranscribeAsync(
        string mediaFilePath,
        string language = "auto",
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
