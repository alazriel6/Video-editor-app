using System.Text.Json;
using System.Text.Json.Serialization;
using AiVideoEditor.Core.Models;

namespace AiVideoEditor.Core.Services;

public static class ProjectSerializer
{
    public const string DefaultExtension = ".vcsproj";

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
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Project file path cannot be empty.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        document.CurrentProjectFilePath = fullPath;
        StoreRelativePaths(document, dir ?? string.Empty);

        string tempPath = fullPath + ".tmp";
        string json = ToJson(document);
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, fullPath, overwrite: true);
    }

    public static async Task<ProjectDocument> LoadAsync(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        string json = await File.ReadAllTextAsync(fullPath);
        var doc = FromJson(json) ?? ProjectDocument.CreateDefault();
        doc.CurrentProjectFilePath = fullPath;

        string? projectDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(projectDir))
        {
            ResolveRelativePaths(doc, projectDir);
            var missing = FindMissingMediaAssets(doc);
            if (missing.Count > 0)
            {
                TryAutoRelinkMissingMedia(doc, new[]
                {
                    projectDir,
                    Path.Combine(projectDir, "Media"),
                    Path.Combine(projectDir, "Assets"),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                });
            }
        }

        doc.InvalidateAssetCache();
        return doc;
    }

    public static void StoreRelativePaths(ProjectDocument document, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
            return;

        foreach (var asset in document.MediaPool)
        {
            if (!string.IsNullOrWhiteSpace(asset.FilePath))
            {
                try
                {
                    if (File.Exists(asset.FilePath))
                    {
                        var fi = new FileInfo(asset.FilePath);
                        asset.FileSizeBytes = fi.Length;
                    }
                    asset.RelativeFilePath = Path.GetRelativePath(baseDirectory, Path.GetFullPath(asset.FilePath));
                }
                catch
                {
                    asset.RelativeFilePath = Path.GetFileName(asset.FilePath);
                }
            }
        }
    }

    public static void ResolveRelativePaths(ProjectDocument document, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return;

        foreach (var asset in document.MediaPool)
        {
            if (!string.IsNullOrWhiteSpace(asset.FilePath) && File.Exists(asset.FilePath))
                continue;

            if (!string.IsNullOrWhiteSpace(asset.RelativeFilePath))
            {
                try
                {
                    string candidate = Path.GetFullPath(Path.Combine(baseDirectory, asset.RelativeFilePath));
                    if (File.Exists(candidate))
                    {
                        asset.FilePath = candidate;
                        continue;
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(asset.FilePath))
            {
                string fileName = Path.GetFileName(asset.FilePath);
                string localCandidate = Path.Combine(baseDirectory, fileName);
                if (File.Exists(localCandidate))
                {
                    asset.FilePath = localCandidate;
                    continue;
                }

                string mediaSubfolderCandidate = Path.Combine(baseDirectory, "Media", fileName);
                if (File.Exists(mediaSubfolderCandidate))
                {
                    asset.FilePath = mediaSubfolderCandidate;
                }
            }
        }

        ResolveLutPathsOnTracks(document.Tracks, baseDirectory);
    }

    private static void ResolveLutPathsOnTracks(List<TimelineTrack>? tracks, string baseDirectory)
    {
        if (tracks == null) return;
        foreach (var track in tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (!string.IsNullOrWhiteSpace(clip.LutFilePath) && !File.Exists(clip.LutFilePath))
                {
                    try
                    {
                        string relCandidate = Path.GetFullPath(Path.Combine(baseDirectory, clip.LutFilePath));
                        if (File.Exists(relCandidate))
                        {
                            clip.LutFilePath = relCandidate;
                        }
                        else
                        {
                            string lutSubCandidate = Path.Combine(baseDirectory, "LUTs", Path.GetFileName(clip.LutFilePath));
                            if (File.Exists(lutSubCandidate))
                                clip.LutFilePath = lutSubCandidate;
                        }
                    }
                    catch { }
                }

                if (clip.NestedTracks != null && clip.NestedTracks.Count > 0)
                {
                    ResolveLutPathsOnTracks(clip.NestedTracks, baseDirectory);
                }
            }
        }
    }

    /// <summary>
    /// Detects all MediaAsset entries in the project whose underlying media files are missing on disk.
    /// </summary>
    public static List<MediaAsset> FindMissingMediaAssets(ProjectDocument document)
    {
        var missing = new List<MediaAsset>();
        foreach (var asset in document.MediaPool)
        {
            if (string.IsNullOrWhiteSpace(asset.FilePath) || !File.Exists(asset.FilePath))
            {
                missing.Add(asset);
            }
        }
        return missing;
    }

    /// <summary>
    /// Automatically searches candidate directories to relink missing media assets by matching file name and optional file size.
    /// Returns the number of successfully relinked assets.
    /// </summary>
    public static int TryAutoRelinkMissingMedia(ProjectDocument document, IEnumerable<string> searchDirectories)
    {
        var missing = FindMissingMediaAssets(document);
        if (missing.Count == 0) return 0;

        int relinkedCount = 0;
        var validDirs = searchDirectories
            .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var asset in missing)
        {
            string targetFileName = !string.IsNullOrWhiteSpace(asset.FilePath)
                ? Path.GetFileName(asset.FilePath)
                : asset.Name;

            if (string.IsNullOrWhiteSpace(targetFileName))
                continue;

            foreach (var dir in validDirs)
            {
                try
                {
                    string directMatch = Path.Combine(dir, targetFileName);
                    if (File.Exists(directMatch))
                    {
                        asset.FilePath = directMatch;
                        relinkedCount++;
                        break;
                    }

                    var found = Directory.EnumerateFiles(dir, targetFileName, new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 3,
                        IgnoreInaccessible = true,
                        MatchCasing = MatchCasing.CaseInsensitive
                    }).FirstOrDefault(candidate =>
                    {
                        if (asset.FileSizeBytes <= 0) return true;
                        try { return new FileInfo(candidate).Length == asset.FileSizeBytes; }
                        catch { return true; }
                    });

                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        asset.FilePath = found;
                        relinkedCount++;
                        break;
                    }
                }
                catch { }
            }
        }

        if (relinkedCount > 0)
            document.InvalidateAssetCache();

        return relinkedCount;
    }

    public static bool RelinkAsset(ProjectDocument document, Guid assetId, string newFilePath)
    {
        if (string.IsNullOrWhiteSpace(newFilePath) || !File.Exists(newFilePath))
            return false;

        var asset = document.FindAsset(assetId);
        if (asset == null)
            return false;

        string fullPath = Path.GetFullPath(newFilePath);
        asset.FilePath = fullPath;
        asset.Name = Path.GetFileName(fullPath);
        try
        {
            asset.FileSizeBytes = new FileInfo(fullPath).Length;
            if (!string.IsNullOrWhiteSpace(document.CurrentProjectFilePath))
            {
                string? projDir = Path.GetDirectoryName(document.CurrentProjectFilePath);
                if (!string.IsNullOrEmpty(projDir))
                    asset.RelativeFilePath = Path.GetRelativePath(projDir, fullPath);
            }
        }
        catch { }

        document.InvalidateAssetCache();
        return true;
    }

    /// <summary>
    /// Collects and archives the entire project, copying all source media assets and 3D LUTs into a self-contained bundle directory.
    /// </summary>
    public static async Task<string> ArchiveProjectAsync(
        ProjectDocument document,
        string archiveDirectory,
        IProgress<(double percent, string status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string rootDir = Path.GetFullPath(archiveDirectory);
        string mediaDir = Path.Combine(rootDir, "Media");
        string lutsDir = Path.Combine(rootDir, "LUTs");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(mediaDir);
        Directory.CreateDirectory(lutsDir);

        string jsonClone = ToJson(document);
        var archivedDoc = FromJson(jsonClone) ?? ProjectDocument.CreateDefault();

        int totalItems = Math.Max(1, archivedDoc.MediaPool.Count + 1);
        int completed = 0;

        foreach (var asset in archivedDoc.MediaPool)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(asset.FilePath) && File.Exists(asset.FilePath))
            {
                string fileName = Path.GetFileName(asset.FilePath);
                string destPath = Path.Combine(mediaDir, fileName);
                progress?.Report(((double)completed / totalItems * 100.0, $"Archiving media: {fileName}..."));

                if (!string.Equals(Path.GetFullPath(asset.FilePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Run(() => File.Copy(asset.FilePath, destPath, overwrite: true), cancellationToken);
                }

                asset.FilePath = destPath;
                asset.RelativeFilePath = Path.Combine("Media", fileName);
            }
            completed++;
        }

        await CopyLutsRecursiveAsync(archivedDoc.Tracks, lutsDir, cancellationToken);

        string safeProjectName = string.Join("_", (archivedDoc.ProjectName ?? "Project").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeProjectName))
            safeProjectName = "Vortex_Archive";

        string archivedProjectFile = Path.Combine(rootDir, $"{safeProjectName}{DefaultExtension}");
        await SaveAsync(archivedDoc, archivedProjectFile);
        progress?.Report((100.0, $"Project archived to {archivedProjectFile}"));
        return archivedProjectFile;
    }

    private static async Task CopyLutsRecursiveAsync(List<TimelineTrack>? tracks, string lutsDir, CancellationToken ct)
    {
        if (tracks == null) return;
        foreach (var track in tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (!string.IsNullOrWhiteSpace(clip.LutFilePath) && File.Exists(clip.LutFilePath))
                {
                    string lutName = Path.GetFileName(clip.LutFilePath);
                    string destLut = Path.Combine(lutsDir, lutName);
                    if (!string.Equals(Path.GetFullPath(clip.LutFilePath), Path.GetFullPath(destLut), StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Run(() => File.Copy(clip.LutFilePath, destLut, overwrite: true), ct);
                    }
                    clip.LutFilePath = destLut;
                }

                if (clip.NestedTracks != null && clip.NestedTracks.Count > 0)
                {
                    await CopyLutsRecursiveAsync(clip.NestedTracks, lutsDir, ct);
                }
            }
        }
    }
}
