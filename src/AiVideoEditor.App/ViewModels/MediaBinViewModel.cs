using System.Collections.ObjectModel;
using AiVideoEditor.AiEngine;
using AiVideoEditor.Core.Commands;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using AiVideoEditor.MediaEngine.Audio;
using AiVideoEditor.MediaEngine.Decoding;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiVideoEditor.App.ViewModels;

public sealed class EffectLibraryItem
{
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Badge { get; init; } = string.Empty;
}

public partial class MediaBinViewModel : ViewModelBase
{
    private readonly VideoFrameReader _frameReader;
    private readonly TimelineService _timelineService;
    private readonly HybridAiRouter _automationRouter;
    private readonly UndoRedoManager _undoRedo;
    private readonly Action _onTimelineChanged;
    private readonly Action<TimelineClip?> _onClipCreatedSelect;
    private readonly Action<string>? _onApplyEffectCode;
    private readonly Action<string> _setStatus;
    private ProjectDocument _document;

    public ObservableCollection<MediaAsset> Assets { get; } = new();
    public ObservableCollection<EffectLibraryItem> FilteredEffectItems { get; } = new();

    private readonly List<EffectLibraryItem> _allEffectItems = new()
    {
        // Video Transitions (Section 5.1)
        new() { Title = "Cross Dissolve", Category = "Video Transitions", Description = "Smooth opacity blend between clips", Code = "Trans:CrossDissolve", Badge = "TRANSITION" },
        new() { Title = "Dip to Black", Category = "Video Transitions", Description = "Fade out to black and fade in", Code = "Trans:DipToBlack", Badge = "TRANSITION" },
        new() { Title = "Dip to White", Category = "Video Transitions", Description = "Bright flash fade through white", Code = "Trans:DipToWhite", Badge = "TRANSITION" },
        new() { Title = "Wipe / Slide Left", Category = "Video Transitions", Description = "Horizontal cubic slide transition", Code = "Trans:SlideLeft", Badge = "TRANSITION" },
        new() { Title = "Zoom In Blur", Category = "Video Transitions", Description = "Dynamic scale zoom transition", Code = "Trans:ZoomBlur", Badge = "TRANSITION" },
        new() { Title = "Flash Cut", Category = "Video Transitions", Description = "High-energy exposure flash cut", Code = "Trans:FlashCut", Badge = "TRANSITION" },
        new() { Title = "Digital Glitch", Category = "Video Transitions", Description = "RGB split scanline glitch cut", Code = "Trans:Glitch", Badge = "TRANSITION" },

        // Video Effects (Section 5.2)
        new() { Title = "Camera Shake (Handheld)", Category = "Video Effects", Description = "Organic handheld motion energy", Code = "Fx:CameraShake", Badge = "VIDEO FX" },
        new() { Title = "Gaussian Blur", Category = "Video Effects", Description = "Smooth cinematic defocus blur", Code = "Fx:GaussianBlur", Badge = "VIDEO FX" },
        new() { Title = "RGB Chromatic Aberration", Category = "Video Effects", Description = "Anamorphic lens RGB channel fringe", Code = "Fx:ChromaticAberration", Badge = "VIDEO FX" },
        new() { Title = "35mm Film Grain", Category = "Video Effects", Description = "Organic cinema luma texture", Code = "Fx:FilmGrain", Badge = "VIDEO FX" },
        new() { Title = "Cinematic Vignette", Category = "Video Effects", Description = "Soft radial edge darkening", Code = "Fx:Vignette", Badge = "VIDEO FX" },
        new() { Title = "Drop Shadow (PIP)", Category = "Video Effects", Description = "Soft shadow behind overlay video", Code = "Fx:DropShadow", Badge = "VIDEO FX" },
        new() { Title = "Remove Background", Category = "Video Effects", Description = "Isolate subject from background", Code = "Fx:RemoveBackground", Badge = "AI FX" },

        // Color Presets (Section 6.1)
        new() { Title = "Cinematic Teal & Orange", Category = "Color Presets", Description = "Cool shadows with warm skin tones", Code = "Color:Cinematic Teal & Orange", Badge = "COLOR LUT" },
        new() { Title = "Warm Golden Hour", Category = "Color Presets", Description = "Golden sunset warmth and glow", Code = "Color:Warm Golden Hour", Badge = "COLOR LUT" },
        new() { Title = "Moody Matte Film", Category = "Color Presets", Description = "Lifted matte blacks & soft contrast", Code = "Color:Moody Matte Film", Badge = "COLOR LUT" },
        new() { Title = "Crisp Black & White", Category = "Color Presets", Description = "High-contrast monochrome cinema", Code = "Color:Crisp Black & White", Badge = "COLOR LUT" },
        new() { Title = "Cyberpunk Neon", Category = "Color Presets", Description = "Vibrant magenta and cyan push", Code = "Color:Cyberpunk Neon", Badge = "COLOR LUT" },
        new() { Title = "Vintage Kodak", Category = "Color Presets", Description = "Nostalgic warm analog film stock", Code = "Color:Vintage Kodak", Badge = "COLOR LUT" },

        // Audio Effects (Section 5.3)
        new() { Title = "Clean Voice Noise", Category = "Audio Effects", Description = "Remove background hum & boost clarity", Code = "Audio:CleanVoice", Badge = "AUDIO FX" },
        new() { Title = "Podcast Voice EQ", Category = "Audio Effects", Description = "Warm broadcast vocal presence + limiter", Code = "Audio:PodcastVoice", Badge = "AUDIO FX" },
        new() { Title = "Bass Boost", Category = "Audio Effects", Description = "Deep low-end punch for music tracks", Code = "Audio:BassBoost", Badge = "AUDIO FX" },
        new() { Title = "Studio Reverb", Category = "Audio Effects", Description = "Natural acoustic room reflection", Code = "Audio:StudioReverb", Badge = "AUDIO FX" }
    };

    [ObservableProperty]
    private MediaAsset? _selectedAsset;

    [ObservableProperty]
    private string _selectedBinFilter = "All Media";

    [ObservableProperty]
    private string _effectsSearchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedEffectsCategory = "All";

    [ObservableProperty]
    private string _ttsInputText = "Welcome to Vortex Cut Studio.";

    [ObservableProperty]
    private string _selectedVoicePreset = "Narrator (Male)";

    [ObservableProperty]
    private string _sfxPromptText = "Cinematic whoosh transition";

    [ObservableProperty]
    private bool _isGeneratingAudio;

    public IReadOnlyList<string> BinFilters { get; } = new[]
    {
        "All Media",
        "Videos & Images",
        "Music & SFX",
        "Voiceovers"
    };

    public IReadOnlyList<string> EffectsCategories { get; } = new[]
    {
        "All",
        "Video Transitions",
        "Video Effects",
        "Color Presets",
        "Audio Effects"
    };

    public IReadOnlyList<string> VoicePresets { get; } = new[]
    {
        "Narrator (Male)",
        "Studio Host (Female)"
    };

    public IReadOnlyList<string> QuickSfxPresets { get; } = new[]
    {
        "Whoosh Transition",
        "Digital Glitch",
        "Deep Bass Impact",
        "Soft Bell Chime"
    };

    public MediaBinViewModel(
        ProjectDocument document,
        VideoFrameReader frameReader,
        TimelineService timelineService,
        HybridAiRouter automationRouter,
        UndoRedoManager undoRedo,
        Action onTimelineChanged,
        Action<TimelineClip?> onClipCreatedSelect,
        Action<string> setStatus,
        Action<string>? onApplyEffectCode = null)
    {
        _document = document;
        _frameReader = frameReader;
        _timelineService = timelineService;
        _automationRouter = automationRouter;
        _undoRedo = undoRedo;
        _onTimelineChanged = onTimelineChanged;
        _onClipCreatedSelect = onClipCreatedSelect;
        _setStatus = setStatus;
        _onApplyEffectCode = onApplyEffectCode;

        RefreshEffectsFilter();
        SyncFromDocument(document);
    }

    partial void OnEffectsSearchQueryChanged(string value) => RefreshEffectsFilter();
    partial void OnSelectedEffectsCategoryChanged(string value) => RefreshEffectsFilter();

    private void RefreshEffectsFilter()
    {
        FilteredEffectItems.Clear();
        foreach (var item in _allEffectItems)
        {
            bool catMatch = SelectedEffectsCategory == "All" ||
                            item.Category.Equals(SelectedEffectsCategory, StringComparison.OrdinalIgnoreCase);
            bool queryMatch = string.IsNullOrWhiteSpace(EffectsSearchQuery) ||
                              item.Title.Contains(EffectsSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                              item.Description.Contains(EffectsSearchQuery, StringComparison.OrdinalIgnoreCase);
            if (catMatch && queryMatch)
                FilteredEffectItems.Add(item);
        }
    }

    [RelayCommand]
    public void ApplyEffectItem(EffectLibraryItem? item)
    {
        if (item == null) return;
        _onApplyEffectCode?.Invoke(item.Code);
    }

    [RelayCommand]
    public void SelectEffectsCategory(string category)
    {
        if (!string.IsNullOrWhiteSpace(category))
            SelectedEffectsCategory = category;
    }

    public void SyncFromDocument(ProjectDocument document)
    {
        _document = document;
        Assets.Clear();
        foreach (var item in _document.MediaPool)
        {
            Assets.Add(item);
        }
        SelectedAsset = Assets.FirstOrDefault();
    }

    public void ImportFiles(IEnumerable<string> filePaths)
    {
        int imported = 0;
        TimelineClip? lastCreatedClip = null;

        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            MediaAssetType type = ext switch
            {
                ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => MediaAssetType.Audio,
                ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" => MediaAssetType.Image,
                _ => MediaAssetType.Video
            };

            var (dur, w, h, fps, hasVideo, hasAudio) = _frameReader.ProbeMediaDetailed(path);
            double validDur = Math.Max(0.5, Math.Round(dur, 2));
            float[] peaks = (type != MediaAssetType.Image && hasAudio)
                ? WaveformGenerator.GeneratePeaks(path, 360, out string pkfPath)
                : Array.Empty<float>();
            string cachePath = (type != MediaAssetType.Image && hasAudio)
                ? Path.Combine(WaveformGenerator.GetPeaksCacheFolder(), $"asset_{Math.Abs(path.GetHashCode()):X8}.pkf")
                : string.Empty;

            var asset = new MediaAsset
            {
                Name = Path.GetFileName(path),
                BinName = "All Media",
                FilePath = path,
                WaveformCachePath = cachePath,
                Type = type,
                Duration = validDur,
                MarkIn = 0.0,
                MarkOut = validDur,
                Width = w > 0 ? w : 1920,
                Height = h > 0 ? h : 1080,
                Fps = fps > 0 ? fps : 30.0,
                HasVideo = type is MediaAssetType.Video or MediaAssetType.Image && hasVideo,
                HasAudio = type != MediaAssetType.Image && hasAudio,
                WaveformPeaks = peaks
            };

            if (asset.HasVideo)
            {
                ThumbnailStripService.EnsurePosterThumbnail(asset, _frameReader);
                ThumbnailStripService.GetOrBuildFilmstrip(asset, _frameReader, 6);
            }

            _document.MediaPool.Add(asset);
            Assets.Add(asset);
            SelectedAsset = asset;
            lastCreatedClip = AddAssetToTimeline(asset);
            imported++;
        }

        if (lastCreatedClip != null)
        {
            _onClipCreatedSelect(lastCreatedClip);
        }

        if (imported > 0 && SelectedAsset != null)
        {
            _setStatus($"Imported '{SelectedAsset.Name}' ({SelectedAsset.Width}x{SelectedAsset.Height} @ {SelectedAsset.Fps:0.##}fps, {SelectedAsset.Duration:0.00}s).");
        }
    }

    [RelayCommand]
    public void AddSelectedAssetToTimeline()
    {
        if (SelectedAsset == null) return;
        var created = AddAssetToTimeline(SelectedAsset);
        if (created != null)
        {
            _onClipCreatedSelect(created);
        }
    }

    public TimelineClip? AddAssetToTimeline(MediaAsset asset, double? atTime = null)
    {
        TimelineClip? primaryClip = null;
        _undoRedo.RecordSnapshot(_document, $"Insert '{asset.Name}'", () =>
        {
            primaryClip = _timelineService.AddAssetWithLinkedAudioToTimeline(_document, asset, atTime);
        }, _ => { });

        _onTimelineChanged();
        return primaryClip;
    }

    [RelayCommand]
    public async Task GenerateAiVoiceoverAsync()
    {
        if (string.IsNullOrWhiteSpace(TtsInputText) || IsGeneratingAudio) return;

        IsGeneratingAudio = true;
        _setStatus("Generating AI Voiceover...");
        try
        {
            string wavPath = await _automationRouter.GenerateVoiceoverAsync(TtsInputText, SelectedVoicePreset);
            var (dur, _, _, _) = _frameReader.ProbeMedia(wavPath);
            double validDur = Math.Max(1.2, Math.Round(dur, 2));
            var peaks = WaveformGenerator.GeneratePeaks(wavPath, 160, out string pkfPath);

            var asset = new MediaAsset
            {
                Name = $"Voiceover_{(TtsInputText.Length > 18 ? TtsInputText[..18].Replace(' ', '_') : TtsInputText.Replace(' ', '_'))}.wav",
                BinName = "Voiceovers",
                FilePath = wavPath,
                WaveformCachePath = pkfPath,
                Type = MediaAssetType.GeneratedVoiceover,
                Duration = validDur,
                MarkIn = 0,
                MarkOut = validDur,
                HasVideo = false,
                HasAudio = true,
                WaveformPeaks = peaks
            };

            _document.MediaPool.Add(asset);
            Assets.Add(asset);
            SelectedAsset = asset;
            var clip = AddAssetToTimeline(asset, 0.0);
            if (clip != null) _onClipCreatedSelect(clip);
            _setStatus($"Added Voiceover '{asset.Name}' ({asset.Duration:0.1}s).");
        }
        finally
        {
            IsGeneratingAudio = false;
        }
    }

    [RelayCommand]
    public async Task GenerateAiSfxAsync(string? presetOverride = null)
    {
        string prompt = !string.IsNullOrWhiteSpace(presetOverride) ? presetOverride : SfxPromptText;
        if (string.IsNullOrWhiteSpace(prompt) || IsGeneratingAudio) return;

        IsGeneratingAudio = true;
        _setStatus($"Generating Sound Effect '{prompt}'...");
        try
        {
            string wavPath = await _automationRouter.GenerateSoundEffectAsync(prompt, 2.0);
            var peaks = WaveformGenerator.GeneratePeaks(wavPath, 100, out string pkfPath);
            var asset = new MediaAsset
            {
                Name = $"SFX_{prompt.Replace(' ', '_')}.wav",
                BinName = "Music & SFX",
                FilePath = wavPath,
                WaveformCachePath = pkfPath,
                Type = MediaAssetType.GeneratedSfx,
                Duration = 2.0,
                MarkIn = 0,
                MarkOut = 2.0,
                HasVideo = false,
                HasAudio = true,
                WaveformPeaks = peaks
            };

            _document.MediaPool.Add(asset);
            Assets.Add(asset);
            SelectedAsset = asset;
            var clip = AddAssetToTimeline(asset);
            if (clip != null) _onClipCreatedSelect(clip);
            _setStatus($"Added Sound Effect '{asset.Name}' to Music & SFX track.");
        }
        finally
        {
            IsGeneratingAudio = false;
        }
    }
}
