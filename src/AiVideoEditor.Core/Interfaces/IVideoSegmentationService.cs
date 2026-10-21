namespace AiVideoEditor.Core.Interfaces;

public interface IVideoSegmentationService
{
    bool IsModelLoaded { get; }

    /// <summary>
    /// Computes an alpha mask or modifies an RGBA/BGRA pixel buffer in-place to remove background.
    /// </summary>
    void ApplyBackgroundRemoval(Span<byte> bgraPixels, int width, int height);
}
