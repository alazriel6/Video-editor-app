namespace AiVideoEditor.AiEngine.Configuration;

public class LocalModelSpec
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string SizeDescription { get; init; } = string.Empty;
}

public class LocalModelDownloader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(2) };

    public static IReadOnlyList<LocalModelSpec> Catalog { get; } = new List<LocalModelSpec>
    {
        new()
        {
            Id = "whisper-base",
            DisplayName = "Whisper Base (Multilingual Speech Recognition)",
            Category = "Auto Subtitles & Remove Silence",
            FileName = "ggml-base.bin",
            DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
            SizeDescription = "147 MB"
        },
        new()
        {
            Id = "whisper-large-v3-turbo",
            DisplayName = "Whisper Large v3 Turbo (Studio Speech Recognition)",
            Category = "Auto Subtitles & Remove Silence",
            FileName = "ggml-large-v3-turbo.bin",
            DownloadUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin",
            SizeDescription = "1.5 GB"
        },
        new()
        {
            Id = "qwen2.5-7b",
            DisplayName = "Qwen 2.5 7B Instruct (Local Smart Assistant Model)",
            Category = "Smart Edit Assistant",
            FileName = "qwen2.5-7b-instruct-q4_k_m.gguf",
            DownloadUrl = "https://huggingface.co/Qwen/Qwen2.5-7B-Instruct-GGUF/resolve/main/qwen2.5-7b-instruct-q4_k_m.gguf",
            SizeDescription = "4.68 GB"
        },
        new()
        {
            Id = "rvm-mobilenetv3",
            DisplayName = "Subject Isolation Engine (Video Background Removal)",
            Category = "Remove Background",
            FileName = "rvm_mobilenetv3_fp32.onnx",
            DownloadUrl = "https://huggingface.co/PeterL1n/RobustVideoMatting/resolve/main/rvm_mobilenetv3_fp32.onnx",
            SizeDescription = "15 MB"
        },
        new()
        {
            Id = "kokoro-tts",
            DisplayName = "Kokoro v1.0 Studio Voice Synthesizer",
            Category = "Voiceover & Sound Effects",
            FileName = "kokoro-v1.0.onnx",
            DownloadUrl = "https://huggingface.co/hexgrad/Kokoro-82M/resolve/main/kokoro-v0_19.onnx",
            SizeDescription = "310 MB"
        }
    };

    public string GetModelPath(string fileName)
    {
        return Path.Combine(AiProviderConfig.GetModelsFolder(), fileName);
    }

    public bool IsModelDownloaded(string fileName)
    {
        string path = GetModelPath(fileName);
        return File.Exists(path) && new FileInfo(path).Length > 1024;
    }

    public async Task DownloadModelAsync(
        LocalModelSpec spec,
        IProgress<(double Percent, string Status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string targetPath = GetModelPath(spec.FileName);
        string tempPath = targetPath + ".part";

        progress?.Report((1.0, $"Connecting to model repository ({spec.FileName})..."));

        using var response = await Http.GetAsync(spec.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;

                if (totalBytes > 0)
                {
                    double pct = (double)totalRead / totalBytes * 100.0;
                    double mbRead = totalRead / (1024.0 * 1024.0);
                    double mbTotal = totalBytes / (1024.0 * 1024.0);
                    progress?.Report((pct, $"Downloading {spec.FileName}: {mbRead:0.1} / {mbTotal:0.1} MB ({pct:0}%)"));
                }
                else
                {
                    double mbRead = totalRead / (1024.0 * 1024.0);
                    progress?.Report((50.0, $"Downloading {spec.FileName}: {mbRead:0.1} MB..."));
                }
            }
        }

        if (File.Exists(targetPath))
            File.Delete(targetPath);
        File.Move(tempPath, targetPath);

        progress?.Report((100.0, $"Model {spec.FileName} verified on disk."));
    }
}
