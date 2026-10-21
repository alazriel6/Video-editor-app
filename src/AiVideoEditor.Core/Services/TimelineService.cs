using AiVideoEditor.Core.Models;

namespace AiVideoEditor.Core.Services;

public class TimelineService
{
    public TimelineClip? AddAssetWithLinkedAudioToTimeline(ProjectDocument doc, MediaAsset asset, double? atTime = null)
    {
        if (asset.Type is MediaAssetType.Audio or MediaAssetType.GeneratedVoiceover or MediaAssetType.GeneratedSfx)
        {
            var audioTrack = asset.Type == MediaAssetType.Audio
                ? (doc.Tracks.FirstOrDefault(t => t.TrackCode == "A1") ?? doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Audio))
                : (doc.Tracks.FirstOrDefault(t => t.TrackCode == "A2") ?? doc.Tracks.LastOrDefault(t => t.Type == TrackType.Audio));

            audioTrack ??= doc.Tracks.First();
            double start = atTime ?? (audioTrack.Clips.Count > 0 ? audioTrack.Clips.Max(c => c.TimelineStart + c.Duration) : 0.0);
            double inPt = Math.Max(0, asset.MarkIn);
            double outPt = asset.MarkOut > inPt + 0.1 ? asset.MarkOut : asset.Duration;
            double dur = Math.Max(0.2, outPt - inPt);

            var aClip = new TimelineClip
            {
                MediaAssetId = asset.Id,
                Label = asset.Name,
                TimelineStart = Math.Round(start, 3),
                Duration = Math.Round(dur, 3),
                SourceIn = Math.Round(inPt, 3),
                SourceOut = Math.Round(outPt, 3),
                GainDb = 0.0f,
                Scale = 1.0f
            };
            audioTrack.Clips.Add(aClip);
            return aClip;
        }
        else
        {
            var v1Track = doc.Tracks.FirstOrDefault(t => t.TrackCode == "V1")
                          ?? doc.Tracks.LastOrDefault(t => t.Type == TrackType.Video)
                          ?? doc.Tracks.First();

            double startPos = atTime ?? (v1Track.Clips.Count > 0
                ? v1Track.Clips.Max(c => c.TimelineStart + c.Duration)
                : 0.0);

            double inPt = Math.Max(0, asset.MarkIn);
            double outPt = asset.MarkOut > inPt + 0.1 ? asset.MarkOut : asset.Duration;
            double dur = Math.Max(0.2, outPt - inPt);

            var vClip = new TimelineClip
            {
                MediaAssetId = asset.Id,
                Label = asset.Name,
                TimelineStart = Math.Round(startPos, 3),
                Duration = Math.Round(dur, 3),
                SourceIn = Math.Round(inPt, 3),
                SourceOut = Math.Round(outPt, 3),
                GainDb = -60.0f, // Mute audio on V1 when linked A1 audio clip is created
                Scale = 1.0f
            };

            if (asset.HasAudio && asset.Type == MediaAssetType.Video)
            {
                var a1Track = doc.Tracks.FirstOrDefault(t => t.TrackCode == "A1")
                              ?? doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Audio);
                if (a1Track != null)
                {
                    var linkedAudioClip = new TimelineClip
                    {
                        MediaAssetId = asset.Id,
                        LinkedClipId = vClip.Id,
                        Label = $"{asset.Name} [Audio]",
                        TimelineStart = Math.Round(startPos, 3),
                        Duration = Math.Round(dur, 3),
                        SourceIn = Math.Round(inPt, 3),
                        SourceOut = Math.Round(outPt, 3),
                        GainDb = 0.0f,
                        Scale = 1.0f
                    };
                    vClip.LinkedClipId = linkedAudioClip.Id;
                    a1Track.Clips.Add(linkedAudioClip);
                }
                else
                {
                    vClip.GainDb = 0.0f;
                }
            }

            v1Track.Clips.Add(vClip);
            return vClip;
        }
    }

    public bool CutClipAt(ProjectDocument doc, Guid clipId, double timelineTime)
    {
        foreach (var track in doc.Tracks)
        {
            if (track.IsLocked) continue;
            var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip != null)
            {
                Guid? linkedId = clip.LinkedClipId;
                bool ok = SplitClipInternal(track, clip, timelineTime, out var rightV);

                if (ok && doc.LinkVideoAndAudio && linkedId.HasValue)
                {
                    foreach (var aTrack in doc.Tracks.Where(t => !t.IsLocked))
                    {
                        var linkedClip = aTrack.Clips.FirstOrDefault(c => c.Id == linkedId.Value);
                        if (linkedClip != null)
                        {
                            if (SplitClipInternal(aTrack, linkedClip, timelineTime, out var rightA) && rightV != null && rightA != null)
                            {
                                rightV.LinkedClipId = rightA.Id;
                                rightA.LinkedClipId = rightV.Id;
                            }
                            break;
                        }
                    }
                }
                return ok;
            }
        }
        return false;
    }

    public int SplitAllClipsAtPlayhead(ProjectDocument doc, double playheadTime, Guid? selectedClipId = null)
    {
        int count = 0;
        if (selectedClipId.HasValue)
        {
            if (CutClipAt(doc, selectedClipId.Value, playheadTime))
                return 1;
        }

        foreach (var track in doc.Tracks)
        {
            if (track.IsLocked) continue;
            var clipsAtTime = track.Clips
                .Where(c => playheadTime > c.TimelineStart + 0.04
                            && playheadTime < c.TimelineStart + c.Duration - 0.04)
                .ToList();

            foreach (var clip in clipsAtTime)
            {
                if (SplitClipInternal(track, clip, playheadTime, out _))
                    count++;
            }
        }
        return count;
    }

    public int SplitClipsEveryInterval(ProjectDocument doc, double intervalSeconds = 5.0)
    {
        if (intervalSeconds < 0.5) intervalSeconds = 5.0;
        int splits = 0;

        foreach (var track in doc.Tracks.Where(t => !t.IsLocked && (t.Type == TrackType.Video || t.Type == TrackType.Audio)))
        {
            double maxEnd = track.Clips.Count > 0 ? track.Clips.Max(c => c.TimelineStart + c.Duration) : 0;
            for (double t = intervalSeconds; t < maxEnd - 0.2; t += intervalSeconds)
            {
                var clip = track.Clips.FirstOrDefault(c => t > c.TimelineStart + 0.1 && t < c.TimelineStart + c.Duration - 0.1);
                if (clip != null && SplitClipInternal(track, clip, t, out _))
                {
                    splits++;
                }
            }
        }
        return splits;
    }

    public TimelineClip? InsertFreezeFrameAtPlayhead(ProjectDocument doc, double playheadTime, double freezeDuration = 3.0)
    {
        var vTrack = doc.Tracks.FirstOrDefault(t => t.TrackCode == "V1" && !t.IsLocked)
                     ?? doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Video && !t.IsLocked);
        if (vTrack == null) return null;

        var activeClip = vTrack.Clips.FirstOrDefault(c => playheadTime >= c.TimelineStart && playheadTime <= c.TimelineStart + c.Duration);
        if (activeClip == null) return null;

        double freezeSrcTime = activeClip.SourceIn + (playheadTime - activeClip.TimelineStart) * activeClip.Speed;

        if (playheadTime > activeClip.TimelineStart + 0.05 && playheadTime < activeClip.TimelineStart + activeClip.Duration - 0.05)
        {
            SplitClipInternal(vTrack, activeClip, playheadTime, out _);
        }

        // Shift subsequent clips right by freezeDuration
        foreach (var tr in doc.Tracks.Where(t => !t.IsLocked))
        {
            foreach (var c in tr.Clips.Where(c => c.TimelineStart >= playheadTime - 0.02))
            {
                c.TimelineStart = Math.Round(c.TimelineStart + freezeDuration, 3);
            }
        }

        var freezeClip = activeClip.Clone();
        freezeClip.Label = $"{activeClip.Label} [Freeze]";
        freezeClip.TimelineStart = Math.Round(playheadTime, 3);
        freezeClip.Duration = Math.Round(freezeDuration, 3);
        freezeClip.SourceIn = Math.Round(freezeSrcTime, 3);
        freezeClip.SourceOut = Math.Round(freezeSrcTime, 3);
        freezeClip.IsFreezeFrame = true;
        freezeClip.FreezeSourceTimestamp = freezeSrcTime;
        freezeClip.LinkedClipId = null;

        vTrack.Clips.Add(freezeClip);
        vTrack.Clips = vTrack.Clips.OrderBy(c => c.TimelineStart).ToList();
        return freezeClip;
    }

    public TimelineClip? DuplicateClip(ProjectDocument doc, Guid clipId)
    {
        foreach (var track in doc.Tracks.Where(t => !t.IsLocked))
        {
            var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip != null)
            {
                var dup = clip.Clone();
                dup.TimelineStart = Math.Round(clip.TimelineStart + clip.Duration, 3);
                dup.LinkedClipId = null;
                track.Clips.Add(dup);
                track.Clips = track.Clips.OrderBy(c => c.TimelineStart).ToList();
                return dup;
            }
        }
        return null;
    }

    public TimelineClip? PasteClipAtTime(ProjectDocument doc, TimelineClip clipboardTemplate, double targetTime, int? preferredTrackIndex = null)
    {
        var copy = clipboardTemplate.Clone();
        copy.TimelineStart = Math.Max(0, Math.Round(targetTime, 3));
        copy.LinkedClipId = null;

        TimelineTrack? targetTrack = null;
        if (preferredTrackIndex.HasValue && preferredTrackIndex.Value >= 0 && preferredTrackIndex.Value < doc.Tracks.Count)
        {
            var cand = doc.Tracks[preferredTrackIndex.Value];
            if (!cand.IsLocked) targetTrack = cand;
        }

        if (targetTrack == null)
        {
            if (copy.TextData != null)
                targetTrack = doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Text && !t.IsLocked);
            else
            {
                var asset = doc.MediaPool.FirstOrDefault(m => m.Id == copy.MediaAssetId);
                targetTrack = (asset != null && !asset.HasVideo)
                    ? doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Audio && !t.IsLocked)
                    : doc.Tracks.FirstOrDefault(t => t.TrackCode == "V1" && !t.IsLocked) ?? doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Video && !t.IsLocked);
            }
        }

        targetTrack ??= doc.Tracks.FirstOrDefault(t => !t.IsLocked);
        if (targetTrack == null) return null;

        targetTrack.Clips.Add(copy);
        targetTrack.Clips = targetTrack.Clips.OrderBy(c => c.TimelineStart).ToList();
        return copy;
    }

    public bool CloseGapAtTime(ProjectDocument doc, double clickedTime)
    {
        int v1Idx = doc.Tracks.FindIndex(t => t.TrackCode == "V1" && !t.IsLocked);
        if (v1Idx >= 0 && CloseGapAtTime(doc, v1Idx, clickedTime))
            return true;

        for (int i = 0; i < doc.Tracks.Count; i++)
        {
            if (CloseGapAtTime(doc, i, clickedTime))
                return true;
        }
        return false;
    }

    public bool CloseGapAtTime(ProjectDocument doc, int trackIndex, double clickedTime)
    {
        if (trackIndex < 0 || trackIndex >= doc.Tracks.Count) return false;
        var track = doc.Tracks[trackIndex];
        if (track.IsLocked || track.Clips.Count == 0) return false;

        var ordered = track.Clips.OrderBy(c => c.TimelineStart).ToList();
        if (ordered.Any(c => clickedTime >= c.TimelineStart && clickedTime <= c.TimelineStart + c.Duration))
            return false;

        double gapStart = 0.0;
        foreach (var c in ordered)
        {
            double end = c.TimelineStart + c.Duration;
            if (end <= clickedTime + 0.01 && end > gapStart)
                gapStart = end;
        }

        var nextClip = ordered.FirstOrDefault(c => c.TimelineStart > clickedTime - 0.01);
        if (nextClip == null) return false;

        double gapDuration = nextClip.TimelineStart - gapStart;
        if (gapDuration <= 0.01) return false;

        foreach (var tr in doc.Tracks.Where(t => !t.IsLocked))
        {
            foreach (var c in tr.Clips.Where(c => c.TimelineStart >= nextClip.TimelineStart - 0.02))
            {
                c.TimelineStart = Math.Max(0, Math.Round(c.TimelineStart - gapDuration, 3));
            }
        }
        return true;
    }

    public TimelineClip? DetachAudioToTrack(ProjectDocument doc, Guid clipId)
    {
        foreach (var track in doc.Tracks.Where(t => t.Type == TrackType.Video))
        {
            var vClip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (vClip != null)
            {
                var aTrack = doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Audio && !t.IsLocked);
                if (aTrack == null) return null;

                // Check if linked audio clip already exists
                if (vClip.LinkedClipId.HasValue)
                {
                    vClip.LinkedClipId = null;
                    vClip.GainDb = -60f;
                    return aTrack.Clips.FirstOrDefault();
                }

                var aClip = vClip.Clone();
                aClip.Label = $"{vClip.Label} (Audio)";
                aClip.GainDb = 0.0f;
                aClip.LinkedClipId = null;
                vClip.GainDb = -60.0f;
                vClip.LinkedClipId = null;
                aTrack.Clips.Add(aClip);
                return aClip;
            }
        }
        return null;
    }

    public double GetPreviousCutPoint(ProjectDocument doc, double currentPlayhead)
    {
        var points = new List<double> { 0.0 };
        foreach (var t in doc.Tracks)
        {
            foreach (var c in t.Clips)
            {
                points.Add(c.TimelineStart);
                points.Add(c.TimelineStart + c.Duration);
            }
        }
        return points.Where(p => p < currentPlayhead - 0.04).DefaultIfEmpty(0.0).Max();
    }

    public double GetNextCutPoint(ProjectDocument doc, double currentPlayhead)
    {
        var points = new List<double>();
        foreach (var t in doc.Tracks)
        {
            foreach (var c in t.Clips)
            {
                points.Add(c.TimelineStart);
                points.Add(c.TimelineStart + c.Duration);
            }
        }
        return points.Where(p => p > currentPlayhead + 0.04).DefaultIfEmpty(doc.TotalDuration).Min();
    }

    public int ApplySmoothZoomKeyframes(ProjectDocument doc, Guid? targetClipId = null, float startScale = 1.0f, float endScale = 1.18f)
    {
        return ApplySmoothZoomKeyframes(doc, 0.0, 3.0, startScale, endScale);
    }

    public int ApplySmoothZoomKeyframes(ProjectDocument doc, double startSeconds, double endSeconds, double startScale = 1.0, double endScale = 1.25)
    {
        int count = 0;
        foreach (var track in doc.Tracks.Where(t => t.Type == TrackType.Video && !t.IsLocked))
        {
            foreach (var clip in track.Clips)
            {
                double kStart = Math.Clamp(startSeconds - clip.TimelineStart, 0.0, clip.Duration);
                double kEnd = Math.Clamp(endSeconds - clip.TimelineStart, Math.Min(0.2, clip.Duration), clip.Duration);
                if (kEnd <= kStart) kEnd = Math.Max(0.2, clip.Duration);

                clip.ScaleTrack.AddOrUpdateKeyframe(kStart, (float)startScale, EasingType.EaseInOutCubic);
                clip.ScaleTrack.AddOrUpdateKeyframe(kEnd, (float)endScale, EasingType.EaseInOutCubic);
                count++;
            }
        }
        return count;
    }

    private static bool SplitClipInternal(TimelineTrack track, TimelineClip clip, double splitTime, out TimelineClip? rightClip)
    {
        rightClip = null;
        double offset = splitTime - clip.TimelineStart;
        if (offset <= 0.04 || offset >= clip.Duration - 0.04)
            return false;

        double rightDuration = clip.Duration - offset;
        double splitSource = clip.SourceIn + (offset * clip.Speed);
        double originalSourceOut = clip.SourceOut;

        clip.Duration = Math.Round(offset, 3);
        clip.SourceOut = Math.Round(splitSource, 3);

        rightClip = clip.Clone();
        rightClip.TimelineStart = Math.Round(splitTime, 3);
        rightClip.Duration = Math.Round(rightDuration, 3);
        rightClip.SourceIn = Math.Round(splitSource, 3);
        rightClip.SourceOut = Math.Round(originalSourceOut, 3);

        int idx = track.Clips.IndexOf(clip);
        if (idx >= 0 && idx + 1 <= track.Clips.Count)
            track.Clips.Insert(idx + 1, rightClip);
        else
            track.Clips.Add(rightClip);

        return true;
    }

    public TimelineClip InsertOrOverwriteSubClip(
        ProjectDocument doc,
        MediaAsset asset,
        double markIn,
        double markOut,
        double playheadTime,
        bool rippleInsert)
    {
        double clipDur = Math.Max(0.2, markOut - markIn);
        TimelineTrack? targetTrack = asset.Type switch
        {
            MediaAssetType.Audio or MediaAssetType.GeneratedVoiceover or MediaAssetType.GeneratedSfx =>
                doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Audio && !t.IsLocked),
            _ => doc.Tracks.FirstOrDefault(t => t.TrackCode == "V1" && !t.IsLocked)
                 ?? doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Video && !t.IsLocked)
        };

        targetTrack ??= doc.Tracks.First();

        if (rippleInsert)
        {
            foreach (var existing in targetTrack.Clips.ToList())
            {
                if (playheadTime > existing.TimelineStart + 0.03 && playheadTime < existing.TimelineStart + existing.Duration - 0.03)
                {
                    SplitClipInternal(targetTrack, existing, playheadTime, out _);
                }
            }
            foreach (var existing in targetTrack.Clips.Where(c => c.TimelineStart >= playheadTime - 0.02))
            {
                existing.TimelineStart = Math.Round(existing.TimelineStart + clipDur, 3);
            }
        }
        else
        {
            double endOverwrite = playheadTime + clipDur;
            foreach (var existing in targetTrack.Clips.ToList())
            {
                if (playheadTime > existing.TimelineStart + 0.03 && playheadTime < existing.TimelineStart + existing.Duration - 0.03)
                    SplitClipInternal(targetTrack, existing, playheadTime, out _);
            }
            foreach (var existing in targetTrack.Clips.ToList())
            {
                if (endOverwrite > existing.TimelineStart + 0.03 && endOverwrite < existing.TimelineStart + existing.Duration - 0.03)
                    SplitClipInternal(targetTrack, existing, endOverwrite, out _);
            }
            targetTrack.Clips.RemoveAll(c => c.TimelineStart >= playheadTime - 0.02 && (c.TimelineStart + c.Duration) <= endOverwrite + 0.02);
        }

        return AddAssetWithLinkedAudioToTimeline(doc, asset, playheadTime) ?? new TimelineClip();
    }

    public bool RippleDeleteClip(ProjectDocument doc, Guid clipId)
    {
        foreach (var track in doc.Tracks)
        {
            if (track.IsLocked) continue;
            var clip = track.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip != null)
            {
                double start = clip.TimelineStart;
                double dur = clip.Duration;
                Guid? linkedId = clip.LinkedClipId;

                track.Clips.Remove(clip);
                foreach (var after in track.Clips.Where(c => c.TimelineStart >= start - 0.01))
                {
                    after.TimelineStart = Math.Max(0, Math.Round(after.TimelineStart - dur, 3));
                }

                if (doc.LinkVideoAndAudio && linkedId.HasValue)
                {
                    foreach (var tr in doc.Tracks.Where(t => !t.IsLocked))
                    {
                        var lClip = tr.Clips.FirstOrDefault(c => c.Id == linkedId.Value);
                        if (lClip != null)
                        {
                            tr.Clips.Remove(lClip);
                            foreach (var after in tr.Clips.Where(c => c.TimelineStart >= start - 0.01))
                            {
                                after.TimelineStart = Math.Max(0, Math.Round(after.TimelineStart - dur, 3));
                            }
                            break;
                        }
                    }
                }
                return true;
            }
        }
        return false;
    }

    public int DeleteTimeRangeAndRipple(ProjectDocument doc, double start, double end)
    {
        if (end <= start) return 0;
        double cutLength = end - start;
        int affected = 0;

        foreach (var track in doc.Tracks)
        {
            if (track.IsLocked) continue;

            foreach (var clip in track.Clips.ToList())
            {
                if (start > clip.TimelineStart + 0.02 && start < clip.TimelineStart + clip.Duration - 0.02)
                {
                    SplitClipInternal(track, clip, start, out _);
                }
            }
            foreach (var clip in track.Clips.ToList())
            {
                if (end > clip.TimelineStart + 0.02 && end < clip.TimelineStart + clip.Duration - 0.02)
                {
                    SplitClipInternal(track, clip, end, out _);
                }
            }

            var insideClips = track.Clips
                .Where(c => c.TimelineStart >= start - 0.02 && (c.TimelineStart + c.Duration) <= end + 0.02)
                .ToList();

            foreach (var c in insideClips)
            {
                track.Clips.Remove(c);
                affected++;
            }

            foreach (var clip in track.Clips.Where(c => c.TimelineStart >= end - 0.02))
            {
                clip.TimelineStart = Math.Max(0, Math.Round(clip.TimelineStart - cutLength, 3));
            }
        }

        return affected;
    }

    public int AutoRemoveSilences(ProjectDocument doc, double silenceThresholdSeconds = 0.6)
    {
        return StripSilenceWithParameters(doc, -36.0f, silenceThresholdSeconds * 1000.0, 80.0, 120.0);
    }

    public int StripSilenceWithParameters(
        ProjectDocument doc,
        float thresholdDb = -36.0f,
        double minSilenceDurationMs = 400.0,
        double padStartMs = 80.0,
        double padEndMs = 120.0)
    {
        var silenceRanges = new List<(double Start, double End)>();
        double minSilenceSec = Math.Max(0.15, minSilenceDurationMs / 1000.0);
        double padStartSec = Math.Max(0.0, padStartMs / 1000.0);
        double padEndSec = Math.Max(0.0, padEndMs / 1000.0);
        float linearThreshold = MathF.Pow(10f, thresholdDb / 20f);

        var primaryTrack = doc.Tracks.FirstOrDefault(t => t.TrackCode == "V1" && t.Clips.Count > 0)
                           ?? doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Video && t.Clips.Count > 0)
                           ?? doc.Tracks.FirstOrDefault(t => t.Clips.Count > 0);
        if (primaryTrack == null) return 0;

        foreach (var clip in primaryTrack.Clips.OrderBy(c => c.TimelineStart).ToList())
        {
            var asset = doc.MediaPool.FirstOrDefault(m => m.Id == clip.MediaAssetId);
            if (asset != null && asset.Transcript.Count > 0)
            {
                var orderedSegs = asset.Transcript.Where(s => !s.IsDeleted).OrderBy(s => s.StartTime).ToList();
                double prevEnd = clip.SourceIn;

                foreach (var seg in orderedSegs)
                {
                    if (seg.EndTime <= clip.SourceIn || seg.StartTime >= clip.SourceOut)
                        continue;

                    double segStart = Math.Max(seg.StartTime, clip.SourceIn);
                    if (segStart - prevEnd >= minSilenceSec)
                    {
                        double rawStart = clip.TimelineStart + (prevEnd - clip.SourceIn) / clip.Speed + padStartSec;
                        double rawEnd = clip.TimelineStart + (segStart - clip.SourceIn) / clip.Speed - padEndSec;
                        if (rawEnd - rawStart >= 0.08)
                        {
                            silenceRanges.Add((rawStart, rawEnd));
                        }
                    }
                    prevEnd = Math.Max(prevEnd, Math.Min(seg.EndTime, clip.SourceOut));
                }
            }
            else if (asset != null && asset.WaveformPeaks.Length > 10)
            {
                int totalBins = asset.WaveformPeaks.Length;
                double binDuration = Math.Max(0.02, asset.Duration / totalBins);
                int silenceStartBin = -1;

                for (int i = 0; i < totalBins; i++)
                {
                    double sourceTime = i * binDuration;
                    if (sourceTime < clip.SourceIn || sourceTime > clip.SourceOut) continue;

                    bool isSilent = asset.WaveformPeaks[i] <= Math.Max(0.03f, linearThreshold);
                    if (isSilent && silenceStartBin < 0)
                    {
                        silenceStartBin = i;
                    }
                    else if (!isSilent && silenceStartBin >= 0)
                    {
                        double silentDur = (i - silenceStartBin) * binDuration;
                        if (silentDur >= minSilenceSec)
                        {
                            double sStart = clip.TimelineStart + ((silenceStartBin * binDuration) - clip.SourceIn) / clip.Speed + padStartSec;
                            double sEnd = clip.TimelineStart + ((i * binDuration) - clip.SourceIn) / clip.Speed - padEndSec;
                            if (sEnd - sStart >= 0.08)
                            {
                                silenceRanges.Add((sStart, sEnd));
                            }
                        }
                        silenceStartBin = -1;
                    }
                }
            }
        }

        int removedCount = 0;
        foreach (var range in silenceRanges.OrderByDescending(r => r.Start))
        {
            if (DeleteTimeRangeAndRipple(doc, range.Start, range.End) > 0)
                removedCount++;
        }

        return removedCount;
    }

    public int ApplyTransitionToAllClips(ProjectDocument doc, string transitionType, double duration = 0.5)
    {
        int count = 0;
        foreach (var track in doc.Tracks.Where(t => t.Type == TrackType.Video && !t.IsLocked))
        {
            foreach (var clip in track.Clips)
            {
                clip.InTransition = new TransitionSpec { Type = transitionType, Duration = duration };
                clip.OutTransition = new TransitionSpec { Type = transitionType, Duration = duration };
                count++;
            }
        }
        return count;
    }

    public int NormalizeTrackAudioGain(ProjectDocument doc, string trackCode = "A1", float targetGainDb = 0.0f)
    {
        int count = 0;
        foreach (var track in doc.Tracks.Where(t => !t.IsLocked && (t.TrackCode.Equals(trackCode, StringComparison.OrdinalIgnoreCase) || t.Type == TrackType.Audio || t.Type == TrackType.Video)))
        {
            foreach (var clip in track.Clips)
            {
                clip.GainDb = Math.Clamp(targetGainDb, -24f, 12f);
                count++;
            }
        }
        return count;
    }

    public TimelineClip AddTextOverlay(ProjectDocument doc, string text, double start, double duration, string stylePreset = "BroadcastBox")
    {
        var textTrack = doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Text);
        if (textTrack == null)
        {
            textTrack = new TimelineTrack { TrackCode = "C1", Name = "Subtitles", Type = TrackType.Text };
            doc.Tracks.Insert(0, textTrack);
        }

        string textColor = stylePreset switch
        {
            "YellowCinema" => "#FFF59E0B",
            "CleanLowerThird" => "#FFE6E6EB",
            "HighContrastStroke" => "#FFFFFFFF",
            _ => "#FFE6E6EB"
        };

        var clip = new TimelineClip
        {
            Label = text.Length > 28 ? text[..28] + "..." : text,
            TimelineStart = Math.Max(0, Math.Round(start, 3)),
            Duration = Math.Max(0.3, Math.Round(duration, 3)),
            SourceIn = 0,
            SourceOut = Math.Max(0.3, Math.Round(duration, 3)),
            TextData = new TextOverlaySpec
            {
                Text = text,
                StylePreset = stylePreset,
                TextColorHex = textColor,
                FontSize = 46f,
                Bold = true
            }
        };

        textTrack.Clips.Add(clip);
        return clip;
    }

    public void PopulateSubtitleTrackFromTranscript(ProjectDocument doc, List<TranscriptSegment> segments, double timelineOffset = 0, string stylePreset = "BroadcastBox")
    {
        var textTrack = doc.Tracks.FirstOrDefault(t => t.Type == TrackType.Text);
        if (textTrack == null)
        {
            textTrack = new TimelineTrack { TrackCode = "C1", Name = "Subtitles", Type = TrackType.Text };
            doc.Tracks.Insert(0, textTrack);
        }

        textTrack.Clips.Clear();
        foreach (var seg in segments.Where(s => !s.IsDeleted && !string.IsNullOrWhiteSpace(s.Text)))
        {
            double start = timelineOffset + seg.StartTime;
            double dur = Math.Max(0.25, seg.EndTime - seg.StartTime);
            AddTextOverlay(doc, seg.Text.Trim(), start, dur, stylePreset);
        }
    }
}
