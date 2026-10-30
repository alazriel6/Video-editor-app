using System.Globalization;
using System.Text;
using System.Xml.Linq;
using AiVideoEditor.Core.Models;

namespace AiVideoEditor.Core.Services;

public static class ProjectInterchangeExporter
{
    /// <summary>
    /// Exports the sequence to a Final Cut Pro 7 XML (xmeml v5) file compatible with DaVinci Resolve, Adobe Premiere Pro, and Final Cut Pro.
    /// </summary>
    public static async Task ExportFcp7XmlAsync(ProjectDocument document, string outputPath)
    {
        string xml = GenerateFcp7Xml(document);
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(outputPath, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Exports the sequence to a standard SMPTE CMX 3600 EDL (.edl) file for DaVinci Resolve, Avid Media Composer, and Premiere Pro.
    /// </summary>
    public static async Task ExportCmx3600EdlAsync(ProjectDocument document, string outputPath)
    {
        string edl = GenerateCmx3600Edl(document);
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(outputPath, edl, Encoding.ASCII);
    }

    public static string GenerateFcp7Xml(ProjectDocument document)
    {
        double fps = document.Fps > 0 ? document.Fps : 30.0;
        int timebase = (int)Math.Round(fps);
        bool isNtsc = Math.Abs(fps - 23.976) < 0.05 || Math.Abs(fps - 29.97) < 0.05 || Math.Abs(fps - 59.94) < 0.05;
        long totalFrames = SecondsToFrames(document.TotalDuration, fps);

        var emittedFileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var videoTracksXml = new List<XElement>();
        var videoTracks = document.Tracks.Where(t => t.Type == TrackType.Video).Reverse().ToList();

        foreach (var track in videoTracks)
        {
            var trackElem = new XElement("track",
                new XElement("enabled", track.IsVisible && !track.IsMuted ? "TRUE" : "FALSE"),
                new XElement("locked", track.IsLocked ? "TRUE" : "FALSE"));

            int clipIdx = 1;
            foreach (var clip in track.Clips.OrderBy(c => c.TimelineStart))
            {
                if (clip.Kind == ClipKind.Transition)
                {
                    long tStart = SecondsToFrames(clip.TimelineStart, fps);
                    long tEnd = SecondsToFrames(clip.TimelineStart + clip.Duration, fps);
                    trackElem.Add(new XElement("transitionitem",
                        new XElement("start", tStart),
                        new XElement("end", tEnd),
                        new XElement("alignment", "center"),
                        new XElement("effect",
                            new XElement("name", clip.TransitionEffectType ?? "Cross Dissolve"),
                            new XElement("effectid", "Cross Dissolve"),
                            new XElement("effecttype", "transition"),
                            new XElement("mediatype", "video"))));
                    continue;
                }

                var asset = document.FindAsset(clip.MediaAssetId);
                long startFrame = SecondsToFrames(clip.TimelineStart, fps);
                long endFrame = SecondsToFrames(clip.TimelineStart + clip.Duration, fps);
                long inFrame = SecondsToFrames(clip.SourceIn, fps);
                long outFrame = SecondsToFrames(clip.SourceOut > clip.SourceIn ? clip.SourceOut : (clip.SourceIn + clip.Duration * clip.Speed), fps);

                string clipItemId = $"clipitem-v-{track.TrackCode}-{clipIdx++}";
                var clipItem = new XElement("clipitem",
                    new XAttribute("id", clipItemId),
                    new XElement("name", string.IsNullOrWhiteSpace(clip.Label) ? (asset?.Name ?? "Clip") : clip.Label),
                    new XElement("enabled", "TRUE"),
                    new XElement("duration", Math.Max(outFrame - inFrame, endFrame - startFrame)),
                    BuildRateElement(timebase, isNtsc),
                    new XElement("start", startFrame),
                    new XElement("end", endFrame),
                    new XElement("in", inFrame),
                    new XElement("out", outFrame));

                if (asset != null)
                {
                    clipItem.Add(BuildFileElement(asset, timebase, isNtsc, emittedFileIds));
                }

                if (Math.Abs(clip.Opacity - 1.0f) > 0.005f)
                {
                    clipItem.Add(new XElement("filter",
                        new XElement("effect",
                            new XElement("name", "Basic Motion"),
                            new XElement("effectid", "basic"),
                            new XElement("effectcategory", "motion"),
                            new XElement("effecttype", "motion"),
                            new XElement("mediatype", "video"),
                            new XElement("parameter",
                                new XElement("parameterid", "opacity"),
                                new XElement("name", "Opacity"),
                                new XElement("value", (clip.Opacity * 100.0f).ToString("0.##", CultureInfo.InvariantCulture))))));
                }

                if (Math.Abs(clip.Speed - 1.0) > 0.005)
                {
                    clipItem.Add(new XElement("filter",
                        new XElement("effect",
                            new XElement("name", "Time Remap"),
                            new XElement("effectid", "timeremap"),
                            new XElement("effectcategory", "motion"),
                            new XElement("effecttype", "motion"),
                            new XElement("mediatype", "video"),
                            new XElement("parameter",
                                new XElement("parameterid", "speed"),
                                new XElement("name", "speed"),
                                new XElement("value", (clip.Speed * 100.0).ToString("0.##", CultureInfo.InvariantCulture))),
                            new XElement("parameter",
                                new XElement("parameterid", "reverse"),
                                new XElement("name", "reverse"),
                                new XElement("value", clip.ReversePlayback ? "TRUE" : "FALSE")))));
                }

                trackElem.Add(clipItem);
            }

            videoTracksXml.Add(trackElem);
        }

        var audioTracksXml = new List<XElement>();
        var audioTracks = document.Tracks.Where(t => t.Type == TrackType.Audio).ToList();

        foreach (var track in audioTracks)
        {
            var trackElem = new XElement("track",
                new XElement("enabled", !track.IsMuted ? "TRUE" : "FALSE"),
                new XElement("locked", track.IsLocked ? "TRUE" : "FALSE"));

            int clipIdx = 1;
            foreach (var clip in track.Clips.Where(c => c.Kind != ClipKind.Transition).OrderBy(c => c.TimelineStart))
            {
                var asset = document.FindAsset(clip.MediaAssetId);
                long startFrame = SecondsToFrames(clip.TimelineStart, fps);
                long endFrame = SecondsToFrames(clip.TimelineStart + clip.Duration, fps);
                long inFrame = SecondsToFrames(clip.SourceIn, fps);
                long outFrame = SecondsToFrames(clip.SourceOut > clip.SourceIn ? clip.SourceOut : (clip.SourceIn + clip.Duration * clip.Speed), fps);

                var clipItem = new XElement("clipitem",
                    new XAttribute("id", $"clipitem-a-{track.TrackCode}-{clipIdx++}"),
                    new XElement("name", string.IsNullOrWhiteSpace(clip.Label) ? (asset?.Name ?? "Audio Clip") : clip.Label),
                    new XElement("enabled", "TRUE"),
                    new XElement("duration", Math.Max(outFrame - inFrame, endFrame - startFrame)),
                    BuildRateElement(timebase, isNtsc),
                    new XElement("start", startFrame),
                    new XElement("end", endFrame),
                    new XElement("in", inFrame),
                    new XElement("out", outFrame));

                if (asset != null)
                {
                    clipItem.Add(BuildFileElement(asset, timebase, isNtsc, emittedFileIds));
                }

                if (Math.Abs(clip.GainDb) > 0.05f)
                {
                    float linearGain = MathF.Pow(10f, clip.GainDb / 20f);
                    clipItem.Add(new XElement("filter",
                        new XElement("effect",
                            new XElement("name", "Audio Levels"),
                            new XElement("effectid", "audiolevels"),
                            new XElement("effecttype", "audiolevels"),
                            new XElement("mediatype", "audio"),
                            new XElement("parameter",
                                new XElement("parameterid", "level"),
                                new XElement("name", "Level"),
                                new XElement("value", linearGain.ToString("0.####", CultureInfo.InvariantCulture))))));
                }

                trackElem.Add(clipItem);
            }

            audioTracksXml.Add(trackElem);
        }

        var xdoc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("xmeml", null, null, null),
            new XElement("xmeml",
                new XAttribute("version", "5"),
                new XElement("sequence",
                    new XAttribute("id", "sequence-1"),
                    new XElement("name", document.ProjectName ?? "Vortex Cut Sequence"),
                    new XElement("duration", totalFrames),
                    BuildRateElement(timebase, isNtsc),
                    new XElement("timecode",
                        BuildRateElement(timebase, isNtsc),
                        new XElement("string", "00:00:00:00"),
                        new XElement("frame", 0),
                        new XElement("displayformat", "NDF")),
                    new XElement("media",
                        new XElement("video",
                            new XElement("format",
                                new XElement("samplecharacteristics",
                                    BuildRateElement(timebase, isNtsc),
                                    new XElement("width", document.Width),
                                    new XElement("height", document.Height),
                                    new XElement("anamorphic", "FALSE"),
                                    new XElement("pixelaspectratio", "square"),
                                    new XElement("fielddominance", "none"))),
                            videoTracksXml),
                        new XElement("audio",
                            new XElement("numOutputChannels", 2),
                            new XElement("format",
                                new XElement("samplecharacteristics",
                                    new XElement("depth", 16),
                                    new XElement("samplerate", document.Settings.SampleRate > 0 ? document.Settings.SampleRate : 48000))),
                            audioTracksXml)))));

        using var sw = new StringWriter(CultureInfo.InvariantCulture);
        xdoc.Save(sw);
        return sw.ToString();
    }

    public static string GenerateCmx3600Edl(ProjectDocument document)
    {
        double fps = document.Fps > 0 ? document.Fps : 30.0;
        var sb = new StringBuilder();
        string title = string.IsNullOrWhiteSpace(document.ProjectName) ? "VORTEX_SEQUENCE" : document.ProjectName.ToUpperInvariant();
        sb.AppendLine($"TITLE: {title}");
        sb.AppendLine("FCM: NON-DROP FRAME");
        sb.AppendLine();

        int eventIndex = 1;

        var primaryVideoTracks = document.Tracks
            .Where(t => t.Type == TrackType.Video && t.IsVisible && !t.IsMuted)
            .Reverse()
            .ToList();

        foreach (var track in primaryVideoTracks)
        {
            foreach (var clip in track.Clips.Where(c => c.Kind != ClipKind.Transition).OrderBy(c => c.TimelineStart))
            {
                var asset = document.FindAsset(clip.MediaAssetId);
                string reelName = asset != null ? FormatEdlReelName(asset.Name) : "BL      ";

                double srcIn = clip.SourceIn;
                double srcOut = clip.SourceOut > clip.SourceIn ? clip.SourceOut : (clip.SourceIn + clip.Duration * clip.Speed);
                double recIn = clip.TimelineStart;
                double recOut = clip.TimelineStart + clip.Duration;

                string tcSrcIn = TimecodeCalculator.SecondsToSmpte(srcIn, fps);
                string tcSrcOut = TimecodeCalculator.SecondsToSmpte(srcOut, fps);
                string tcRecIn = TimecodeCalculator.SecondsToSmpte(recIn, fps);
                string tcRecOut = TimecodeCalculator.SecondsToSmpte(recOut, fps);

                bool hasDissolve = clip.InTransition != null && clip.InTransition.IsEnabled && clip.InTransition.Duration > 0.01;
                string transCode = hasDissolve
                    ? $"D    {(int)Math.Round(clip.InTransition!.Duration * fps):D3}"
                    : "C       ";

                sb.AppendLine($"{eventIndex:D3}  {reelName} V     {transCode} {tcSrcIn} {tcSrcOut} {tcRecIn} {tcRecOut}");
                sb.AppendLine($"* FROM CLIP NAME: {(string.IsNullOrWhiteSpace(clip.Label) ? (asset?.Name ?? "Clip") : clip.Label)}");
                if (asset != null && !string.IsNullOrWhiteSpace(asset.FilePath))
                {
                    sb.AppendLine($"* SOURCE FILE: {Path.GetFileName(asset.FilePath)}");
                }
                if (Math.Abs(clip.Speed - 1.0) > 0.005)
                {
                    double eventFps = fps * clip.Speed * (clip.ReversePlayback ? -1.0 : 1.0);
                    sb.AppendLine($"M2   {reelName}       {eventFps.ToString("000.0", CultureInfo.InvariantCulture)}                {tcSrcIn}");
                }
                sb.AppendLine();
                eventIndex++;
            }
        }

        var audioTracks = document.Tracks.Where(t => t.Type == TrackType.Audio && !t.IsMuted).ToList();
        foreach (var track in audioTracks)
        {
            foreach (var clip in track.Clips.Where(c => c.Kind != ClipKind.Transition).OrderBy(c => c.TimelineStart))
            {
                var asset = document.FindAsset(clip.MediaAssetId);
                string reelName = asset != null ? FormatEdlReelName(asset.Name) : "AX      ";

                double srcIn = clip.SourceIn;
                double srcOut = clip.SourceOut > clip.SourceIn ? clip.SourceOut : (clip.SourceIn + clip.Duration * clip.Speed);
                double recIn = clip.TimelineStart;
                double recOut = clip.TimelineStart + clip.Duration;

                string tcSrcIn = TimecodeCalculator.SecondsToSmpte(srcIn, fps);
                string tcSrcOut = TimecodeCalculator.SecondsToSmpte(srcOut, fps);
                string tcRecIn = TimecodeCalculator.SecondsToSmpte(recIn, fps);
                string tcRecOut = TimecodeCalculator.SecondsToSmpte(recOut, fps);

                sb.AppendLine($"{eventIndex:D3}  {reelName} AA    C        {tcSrcIn} {tcSrcOut} {tcRecIn} {tcRecOut}");
                sb.AppendLine($"* FROM CLIP NAME: {(string.IsNullOrWhiteSpace(clip.Label) ? (asset?.Name ?? "Audio") : clip.Label)}");
                if (asset != null && !string.IsNullOrWhiteSpace(asset.FilePath))
                {
                    sb.AppendLine($"* SOURCE FILE: {Path.GetFileName(asset.FilePath)}");
                }
                sb.AppendLine();
                eventIndex++;
            }
        }

        return sb.ToString();
    }

    private static XElement BuildRateElement(int timebase, bool isNtsc) =>
        new("rate",
            new XElement("timebase", timebase),
            new XElement("ntsc", isNtsc ? "TRUE" : "FALSE"));

    private static XElement BuildFileElement(MediaAsset asset, int timebase, bool isNtsc, HashSet<string> emittedFileIds)
    {
        string fileId = $"file-{asset.AssetId}";
        if (!emittedFileIds.Add(fileId))
        {
            return new XElement("file", new XAttribute("id", fileId));
        }

        string pathUrl = string.Empty;
        if (!string.IsNullOrWhiteSpace(asset.FilePath))
        {
            try
            {
                var uri = new Uri(Path.GetFullPath(asset.FilePath));
                pathUrl = uri.AbsoluteUri;
            }
            catch
            {
                pathUrl = $"file://localhost/{asset.FilePath.Replace('\\', '/')}";
            }
        }

        long durFrames = Math.Max(1, asset.DurationFrames);
        return new XElement("file",
            new XAttribute("id", fileId),
            new XElement("name", asset.Name),
            new XElement("pathurl", pathUrl),
            BuildRateElement(timebase, isNtsc),
            new XElement("duration", durFrames),
            new XElement("media",
                asset.HasVideo
                    ? new XElement("video",
                        new XElement("samplecharacteristics",
                            BuildRateElement(timebase, isNtsc),
                            new XElement("width", asset.Width),
                            new XElement("height", asset.Height)))
                    : null,
                asset.HasAudio
                    ? new XElement("audio",
                        new XElement("channelcount", asset.Channels > 0 ? asset.Channels : 2),
                        new XElement("samplecharacteristics",
                            new XElement("depth", 16),
                            new XElement("samplerate", asset.SampleRate > 0 ? asset.SampleRate : 48000)))
                    : null));
    }

    private static long SecondsToFrames(double seconds, double fps) =>
        (long)Math.Round(Math.Max(0, seconds) * Math.Max(1.0, fps));

    private static string FormatEdlReelName(string rawName)
    {
        string clean = new string(Path.GetFileNameWithoutExtension(rawName)
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(clean))
            clean = "AX";
        if (clean.Length > 8)
            clean = clean[..8];
        return clean.PadRight(8, ' ');
    }
}
