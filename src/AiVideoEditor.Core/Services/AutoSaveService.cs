using AiVideoEditor.Core.Commands;
using AiVideoEditor.Core.Models;

namespace AiVideoEditor.Core.Services;

public sealed class AutoSaveService : IDisposable
{
    private readonly Func<ProjectDocument> _getDocument;
    private readonly UndoRedoManager _undoRedo;
    private readonly Action<string>? _onAutoSaveNotified;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly int _maxRollingBackups;
    private bool _hasUnbackedModifications;
    private bool _disposed;

    public string AutoSaveDirectory { get; }
    public DateTime? LastAutoSaveUtc { get; private set; }
    public string? LastAutoSaveFilePath { get; private set; }

    public AutoSaveService(
        Func<ProjectDocument> getDocument,
        UndoRedoManager undoRedo,
        Action<string>? onAutoSaveNotified = null,
        TimeSpan? interval = null,
        int maxRollingBackups = 10)
    {
        _getDocument = getDocument ?? throw new ArgumentNullException(nameof(getDocument));
        _undoRedo = undoRedo ?? throw new ArgumentNullException(nameof(undoRedo));
        _onAutoSaveNotified = onAutoSaveNotified;
        _maxRollingBackups = Math.Max(3, maxRollingBackups);

        AutoSaveDirectory = GetDefaultAutoSaveDirectory();
        Directory.CreateDirectory(AutoSaveDirectory);

        _undoRedo.StateChanged += OnUndoRedoStateChanged;

        TimeSpan period = interval ?? TimeSpan.FromMinutes(3);
        _timer = new Timer(OnTimerCallback, null, period, period);
    }

    public static string GetDefaultAutoSaveDirectory()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VortexCutStudio",
            "Autosave");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void OnUndoRedoStateChanged()
    {
        if (_undoRedo.IsProjectModified)
        {
            _hasUnbackedModifications = true;
        }
    }

    private async void OnTimerCallback(object? state)
    {
        if (_disposed || !_undoRedo.IsProjectModified || !_hasUnbackedModifications)
            return;

        await PerformAutoSaveAsync();
    }

    public async Task<string?> PerformAutoSaveAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return null;
        if (!await _saveLock.WaitAsync(0, cancellationToken))
            return null;

        try
        {
            var doc = _getDocument();
            if (doc == null) return null;

            Directory.CreateDirectory(AutoSaveDirectory);

            string rawName = string.IsNullOrWhiteSpace(doc.ProjectName) ? "Untitled" : doc.ProjectName;
            string safeName = string.Join("_", rawName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "Sequence";

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string rollingFileName = $"{safeName}_autosave_{timestamp}{ProjectSerializer.DefaultExtension}";
            string rollingPath = Path.Combine(AutoSaveDirectory, rollingFileName);

            string json = ProjectSerializer.ToJson(doc);
            await File.WriteAllTextAsync(rollingPath, json, cancellationToken);

            string latestRecoveryPath = Path.Combine(AutoSaveDirectory, $"latest_recovery{ProjectSerializer.DefaultExtension}");
            await File.WriteAllTextAsync(latestRecoveryPath, json, cancellationToken);

            _hasUnbackedModifications = false;
            LastAutoSaveUtc = DateTime.UtcNow;
            LastAutoSaveFilePath = rollingPath;

            PruneOldBackups();
            _onAutoSaveNotified?.Invoke($"Auto-saved backup ({DateTime.Now:HH:mm:ss})");
            return rollingPath;
        }
        catch
        {
            return null;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public void ClearRecoveryAfterCleanExit()
    {
        try
        {
            string latestRecoveryPath = Path.Combine(AutoSaveDirectory, $"latest_recovery{ProjectSerializer.DefaultExtension}");
            if (File.Exists(latestRecoveryPath))
                File.Delete(latestRecoveryPath);
        }
        catch { }
    }

    public static string? GetLatestRecoveryFile()
    {
        try
        {
            string dir = GetDefaultAutoSaveDirectory();
            string latest = Path.Combine(dir, $"latest_recovery{ProjectSerializer.DefaultExtension}");
            if (File.Exists(latest))
                return latest;

            var newest = new DirectoryInfo(dir)
                .EnumerateFiles($"*{ProjectSerializer.DefaultExtension}")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            return newest?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private void PruneOldBackups()
    {
        try
        {
            var files = new DirectoryInfo(AutoSaveDirectory)
                .EnumerateFiles($"*_autosave_*{ProjectSerializer.DefaultExtension}")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(_maxRollingBackups)
                .ToList();

            foreach (var oldFile in files)
            {
                try { oldFile.Delete(); }
                catch { }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _undoRedo.StateChanged -= OnUndoRedoStateChanged;
        _timer.Dispose();
        _saveLock.Dispose();
    }
}
