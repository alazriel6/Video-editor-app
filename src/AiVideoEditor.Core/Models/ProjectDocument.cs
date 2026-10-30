using System.Text.Json.Serialization;

namespace AiVideoEditor.Core.Models;

public enum MediaAssetType
{
    Video,
    Audio,
    Image,
    GeneratedVoiceover,
    GeneratedSfx
}

public class TimelineMarker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double TimeSeconds { get; set; }
    public string Label { get; set; } = "Marker";
    public string ColorHex { get; set; } = "#FFF59E0B";
}

public class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AssetId => $"asset_{Id.ToString("N")[..6]}";
    public string Name { get; set; } = string.Empty;
    public string BinName { get; set; } = "Master Bin";
    public string FilePath { get; set; } = string.Empty;
    public string RelativeFilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ProxyPath { get; set; } = string.Empty;
    public string WaveformCachePath { get; set; } = string.Empty;
    public string PosterThumbnailPath { get; set; } = string.Empty;
    public MediaAssetType Type { get; set; } = MediaAssetType.Video;
    public double Duration { get; set; }
    public long DurationFrames => (long)Math.Round(Duration * Math.Max(1.0, Fps));
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double Fps { get; set; } = 30.0;
    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 2;
    public string VideoCodec { get; set; } = "H.264";
    public string ColorSpace { get; set; } = "Rec.709";
    public bool HasVideo { get; set; } = true;
    public bool HasAudio { get; set; } = true;
    public double MarkIn { get; set; } = 0.0;
    public double MarkOut { get; set; } = 5.0;
    public float[] WaveformPeaks { get; set; } = Array.Empty<float>();
    public List<TranscriptSegment> Transcript { get; set; } = new();

    [JsonIgnore]
    public bool IsOffline => !string.IsNullOrWhiteSpace(FilePath) && !File.Exists(FilePath);

    [JsonIgnore]
    public string ResolutionSummary => IsOffline
        ? "OFFLINE • Relink Required"
        : (HasVideo ? $"{Width}x{Height} • {Fps:0.##} fps" : $"{SampleRate / 1000.0:0.#} kHz Stereo");
}

public class ProjectSettingsSpec
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int FpsNumerator { get; set; } = 30000;
    public int FpsDenominator { get; set; } = 1000;
    public int SampleRate { get; set; } = 48000;
}

public class ProjectDocument
{
    public string ProjectVersion { get; set; } = "3.0";
    public string ProjectName { get; set; } = "Untitled Project";

    [JsonIgnore]
    public string? CurrentProjectFilePath { get; set; }

    public ProjectSettingsSpec Settings { get; set; } = new();
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double Fps { get; set; } = 30.0;
    public string AspectRatioPreset { get; set; } = "16:9 (1920x1080)";
    public bool AutoDuckingEnabled { get; set; } = false;
    public float AutoDuckingThresholdDb { get; set; } = -24.0f;
    public float AutoDuckingRatio { get; set; } = 0.25f;
    public bool LinkVideoAndAudio { get; set; } = true;
    public double? TimelineMarkIn { get; set; }
    public double? TimelineMarkOut { get; set; }
    public List<MediaAsset> MediaPool { get; set; } = new();
    public List<TimelineTrack> Tracks { get; set; } = new();
    public List<TimelineMarker> Markers { get; set; } = new();

    [JsonIgnore]
    private readonly Dictionary<Guid, MediaAsset> _mediaAssetCache = new();
    [JsonIgnore]
    private int _mediaAssetCacheVersion = -1;

    public void InvalidateAssetCache()
    {
        lock (_mediaAssetCache)
        {
            _mediaAssetCacheVersion = -1;
            _mediaAssetCache.Clear();
        }
    }

    /// <summary>
    /// Thread-safe O(1) media asset lookup with automatic cache synchronization.
    /// </summary>
    public MediaAsset? FindAsset(Guid id)
    {
        if (id == Guid.Empty) return null;

        lock (_mediaAssetCache)
        {
            if (_mediaAssetCacheVersion != MediaPool.Count)
            {
                _mediaAssetCache.Clear();
                for (int i = 0; i < MediaPool.Count; i++)
                {
                    var a = MediaPool[i];
                    _mediaAssetCache[a.Id] = a;
                }
                _mediaAssetCacheVersion = MediaPool.Count;
            }

            return _mediaAssetCache.TryGetValue(id, out var asset) ? asset : null;
        }
    }

    [JsonIgnore]
    public double TotalDuration
    {
        get
        {
            double max = 0;
            foreach (var track in Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    double end = clip.TimelineStart + clip.Duration;
                    if (end > max) max = end;
                }
            }
            return Math.Max(max, 15.0);
        }
    }

    public static ProjectDocument CreateDefault()
    {
        var doc = new ProjectDocument
        {
            ProjectVersion = "3.0",
            ProjectName = "Untitled Project",
            Width = 1920,
            Height = 1080,
            Fps = 30.0
        };

        doc.Tracks.Add(new TimelineTrack
        {
            TrackCode = "C1",
            Name = "Subtitles",
            Type = TrackType.Text,
            Index = 0
        });
        doc.Tracks.Add(new TimelineTrack
        {
            TrackCode = "V2",
            Name = "Video 2 (Overlay)",
            Type = TrackType.Video,
            Index = 1
        });
        doc.Tracks.Add(new TimelineTrack
        {
            TrackCode = "V1",
            Name = "Video 1",
            Type = TrackType.Video,
            Index = 2
        });
        doc.Tracks.Add(new TimelineTrack
        {
            TrackCode = "A1",
            Name = "Audio 1 (Voice)",
            Type = TrackType.Audio,
            Index = 0
        });
        doc.Tracks.Add(new TimelineTrack
        {
            TrackCode = "A2",
            Name = "Audio 2 (Music & SFX)",
            Type = TrackType.Audio,
            Index = 1
        });

        return doc;
    }
}
