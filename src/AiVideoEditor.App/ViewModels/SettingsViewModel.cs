using System.Collections.ObjectModel;
using AiVideoEditor.AiEngine;
using AiVideoEditor.AiEngine.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiVideoEditor.App.ViewModels;

public partial class LocalModelItemViewModel : ViewModelBase
{
    private readonly LocalModelDownloader _downloader;
    public LocalModelSpec Spec { get; }

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public LocalModelItemViewModel(LocalModelSpec spec, LocalModelDownloader downloader)
    {
        Spec = spec;
        _downloader = downloader;
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        IsInstalled = _downloader.IsModelDownloaded(Spec.FileName);
        StatusText = IsInstalled ? "VERIFIED ON DISK (OFFLINE READY)" : $"NOT INSTALLED ({Spec.SizeDescription})";
    }

    [RelayCommand]
    public async Task DownloadAsync()
    {
        if (IsDownloading) return;
        IsDownloading = true;
        DownloadProgress = 2;
        StatusText = "Initializing HTTP stream...";

        try
        {
            var progress = new Progress<(double Percent, string Status)>(p =>
            {
                DownloadProgress = p.Percent;
                StatusText = p.Status;
            });

            await _downloader.DownloadModelAsync(Spec, progress);
            RefreshStatus();
        }
        catch (Exception ex)
        {
            StatusText = $"Fault: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly AiProviderConfig _config;
    private readonly UniversalAiClient _universalClient;
    private readonly Action<string> _setStatus;

    [ObservableProperty]
    private bool _useCloudTranscription;

    [ObservableProperty]
    private bool _useCloudAgent;

    [ObservableProperty]
    private bool _useCloudAudio;

    // Local Server Settings (LM Studio / Ollama)
    [ObservableProperty]
    private string _localServerProvider = "LM Studio";

    [ObservableProperty]
    private string _lmStudioBaseUrl = "http://localhost:1234/v1";

    [ObservableProperty]
    private string _ollamaBaseUrl = "http://localhost:11434/v1";

    [ObservableProperty]
    private string _selectedLocalServerModel = "local-model";

    [ObservableProperty]
    private string _connectionStatusText = "Ready — Click 'Test Connection & Fetch Loaded Models' to verify LM Studio / Cloud API";

    [ObservableProperty]
    private bool _isTestingConnection;

    // Cloud API Settings
    [ObservableProperty]
    private string _selectedCloudLlmProvider = "OpenAI";

    [ObservableProperty]
    private string _selectedCloudLlmModel = "gpt-4o-mini";

    [ObservableProperty]
    private string _openAiKey = string.Empty;

    [ObservableProperty]
    private string _geminiKey = string.Empty;

    [ObservableProperty]
    private string _claudeKey = string.Empty;

    [ObservableProperty]
    private string _groqKey = string.Empty;

    [ObservableProperty]
    private string _elevenLabsKey = string.Empty;

    [ObservableProperty]
    private string _selectedHardwareBackend = "DirectML / CUDA Auto-Detect";

    public ObservableCollection<string> AvailableLocalModels { get; } = new()
    {
        "local-model",
        "qwen2.5-coder-7b-instruct",
        "llama-3.2-3b-instruct",
        "deepseek-r1-distill-qwen-7b"
    };

    public IReadOnlyList<string> LocalServerProviders { get; } = new[]
    {
        "LM Studio",
        "Ollama",
        "Local Offline Model"
    };

    public IReadOnlyList<string> CloudProviders { get; } = new[]
    {
        "OpenAI (ChatGPT)",
        "Google Gemini",
        "Anthropic Claude",
        "Groq"
    };

    public IReadOnlyList<string> CloudModelPresets { get; } = new[]
    {
        "gpt-4o-mini",
        "gpt-4o",
        "o3-mini",
        "gemini-2.5-flash",
        "gemini-2.5-pro",
        "gemini-2.0-flash",
        "claude-3-7-sonnet-latest",
        "llama-3.3-70b-versatile"
    };

    public IReadOnlyList<string> HardwareBackends { get; } = new[]
    {
        "DirectML / CUDA Auto-Detect",
        "NVIDIA CUDA 12 Execution Provider",
        "DirectX 12 DirectML Provider",
        "CPU AVX2 Fallback"
    };

    public string ModelsFolderPath => AiProviderConfig.GetModelsFolder();

    public ObservableCollection<LocalModelItemViewModel> LocalModels { get; } = new();

    public SettingsViewModel(
        AiProviderConfig config,
        LocalModelDownloader downloader,
        UniversalAiClient universalClient,
        Action<string> setStatus)
    {
        _config = config;
        _universalClient = universalClient;
        _setStatus = setStatus;

        UseCloudTranscription = config.TranscriptionMode == AiExecutionMode.Cloud;
        UseCloudAgent = config.AgentMode == AiExecutionMode.Cloud;
        UseCloudAudio = config.AudioGenerationMode == AiExecutionMode.Cloud;

        LocalServerProvider = config.LocalServerProvider;
        LmStudioBaseUrl = config.LmStudioBaseUrl;
        OllamaBaseUrl = config.OllamaBaseUrl;
        SelectedLocalServerModel = config.SelectedLocalServerModel;

        SelectedCloudLlmProvider = config.SelectedCloudLlmProvider;
        SelectedCloudLlmModel = config.SelectedCloudLlmModel;

        OpenAiKey = config.OpenAiApiKey;
        GeminiKey = config.GeminiApiKey;
        ClaudeKey = config.ClaudeApiKey;
        GroqKey = config.GroqApiKey;
        ElevenLabsKey = config.ElevenLabsApiKey;

        foreach (var spec in LocalModelDownloader.Catalog)
        {
            LocalModels.Add(new LocalModelItemViewModel(spec, downloader));
        }
    }

    [RelayCommand]
    public async Task TestLocalConnectionAsync()
    {
        if (IsTestingConnection) return;
        IsTestingConnection = true;
        string url = LocalServerProvider.Contains("Ollama", StringComparison.OrdinalIgnoreCase)
            ? OllamaBaseUrl
            : LmStudioBaseUrl;
        ConnectionStatusText = $"Probing {LocalServerProvider} at {url}...";

        try
        {
            var (connected, models, msg) = await _universalClient.TestConnectionAndFetchModelsAsync(LocalServerProvider, url);
            ConnectionStatusText = connected ? $"[CONNECTED] {msg}" : $"[OFFLINE] {msg}";
            if (connected && models.Count > 0)
            {
                AvailableLocalModels.Clear();
                foreach (var m in models)
                    AvailableLocalModels.Add(m);
                SelectedLocalServerModel = models[0];
            }
            _setStatus(ConnectionStatusText);
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    public async Task TestCloudConnectionAsync()
    {
        if (IsTestingConnection) return;
        IsTestingConnection = true;
        ConnectionStatusText = $"Verifying {SelectedCloudLlmProvider} API credentials...";

        try
        {
            string baseUrl = "https://api.openai.com/v1";
            string apiKey = OpenAiKey;

            if (SelectedCloudLlmProvider.Contains("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "https://generativelanguage.googleapis.com/v1beta";
                apiKey = GeminiKey;
            }
            else if (SelectedCloudLlmProvider.Contains("Groq", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "https://api.groq.com/openai/v1";
                apiKey = GroqKey;
            }

            var (connected, models, msg) = await _universalClient.TestConnectionAndFetchModelsAsync(SelectedCloudLlmProvider, baseUrl, apiKey);
            ConnectionStatusText = connected ? $"[CONNECTED] {msg}" : $"[ERROR] {msg}";
            _setStatus(ConnectionStatusText);
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _config.TranscriptionMode = UseCloudTranscription ? AiExecutionMode.Cloud : AiExecutionMode.Local;
        _config.AgentMode = UseCloudAgent ? AiExecutionMode.Cloud : AiExecutionMode.Local;
        _config.AudioGenerationMode = UseCloudAudio ? AiExecutionMode.Cloud : AiExecutionMode.Local;

        _config.LocalServerProvider = LocalServerProvider;
        _config.LmStudioBaseUrl = LmStudioBaseUrl.Trim();
        _config.OllamaBaseUrl = OllamaBaseUrl.Trim();
        _config.SelectedLocalServerModel = SelectedLocalServerModel.Trim();

        _config.SelectedCloudLlmProvider = SelectedCloudLlmProvider;
        _config.SelectedCloudLlmModel = SelectedCloudLlmModel.Trim();

        _config.OpenAiApiKey = OpenAiKey.Trim();
        _config.GeminiApiKey = GeminiKey.Trim();
        _config.ClaudeApiKey = ClaudeKey.Trim();
        _config.GroqApiKey = GroqKey.Trim();
        _config.ElevenLabsApiKey = ElevenLabsKey.Trim();

        _config.Save();
        _setStatus($"Vortex Cut Studio Settings Saved ({(_config.AgentMode == AiExecutionMode.Local ? LocalServerProvider : SelectedCloudLlmProvider)} active).");
    }
}
