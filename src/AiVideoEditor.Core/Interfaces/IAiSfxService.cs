namespace AiVideoEditor.Core.Interfaces;

public interface IAiSfxService
{
    Task<string> GenerateVoiceoverAsync(
        string text,
        string voicePreset = "Narrator",
        CancellationToken cancellationToken = default);

    Task<string> GenerateSoundEffectAsync(
        string prompt,
        double durationSeconds = 2.0,
        CancellationToken cancellationToken = default);
}
