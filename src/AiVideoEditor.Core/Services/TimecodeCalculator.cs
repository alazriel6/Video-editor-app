namespace AiVideoEditor.Core.Services;

public static class TimecodeCalculator
{
    public static string SecondsToSmpte(double timeSeconds, double fps = 30.0)
    {
        if (timeSeconds < 0 || double.IsNaN(timeSeconds) || double.IsInfinity(timeSeconds))
            timeSeconds = 0;

        int nominalFps = Math.Max(1, (int)Math.Round(fps));
        long totalFrames = (long)Math.Round(timeSeconds * fps);

        int frames = (int)(totalFrames % nominalFps);
        long totalSeconds = totalFrames / nominalFps;
        int seconds = (int)(totalSeconds % 60);
        int minutes = (int)((totalSeconds / 60) % 60);
        int hours = (int)(totalSeconds / 3600);

        return $"{hours:00}:{minutes:00}:{seconds:00}:{frames:00}";
    }

    public static long SecondsToFrames(double timeSeconds, double fps = 30.0)
    {
        return Math.Max(0, (long)Math.Round(timeSeconds * Math.Max(1.0, fps)));
    }

    public static double FramesToSeconds(long frames, double fps = 30.0)
    {
        return Math.Max(0.0, frames / Math.Max(1.0, fps));
    }

    public static bool TryParseSmpte(string smpte, double fps, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(smpte)) return false;

        var parts = smpte.Trim().Split(':');
        if (parts.Length != 4) return false;

        if (int.TryParse(parts[0], out int h) &&
            int.TryParse(parts[1], out int m) &&
            int.TryParse(parts[2], out int s) &&
            int.TryParse(parts[3], out int f))
        {
            seconds = h * 3600 + m * 60 + s + (f / Math.Max(1.0, fps));
            return true;
        }
        return false;
    }
}
