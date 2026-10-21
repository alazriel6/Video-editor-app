using AiVideoEditor.AiEngine.CloudProviders;
using AiVideoEditor.AiEngine.Configuration;
using AiVideoEditor.AiEngine.LocalProviders;
using AiVideoEditor.Core.Interfaces;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;

namespace AiVideoEditor.AiEngine;

public sealed class HybridAiRouter : IAiTranscriptionService, IAiAgentService, IAiSfxService, IVideoSegmentationService, IDisposable
{
    public AiProviderConfig Config { get; }
    public LocalModelDownloader Downloader { get; }
    public UniversalAiClient UniversalClient { get; }

    private readonly LocalWhisperService _localWhisper;
    private readonly CloudTranscriptionService _cloudWhisper;
    private readonly LocalLlamaAgentService _localAgent;
    private readonly CloudLlmAgentService _cloudAgent;
    private readonly LocalTtsAndSfxService _localAudio;
    private readonly ElevenLabsAudioService _cloudAudio;
    private readonly OnnxRvmMattingService _rvmMatting;

    public HybridAiRouter(AiProviderConfig config, TimelineService timelineService)
    {
        Config = config;
        Downloader = new LocalModelDownloader();
        UniversalClient = new UniversalAiClient();

        _localWhisper = new LocalWhisperService(config, Downloader);
        _cloudWhisper = new CloudTranscriptionService(config, _localWhisper);

        _localAudio = new LocalTtsAndSfxService();
        _cloudAudio = new ElevenLabsAudioService(config, _localAudio);

        _localAgent = new LocalLlamaAgentService(config, Downloader, timelineService, this, UniversalClient);
        _cloudAgent = new CloudLlmAgentService(config, timelineService, this, UniversalClient);

        _rvmMatting = new OnnxRvmMattingService(Downloader);
    }

    public bool IsModelLoaded => _rvmMatting.IsModelLoaded;

    public Task<List<TranscriptSegment>> TranscribeAsync(
        string mediaFilePath,
        string language = "auto",
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Config.TranscriptionMode == AiExecutionMode.Cloud
            ? _cloudWhisper.TranscribeAsync(mediaFilePath, language, progress, cancellationToken)
            : _localWhisper.TranscribeAsync(mediaFilePath, language, progress, cancellationToken);
    }

    public Task<AiAgentExecutionResult> ExecutePromptAsync(
        string userPrompt,
        ProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        return Config.AgentMode == AiExecutionMode.Cloud
            ? _cloudAgent.ExecutePromptAsync(userPrompt, document, cancellationToken)
            : _localAgent.ExecutePromptAsync(userPrompt, document, cancellationToken);
    }

    public Task<string> GenerateVoiceoverAsync(
        string text,
        string voicePreset = "Narrator",
        CancellationToken cancellationToken = default)
    {
        return Config.AudioGenerationMode == AiExecutionMode.Cloud
            ? _cloudAudio.GenerateVoiceoverAsync(text, voicePreset, cancellationToken)
            : _localAudio.GenerateVoiceoverAsync(text, voicePreset, cancellationToken);
    }

    public Task<string> GenerateSoundEffectAsync(
        string prompt,
        double durationSeconds = 2.0,
        CancellationToken cancellationToken = default)
    {
        return Config.AudioGenerationMode == AiExecutionMode.Cloud
            ? _cloudAudio.GenerateSoundEffectAsync(prompt, durationSeconds, cancellationToken)
            : _localAudio.GenerateSoundEffectAsync(prompt, durationSeconds, cancellationToken);
    }

    public void ApplyBackgroundRemoval(Span<byte> bgraPixels, int width, int height)
    {
        _rvmMatting.ApplyBackgroundRemoval(bgraPixels, width, height);
    }

    public void Dispose()
    {
        _rvmMatting.Dispose();
    }
}
