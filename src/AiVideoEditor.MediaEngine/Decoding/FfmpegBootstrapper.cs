using System.Runtime.InteropServices;
using FFMediaToolkit;

namespace AiVideoEditor.MediaEngine.Decoding;

public static class FfmpegBootstrapper
{
    private static bool _initialized;
    public static bool IsAvailable { get; private set; }
    public static string LoadedPath { get; private set; } = string.Empty;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var candidates = new List<string>();

            string appDataFfmpeg = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AiVideoEditor", "ffmpeg");
            candidates.Add(appDataFfmpeg);
            candidates.Add(AppDomain.CurrentDomain.BaseDirectory);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                candidates.Add(@"C:\ffmpeg\bin");
                candidates.Add(@"C:\Program Files\ffmpeg\bin");
                candidates.Add(@"C:\ProgramData\chocolatey\lib\ffmpeg\tools\ffmpeg\bin");

                string? pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (Directory.Exists(p))
                            candidates.Add(p);
                    }
                }

                // Check Winget packages folder for Gyan.FFmpeg / BtbN
                string wingetPkg = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WinGet", "Packages");
                if (Directory.Exists(wingetPkg))
                {
                    foreach (var dir in Directory.GetDirectories(wingetPkg, "*FFmpeg*"))
                    {
                        var binDirs = Directory.GetDirectories(dir, "bin", SearchOption.AllDirectories);
                        candidates.AddRange(binDirs);
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                candidates.Add("/opt/homebrew/lib");
                candidates.Add("/usr/local/lib");
            }
            else
            {
                candidates.Add("/usr/lib/x86_64-linux-gnu");
                candidates.Add("/usr/lib");
            }

            foreach (var dir in candidates.Distinct())
            {
                if (!Directory.Exists(dir)) continue;

                bool hasAvCodec = Directory.GetFiles(dir, "avcodec*.dll").Length > 0
                                  || Directory.GetFiles(dir, "libavcodec*.so*").Length > 0
                                  || Directory.GetFiles(dir, "libavcodec*.dylib").Length > 0;

                if (hasAvCodec)
                {
                    FFmpegLoader.FFmpegPath = dir;
                    LoadedPath = dir;
                    IsAvailable = true;
                    return;
                }
            }
        }
        catch
        {
            IsAvailable = false;
        }
    }
}
