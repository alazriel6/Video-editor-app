using System.ComponentModel;
using AiVideoEditor.Core.Interfaces;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using AiVideoEditor.MediaEngine.Audio;
using Microsoft.SemanticKernel;

namespace AiVideoEditor.AiEngine;

public sealed class TimelineAgentPlugin
{
    private readonly ProjectDocument _document;
    private readonly TimelineService _timelineService;
    private readonly IAiSfxService _sfxService;
    public List<string> ActionLog { get; } = new();

    public TimelineAgentPlugin(ProjectDocument document, TimelineService timelineService, IAiSfxService sfxService)
    {
        _document = document;
        _timelineService = timelineService;
        _sfxService = sfxService;
    }

    [KernelFunction("CutClipAt")]
    [Description("Splits/cuts a video or audio clip on the timeline at the given time in seconds.")]
    public string CutClipAt(
        [Description("The GUID of the clip to cut, or empty GUID to cut all clips at the timestamp")] Guid clipId,
        [Description("Timeline position in seconds where the cut should occur")] double timeInSeconds)
    {
        bool ok = clipId != Guid.Empty
            ? _timelineService.CutClipAt(_document, clipId, timeInSeconds)
            : _timelineService.SplitAllClipsAtPlayhead(_document, timeInSeconds) > 0;

        string msg = ok
            ? $"SplitClipCommand executed at {TimecodeCalculator.SecondsToSmpte(timeInSeconds, _document.Fps)}"
            : $"Razor split evaluated at {TimecodeCalculator.SecondsToSmpte(timeInSeconds, _document.Fps)}";
        ActionLog.Add(msg);
        return msg;
    }

    [KernelFunction("SplitEveryInterval")]
    [Description("Splits all timeline clips at regular intervals (e.g. every 5 seconds).")]
    public string SplitEveryInterval(
        [Description("Interval in seconds between cuts, e.g. 5.0")] double intervalSeconds = 5.0)
    {
        int cuts = _timelineService.SplitClipsEveryInterval(_document, intervalSeconds);
        string msg = $"IntervalSplit: Created {cuts} cut(s) every {intervalSeconds:0.0}s across timeline";
        ActionLog.Add(msg);
        return msg;
    }

    [KernelFunction("AddZoomKeyframes")]
    [Description("Adds smooth EaseInOutCubic Scale keyframes (◇) to animate zoom-in or zoom-out on the video clip.")]
    public string AddZoomKeyframes(
        [Description("Start time of zoom in seconds")] double startSeconds = 0.0,
        [Description("End time of zoom in seconds")] double endSeconds = 3.0,
        [Description("Starting scale factor, e.g. 1.0")] double startScale = 1.0,
        [Description("Ending scale factor, e.g. 1.25")] double endScale = 1.25)
    {
        int count = _timelineService.ApplySmoothZoomKeyframes(_document, startSeconds, endSeconds, startScale, endScale);
        string msg = $"AddKeyframe [Scale ◆]: {startScale:0.00}x -> {endScale:0.00}x ({startSeconds:0.0}s .. {endSeconds:0.0}s, EaseInOutCubic) on {count} clip(s)";
        ActionLog.Add(msg);
        return msg;
    }

    [KernelFunction("DeleteTimeRange")]
    [Description("Deletes the video/audio segment between start and end seconds and ripple-shifts subsequent clips.")]
    public string DeleteTimeRange(
        [Description("Start time in seconds")] double start,
        [Description("End time in seconds")] double end)
    {
        int count = _timelineService.DeleteTimeRangeAndRipple(_document, start, end);
        string msg = $"RippleDeleteCommand [{start:0.00}s .. {end:0.00}s] ({count} clip segment(s) closed)";
        ActionLog.Add(msg);
        return msg;
    }

    [KernelFunction("ApplyTransitionToAllClips")]
    [Description("Applies a transition (CrossDissolve, DipToBlack, WipeLeft, ZoomIn, Glitch) to all video clips on the timeline.")]
    public string ApplyTransitionToAllClips(
        [Description("Transition type: CrossDissolve, DipToBlack, WipeLeft, ZoomIn, or Glitch")] string transitionType,
        [Description("Duration of the transition in seconds, e.g., 0.5")] double duration = 0.5)
    {
        int count = _timelineService.ApplyTransitionToAllClips(_document, transitionType, Math.Clamp(duration, 0.1, 2.5));
        int frames = (int)Math.Round(duration * _document.Fps);
        string msg = $"AddTransition: '{transitionType}' ({frames}f / {duration:0.00}s) applied to {count} clip(s)";
        ActionLog.Add(msg);
        return msg;
    }

    [KernelFunction("SetAudioGain")]
    [Description("Normalizes or sets audio gain in dB across clips on the target track.")]
    public string SetAudioGain(
        [Description("Track code such as A1 or A2")] string trackCode = "A1",
        [Description("Target gain in dB, e.g. 0.0 or -3.0")] float gainDb = 0.0f)
    {
        int count = _timelineService.NormalizeTrackAudioGain(_document, trackCode, gainDb);
        string msg = $"SetAudioGain: Normalized {count} clip(s) on {trackCode} to {gainDb:+0.0;-0.0;0.0} dB (-14 LUFS target)";
        ActionLog.Add(msg);
        return msg;
    }

    [KernelFunction("AddTextOverlay")]
    [Description("Adds a subtitle or caption clip onto Track C1.")]
    public string AddTextOverlay(
        [Description("The subtitle or caption text to display")] string text,
        [Description("Timeline start time in seconds")] double start,
        [Description("Duration in seconds")] double duration,
        [Description("Visual style preset: BroadcastBox, YellowCinema, CleanLowerThird, or HighContrastStroke")] string style = "BroadcastBox")
    {
        _timelineService.AddTextOverlay(_document, text, start, duration, style);
        string msg = $"InsertCaption [C1]: \"{text}\" at {TimecodeCalculator.SecondsToSmpte(start, _document.Fps)} ({style})";
        ActionLog.Add(msg);
        return msg;
    }

    [KernelFunction("AddSoundEffect")]
    [Description("Synthesizes a Foley/Atmos cue and inserts it on Track A2 at timelinePosition.")]
    public async Task<string> AddSoundEffect(
        [Description("Acoustic cue description, e.g., whoosh, heavy door creak, sub impact")] string sfxPrompt,
        [Description("Timeline start position in seconds")] double timelinePosition)
    {
        string wavPath = await _sfxService.GenerateSoundEffectAsync(sfxPrompt, 1.8);
        var asset = new MediaAsset
        {
            Name = $"Foley_{sfxPrompt.Replace(' ', '_')}.wav",
            BinName = "Foley & Atmos",
            FilePath = wavPath,
            Type = MediaAssetType.GeneratedSfx,
            Duration = 1.8,
            HasVideo = false,
            HasAudio = true,
            WaveformPeaks = WaveformGenerator.GeneratePeaks(wavPath, 80, out string pkfPath),
            WaveformCachePath = pkfPath
        };
        _document.MediaPool.Add(asset);

        var sfxTrack = _document.Tracks.LastOrDefault(t => t.Type == TrackType.Audio)
                       ?? _document.Tracks.FirstOrDefault(t => t.Type == TrackType.Audio);
        if (sfxTrack == null)
        {
            sfxTrack = new TimelineTrack { TrackCode = "A2", Name = "Foley & Stem Mix", Type = TrackType.Audio };
            _document.Tracks.Add(sfxTrack);
        }

        sfxTrack.Clips.Add(new TimelineClip
        {
            MediaAssetId = asset.Id,
            Label = asset.Name,
            TimelineStart = Math.Max(0, timelinePosition),
            Duration = 1.8,
            SourceIn = 0,
            SourceOut = 1.8,
            GainDb = 0.0f
        });

        string msg = $"InsertFoley [A2]: '{asset.Name}' at {TimecodeCalculator.SecondsToSmpte(timelinePosition, _document.Fps)}";
        ActionLog.Add(msg);
        return msg;
    }

    [KernelFunction("RemoveSilence")]
    [Description("Strips silent regions and performs ripple delete across the timeline.")]
    public string RemoveSilence(
        [Description("Minimum silence threshold in seconds, default 0.5")] double thresholdSeconds = 0.5)
    {
        int removed = _timelineService.AutoRemoveSilences(_document, thresholdSeconds);
        string msg = $"StripSilence: Ripple-cut {removed} dead-air region(s) (> {thresholdSeconds:0.00}s)";
        ActionLog.Add(msg);
        return msg;
    }
}
