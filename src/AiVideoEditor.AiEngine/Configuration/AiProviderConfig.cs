using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiVideoEditor.AiEngine.Configuration;

public enum AiExecutionMode
{
    Local,
    Cloud
}

public class AiProviderConfig
{
    public AiExecutionMode TranscriptionMode { get; set; } = AiExecutionMode.Local;
    public AiExecutionMode AgentMode { get; set; } = AiExecutionMode.Local;
    public AiExecutionMode AudioGenerationMode { get; set; } = AiExecutionMode.Local;

    // Local Server Settings (LM Studio / Ollama / Local GGUF)
    public string LocalServerProvider { get; set; } = "LM Studio"; // "LM Studio", "Ollama", "Local GGUF"
    public string LmStudioBaseUrl { get; set; } = "http://localhost:1234/v1";
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434/v1";
    public string SelectedLocalServerModel { get; set; } = "local-model";

    public string SelectedWhisperModel { get; set; } = "ggml-base.bin";
    public string SelectedLlamaModel { get; set; } = "qwen2.5-7b-instruct-q4_k_m.gguf";

    // Cloud LLM Settings
    public string SelectedCloudLlmProvider { get; set; } = "OpenAI"; // OpenAI, Gemini, Claude, Groq
    public string SelectedCloudLlmModel { get; set; } = "gpt-4o-mini";
    public string CustomOpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";

    // Encrypted storage strings
    public string EncryptedOpenAiKey { get; set; } = string.Empty;
    public string EncryptedGeminiKey { get; set; } = string.Empty;
    public string EncryptedClaudeKey { get; set; } = string.Empty;
    public string EncryptedGroqKey { get; set; } = string.Empty;
    public string EncryptedElevenLabsKey { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string OpenAiApiKey
    {
        get => Unprotect(EncryptedOpenAiKey);
        set => EncryptedOpenAiKey = Protect(value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string GeminiApiKey
    {
        get => Unprotect(EncryptedGeminiKey);
        set => EncryptedGeminiKey = Protect(value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string ClaudeApiKey
    {
        get => Unprotect(EncryptedClaudeKey);
        set => EncryptedClaudeKey = Protect(value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string GroqApiKey
    {
        get => Unprotect(EncryptedGroqKey);
        set => EncryptedGroqKey = Protect(value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string ElevenLabsApiKey
    {
        get => Unprotect(EncryptedElevenLabsKey);
        set => EncryptedElevenLabsKey = Protect(value);
    }

    public static string GetAppDataFolder()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VortexCutStudio");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string GetModelsFolder()
    {
        string folder = Path.Combine(GetAppDataFolder(), "models");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string GetConfigFilePath() => Path.Combine(GetAppDataFolder(), "ai_config.json");

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetConfigFilePath(), json);
        }
        catch { }
    }

    public static AiProviderConfig Load()
    {
        try
        {
            string path = GetConfigFilePath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AiProviderConfig>(json) ?? new AiProviderConfig();
            }
        }
        catch { }
        return new AiProviderConfig();
    }

    private static readonly byte[] EntropyBytes = Encoding.UTF8.GetBytes("VortexCutStudio.HybridEngine.Salt.v2");

    private static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            using var aes = Aes.Create();
            using var sha = SHA256.Create();
            aes.Key = sha.ComputeHash(EntropyBytes);
            aes.IV = new byte[16];
            using var encryptor = aes.CreateEncryptor();
            byte[] cipher = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            return Convert.ToBase64String(cipher);
        }
        catch
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        }
    }

    private static string Unprotect(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return string.Empty;
        try
        {
            byte[] cipher = Convert.FromBase64String(encryptedBase64);
            using var aes = Aes.Create();
            using var sha = SHA256.Create();
            aes.Key = sha.ComputeHash(EntropyBytes);
            aes.IV = new byte[16];
            using var decryptor = aes.CreateDecryptor();
            byte[] plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return string.Empty;
        }
    }
}
