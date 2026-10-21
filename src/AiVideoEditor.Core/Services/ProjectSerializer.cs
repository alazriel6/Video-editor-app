using System.Text.Json;
using System.Text.Json.Serialization;
using AiVideoEditor.Core.Models;

namespace AiVideoEditor.Core.Services;

public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(ProjectDocument document)
    {
        return JsonSerializer.Serialize(document, Options);
    }

    public static ProjectDocument? FromJson(string json)
    {
        return JsonSerializer.Deserialize<ProjectDocument>(json, Options);
    }

    public static async Task SaveAsync(ProjectDocument document, string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string json = ToJson(document);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static async Task<ProjectDocument> LoadAsync(string filePath)
    {
        string json = await File.ReadAllTextAsync(filePath);
        return FromJson(json) ?? ProjectDocument.CreateDefault();
    }
}
