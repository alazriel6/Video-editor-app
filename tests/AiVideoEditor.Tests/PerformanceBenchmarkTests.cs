using System.Diagnostics;
using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using AiVideoEditor.MediaEngine.Decoding;
using AiVideoEditor.MediaEngine.Rendering;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace AiVideoEditor.Tests;

public class PerformanceBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Benchmark_ProjectSerialization_Scalability()
    {
        int[] clipCounts = { 10, 100, 500, 1000 };

        foreach (int count in clipCounts)
        {
            var doc = CreateTestProject(count);

            var sw = Stopwatch.StartNew();
            string json = ProjectSerializer.ToJson(doc);
            sw.Stop();
            long saveMs = sw.ElapsedMilliseconds;

            sw.Restart();
            var restored = ProjectSerializer.FromJson(json);
            sw.Stop();
            long loadMs = sw.ElapsedMilliseconds;

            Assert.NotNull(restored);
            _output.WriteLine($"[Project Scale {count} Clips] Save: {saveMs}ms | Load: {loadMs}ms | Json Size: {json.Length / 1024} KB");
        }
    }

    [Fact]
    public void Benchmark_KeyframeRelativeTimes_Overhead()
    {
        var clip = new TimelineClip
        {
            TimelineStart = 0,
            Duration = 10.0
        };

        // Clip without keyframes: measure cost of 10,000 evaluations
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            var times = clip.GetAllKeyframeRelativeTimes();
            Assert.Empty(times);
        }
        sw.Stop();
        _output.WriteLine($"[Keyframe Eval - 10,000 calls without keyframes]: {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalMicroseconds / 10000:0.00} µs/call)");

        // Clip with 5 keyframes
        clip.ScaleTrack.IsKeyframingEnabled = true;
        clip.ScaleTrack.Keyframes.Add(new KeyframePoint { ClipRelativeTime = 0.0, Value = 1.0f });
        clip.ScaleTrack.Keyframes.Add(new KeyframePoint { ClipRelativeTime = 2.5, Value = 1.2f });
        clip.ScaleTrack.Keyframes.Add(new KeyframePoint { ClipRelativeTime = 5.0, Value = 1.5f });
        clip.OpacityTrack.IsKeyframingEnabled = true;
        clip.OpacityTrack.Keyframes.Add(new KeyframePoint { ClipRelativeTime = 0.0, Value = 0.0f });
        clip.OpacityTrack.Keyframes.Add(new KeyframePoint { ClipRelativeTime = 1.0, Value = 1.0f });

        sw.Restart();
        for (int i = 0; i < 10000; i++)
        {
            var times = clip.GetAllKeyframeRelativeTimes();
            Assert.Equal(4, times.Count);
        }
        sw.Stop();
        _output.WriteLine($"[Keyframe Eval - 10,000 calls with keyframes]: {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalMicroseconds / 10000:0.00} µs/call)");
    }

    [Fact]
    public void Benchmark_Compositor_FrameRenderThroughput()
    {
        var doc = CreateTestProject(50);
        using var reader = new VideoFrameReader();
        var compositor = new CompositorEngine(reader);

        using var surface = SKSurface.Create(new SKImageInfo(960, 540, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // Warmup
        compositor.RenderCompositeFrame(canvas, doc, 1.0, 960, 540);

        int frameCount = 100;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < frameCount; i++)
        {
            double t = (i * 0.033) % 20.0;
            compositor.RenderCompositeFrame(canvas, doc, t, 960, 540);
        }
        sw.Stop();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double msPerFrame = totalMs / frameCount;
        double fps = 1000.0 / Math.Max(0.001, msPerFrame);

        _output.WriteLine($"[Compositor Throughput - 50 Clips Timeline, 100 Frames]: {totalMs:0.1}ms total | {msPerFrame:0.2} ms/frame | {fps:0.0} effective FPS");
    }

    [Fact]
    public void Benchmark_TimelineHitTesting_1000Clips()
    {
        var doc = CreateTestProject(1000);
        var track = doc.Tracks[0]; // has ~333 clips

        var sw = Stopwatch.StartNew();
        int hits = 0;
        for (int i = 0; i < 5000; i++)
        {
            double clickTime = (i * 0.25) % (track.Clips.Count * 1.5);
            var hit = track.Clips.LastOrDefault(c => clickTime >= c.TimelineStart && clickTime <= c.TimelineStart + c.Duration);
            if (hit != null) hits++;
        }
        sw.Stop();
        _output.WriteLine($"[Timeline Hit-Testing 5,000 pointer queries - 333 clips/track]: {sw.ElapsedMilliseconds}ms ({sw.Elapsed.TotalMicroseconds / 5000:0.00} µs/query) | Hits: {hits}");
    }

    [Fact]
    public void Benchmark_AssetLookup_O1_Vs_LinqLinear()
    {
        var doc = new ProjectDocument();
        var targetIds = new List<Guid>();

        for (int i = 0; i < 200; i++)
        {
            var asset = new MediaAsset
            {
                Id = Guid.NewGuid(),
                Name = $"Asset_{i:000}.mp4",
                Duration = 60.0
            };
            doc.MediaPool.Add(asset);
            if (i % 10 == 0) targetIds.Add(asset.Id);
        }

        int iterations = 20000;

        // Baseline: LINQ FirstOrDefault
        var sw = Stopwatch.StartNew();
        int linqFound = 0;
        for (int i = 0; i < iterations; i++)
        {
            var id = targetIds[i % targetIds.Count];
            var a = doc.MediaPool.FirstOrDefault(m => m.Id == id);
            if (a != null) linqFound++;
        }
        sw.Stop();
        long linqMs = sw.ElapsedMilliseconds;

        // Optimized: document.FindAsset (O(1) versioned dictionary)
        sw.Restart();
        int o1Found = 0;
        for (int i = 0; i < iterations; i++)
        {
            var id = targetIds[i % targetIds.Count];
            var a = doc.FindAsset(id);
            if (a != null) o1Found++;
        }
        sw.Stop();
        long o1Ms = sw.ElapsedMilliseconds;

        Assert.Equal(linqFound, o1Found);
        _output.WriteLine($"[Asset Lookup - 20,000 queries in 200-asset pool]: LINQ FirstOrDefault = {linqMs}ms vs O(1) FindAsset = {o1Ms}ms (Speedup: {(double)linqMs / Math.Max(1, o1Ms):0.0}x)");
    }

    private static ProjectDocument CreateTestProject(int clipCount)
    {
        var doc = new ProjectDocument
        {
            ProjectName = $"Benchmark Project ({clipCount} Clips)",
            Fps = 30.0,
            Width = 1920,
            Height = 1080
        };

        var track1 = new TimelineTrack { Name = "V1", Type = TrackType.Video };
        var track2 = new TimelineTrack { Name = "V2", Type = TrackType.Video };
        var trackText = new TimelineTrack { Name = "C1 Subtitles", Type = TrackType.Text };
        var trackAudio = new TimelineTrack { Name = "A1", Type = TrackType.Audio };

        double curTime = 0.0;
        for (int i = 0; i < clipCount; i++)
        {
            var targetTrack = (i % 3) switch
            {
                0 => track1,
                1 => track2,
                _ => trackText
            };

            var clip = new TimelineClip
            {
                Id = Guid.NewGuid(),
                Label = $"Clip_{i:000}",
                TimelineStart = curTime,
                Duration = 3.0,
                Kind = targetTrack.Type == TrackType.Text ? ClipKind.Text : ClipKind.Media,
                TextData = targetTrack.Type == TrackType.Text ? new TextOverlaySpec { Text = $"Subtitle {i}" } : null
            };

            targetTrack.Clips.Add(clip);
            curTime += 1.5; // Overlapping clips
        }

        doc.Tracks.Add(track1);
        doc.Tracks.Add(track2);
        doc.Tracks.Add(trackText);
        doc.Tracks.Add(trackAudio);

        return doc;
    }
}
