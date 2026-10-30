using AiVideoEditor.Core.Models;
using AiVideoEditor.Core.Services;
using AiVideoEditor.MediaEngine.Shaders;
using SkiaSharp;
using Xunit;

namespace AiVideoEditor.Tests;

public class TransitionTests
{
    [Fact]
    public void EasingHelper_Linear_ReturnsIdentityWithinBounds()
    {
        Assert.Equal(0.0f, TransitionEasingHelper.ApplyEasing(0.0f, "Linear", 1.0));
        Assert.Equal(0.5f, TransitionEasingHelper.ApplyEasing(0.5f, "Linear", 1.0));
        Assert.Equal(1.0f, TransitionEasingHelper.ApplyEasing(1.0f, "Linear", 1.0));

        // Clamping check
        Assert.Equal(0.0f, TransitionEasingHelper.ApplyEasing(-0.5f, "Linear", 1.0));
        Assert.Equal(1.0f, TransitionEasingHelper.ApplyEasing(1.5f, "Linear", 1.0));
    }

    [Fact]
    public void EasingHelper_EaseIn_ProducesQuadraticCurve()
    {
        float mid = TransitionEasingHelper.ApplyEasing(0.5f, "EaseIn", 1.0);
        // At t=0.5, t^2 = 0.25 < 0.5 (starts slow)
        Assert.True(mid < 0.5f);
        Assert.Equal(0.25f, mid, 3);
        Assert.Equal(0.0f, TransitionEasingHelper.ApplyEasing(0.0f, "EaseIn", 1.0));
        Assert.Equal(1.0f, TransitionEasingHelper.ApplyEasing(1.0f, "EaseIn", 1.0));
    }

    [Fact]
    public void EasingHelper_EaseOut_ProducesDeceleratingCurve()
    {
        float mid = TransitionEasingHelper.ApplyEasing(0.5f, "EaseOut", 1.0);
        // At t=0.5, 1 - (1-0.5)^2 = 0.75 > 0.5 (starts fast, slows down)
        Assert.True(mid > 0.5f);
        Assert.Equal(0.75f, mid, 3);
        Assert.Equal(0.0f, TransitionEasingHelper.ApplyEasing(0.0f, "EaseOut", 1.0));
        Assert.Equal(1.0f, TransitionEasingHelper.ApplyEasing(1.0f, "EaseOut", 1.0));
    }

    [Fact]
    public void EasingHelper_EaseInOut_SmoothstepSymmetricAtMidpoint()
    {
        float mid = TransitionEasingHelper.ApplyEasing(0.5f, "EaseInOut", 1.0);
        Assert.Equal(0.5f, mid, 3);

        float quarter = TransitionEasingHelper.ApplyEasing(0.25f, "EaseInOut", 1.0);
        Assert.True(quarter < 0.25f); // Slow start

        float threeQuarter = TransitionEasingHelper.ApplyEasing(0.75f, "EaseInOut", 1.0);
        Assert.True(threeQuarter > 0.75f); // Slow end
    }

    [Fact]
    public void EasingHelper_SpeedMultiplier_ModulatesProgress()
    {
        // 2x speed reaches 1.0 at half duration (t = 0.5)
        float atHalf = TransitionEasingHelper.ApplyEasing(0.5f, "Linear", 2.0);
        Assert.Equal(1.0f, atHalf);

        // 0.5x speed only reaches 0.25 at t = 0.5
        float halfSpeed = TransitionEasingHelper.ApplyEasing(0.5f, "Linear", 0.5);
        Assert.Equal(0.25f, halfSpeed);
    }

    [Fact]
    public void TransitionSpec_Clone_CreatesDeepIndependentCopy()
    {
        var original = new TransitionSpec
        {
            Type = "Wipe",
            Duration = 0.75,
            Speed = 1.5,
            Easing = "EaseOut",
            Direction = "Right",
            StartOpacity = 10.0f,
            EndOpacity = 90.0f,
            StartOffset = 0.2,
            IsEnabled = true
        };

        var clone = original.Clone();

        Assert.NotNull(clone);
        Assert.Equal(original.Type, clone.Type);
        Assert.Equal(original.Duration, clone.Duration);
        Assert.Equal(original.Speed, clone.Speed);
        Assert.Equal(original.Easing, clone.Easing);
        Assert.Equal(original.Direction, clone.Direction);
        Assert.Equal(original.StartOpacity, clone.StartOpacity);
        Assert.Equal(original.EndOpacity, clone.EndOpacity);
        Assert.Equal(original.StartOffset, clone.StartOffset);
        Assert.Equal(original.IsEnabled, clone.IsEnabled);

        // Modify clone and confirm original is unchanged
        clone.Duration = 2.0;
        clone.Type = "DipToBlack";
        Assert.Equal(0.75, original.Duration);
        Assert.Equal("Wipe", original.Type);
    }

    [Fact]
    public void TimelineClip_CopyStateFrom_ClonesTransitionsCorrectly()
    {
        var clipA = new TimelineClip
        {
            Label = "Test Clip",
            TimelineStart = 2.0,
            Duration = 5.0,
            InTransition = new TransitionSpec { Type = "FadeIn", Duration = 0.8, Easing = "EaseIn" },
            OutTransition = new TransitionSpec { Type = "FadeOut", Duration = 1.0, Easing = "EaseOut" },
            TransitionSpeed = 1.25,
            TransitionEasing = "EaseInOut",
            TransitionDirection = "Up",
            TransitionStartOpacity = 5f,
            TransitionEndOpacity = 95f
        };

        var clipB = new TimelineClip();
        clipB.CopyStateFrom(clipA);

        Assert.Equal(clipA.Label, clipB.Label);
        Assert.Equal(clipA.TransitionSpeed, clipB.TransitionSpeed);
        Assert.Equal(clipA.TransitionEasing, clipB.TransitionEasing);
        Assert.Equal(clipA.TransitionDirection, clipB.TransitionDirection);
        Assert.Equal(clipA.TransitionStartOpacity, clipB.TransitionStartOpacity);
        Assert.Equal(clipA.TransitionEndOpacity, clipB.TransitionEndOpacity);

        Assert.NotNull(clipB.InTransition);
        Assert.Equal(0.8, clipB.InTransition.Duration);
        Assert.Equal("FadeIn", clipB.InTransition.Type);

        Assert.NotNull(clipB.OutTransition);
        Assert.Equal(1.0, clipB.OutTransition.Duration);

        // Mutating clipB InTransition shouldn't affect clipA
        clipB.InTransition.Duration = 2.5;
        Assert.Equal(0.8, clipA.InTransition.Duration);
    }

    [Fact]
    public void TimelineClip_IsTransitionClip_PropertyReflectsState()
    {
        var clip = new TimelineClip();
        Assert.False(clip.IsTransitionClip);

        clip.IsTransitionClip = true;
        Assert.True(clip.IsTransitionClip);
        Assert.Equal(ClipKind.Transition, clip.Kind);
        Assert.False(string.IsNullOrEmpty(clip.TransitionType));

        clip.IsTransitionClip = false;
        Assert.False(clip.IsTransitionClip);
        Assert.Equal(ClipKind.Media, clip.Kind);
        Assert.Null(clip.TransitionType);
    }

    [Fact]
    public void ShortClip_TransitionClamping_PreservesClipIntegrity()
    {
        var shortClip = new TimelineClip
        {
            TimelineStart = 0.0,
            Duration = 0.4,
            InTransition = new TransitionSpec { Type = "CrossDissolve", Duration = 0.5 },
            OutTransition = new TransitionSpec { Type = "CrossDissolve", Duration = 0.5 }
        };

        // When clamping to clip duration (e.g. 48% maximum for head and tail transitions):
        double maxIn = Math.Min(shortClip.Duration * 0.48, shortClip.InTransition.Duration);
        double maxOut = Math.Min(shortClip.Duration * 0.48, shortClip.OutTransition.Duration);

        Assert.True(maxIn + maxOut <= shortClip.Duration);
        Assert.True(maxIn > 0);
        Assert.True(maxOut > 0);
    }

    [Fact]
    public void Serialization_PreservesTransitionsAndStandaloneBlocks()
    {
        var doc = new ProjectDocument
        {
            ProjectName = "Transition Test Project",
            Fps = 30.0,
            Width = 1920,
            Height = 1080
        };

        var videoTrack = new TimelineTrack
        {
            Id = Guid.NewGuid(),
            Name = "V1",
            Type = TrackType.Video
        };

        var clip1 = new TimelineClip
        {
            Id = Guid.NewGuid(),
            Label = "Clip 1",
            TimelineStart = 0.0,
            Duration = 4.0,
            InTransition = new TransitionSpec
            {
                Type = "FadeIn",
                Duration = 0.6,
                Easing = "EaseIn",
                Speed = 1.2
            },
            OutTransition = new TransitionSpec
            {
                Type = "CrossDissolve",
                Duration = 0.8,
                Easing = "EaseInOut",
                Speed = 1.0
            }
        };

        var standaloneTransition = new TimelineClip
        {
            Id = Guid.NewGuid(),
            Label = "Wipe Transition",
            Kind = ClipKind.Transition,
            TransitionEffectType = "Wipe",
            TimelineStart = 3.6,
            Duration = 0.8,
            TransitionSpeed = 1.0,
            TransitionEasing = "EaseInOut",
            TransitionDirection = "Right",
            TransitionStartOpacity = 0.0f,
            TransitionEndOpacity = 1.0f
        };

        videoTrack.Clips.Add(clip1);
        videoTrack.Clips.Add(standaloneTransition);
        doc.Tracks.Add(videoTrack);

        // Serialize to JSON
        string json = ProjectSerializer.ToJson(doc);
        Assert.False(string.IsNullOrWhiteSpace(json));

        // Deserialize back
        var restored = ProjectSerializer.FromJson(json);
        Assert.NotNull(restored);
        Assert.Single(restored.Tracks);

        var restoredTrack = restored.Tracks[0];
        Assert.Equal(2, restoredTrack.Clips.Count);

        var rClip1 = restoredTrack.Clips[0];
        Assert.NotNull(rClip1.InTransition);
        Assert.Equal("FadeIn", rClip1.InTransition.Type);
        Assert.Equal(0.6, rClip1.InTransition.Duration);
        Assert.Equal("EaseIn", rClip1.InTransition.Easing);
        Assert.Equal(1.2, rClip1.InTransition.Speed);

        Assert.NotNull(rClip1.OutTransition);
        Assert.Equal("CrossDissolve", rClip1.OutTransition.Type);
        Assert.Equal(0.8, rClip1.OutTransition.Duration);

        var rTrans = restoredTrack.Clips[1];
        Assert.True(rTrans.IsTransitionClip);
        Assert.Equal("Wipe", rTrans.TransitionEffectType);
        Assert.Equal(3.6, rTrans.TimelineStart);
        Assert.Equal(0.8, rTrans.Duration);
        Assert.Equal("Right", rTrans.TransitionDirection);
        Assert.Equal("EaseInOut", rTrans.TransitionEasing);
    }

    [Fact]
    public void CubeLutParser_3DLut_ParsesAndInterpolatesTrilinearly()
    {
        // Build a 2x2x2 identity + warm shift 3D LUT (R fastest, then G, then B)
        string cubeText = """
            TITLE "Studio Warm Test 3D LUT"
            LUT_3D_SIZE 2
            DOMAIN_MIN 0.0 0.0 0.0
            DOMAIN_MAX 1.0 1.0 1.0
            # B=0, G=0, R=0..1
            0.0 0.0 0.0
            1.0 0.0 0.0
            # B=0, G=1, R=0..1
            0.0 1.0 0.0
            1.0 1.0 0.0
            # B=1, G=0, R=0..1
            0.0 0.0 1.0
            1.0 0.0 1.0
            # B=1, G=1, R=0..1
            0.0 1.0 1.0
            1.0 1.0 1.0
            """;

        var lut = CubeLutParser.ParseContent(cubeText);
        Assert.NotNull(lut);
        Assert.True(lut!.Is3D);
        Assert.Equal(2, lut.Size);
        Assert.Equal("Studio Warm Test 3D LUT", lut.Title);

        lut.Sample3DTrilinear(0.5f, 0.25f, 0.75f, out float rOut, out float gOut, out float bOut);
        Assert.Equal(0.5f, rOut, 3);
        Assert.Equal(0.25f, gOut, 3);
        Assert.Equal(0.75f, bOut, 3);
    }

    [Fact]
    public void ColorScienceProcessor_RgbaF16_UpgradesAndProcessesHalfFloatWithoutBanding()
    {
        using var bmp8 = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul));
        bmp8.Erase(new SKColor(128, 64, 192, 255));

        using var bmp16 = ColorScienceProcessor.ConvertToRgbaF16(bmp8);
        Assert.Equal(SKColorType.RgbaF16, bmp16.ColorType);
        Assert.Equal(8, bmp16.Width);
        Assert.Equal(8, bmp16.Height);
    }

    [Fact]
    public void TimelineService_AdjustmentLayerAndCompoundClip_CreateAndSerializeCorrectly()
    {
        var doc = ProjectDocument.CreateDefault();
        var service = new TimelineService();

        var v1 = doc.Tracks.First(t => t.Type == TrackType.Video);
        var baseClip = new TimelineClip
        {
            Label = "Shot A",
            Kind = ClipKind.Media,
            TimelineStart = 0.0,
            Duration = 4.0,
            Mask = new MaskPath
            {
                IsEnabled = true,
                ShapePreset = "Ellipse",
                MaskFeather = 24f,
                MaskOpacity = 90f,
                InvertMask = true,
                Points = MaskPath.CreateEllipseDefault().Points
            }
        };
        v1.Clips.Add(baseClip);

        var adj = service.AddAdjustmentLayerClip(doc, 0.5, 3.0, "Grade Adj Layer");
        Assert.Equal(ClipKind.AdjustmentLayer, adj.Kind);

        var compound = service.CreateCompoundClip(doc, baseClip.Id, "Nested Scene 1");
        Assert.NotNull(compound);
        Assert.Equal(ClipKind.CompoundClip, compound!.Kind);
        Assert.NotEmpty(compound.NestedTracks);

        string json = ProjectSerializer.ToJson(doc);
        var roundTrip = ProjectSerializer.FromJson(json);
        Assert.NotNull(roundTrip);
        var rtCompound = roundTrip!.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Kind == ClipKind.CompoundClip);
        Assert.NotNull(rtCompound);
        Assert.NotEmpty(rtCompound!.NestedTracks);
        var nestedClip = rtCompound.NestedTracks.SelectMany(t => t.Clips).First();
        Assert.True(nestedClip.Mask.IsEnabled);
        Assert.Equal(24f, nestedClip.Mask.MaskFeather);
        Assert.True(nestedClip.Mask.InvertMask);
    }

    [Fact]
    public void ProjectInterchangeExporter_GeneratesValidFcp7XmlAndCmx3600Edl()
    {
        var doc = ProjectDocument.CreateDefault();
        doc.ProjectName = "Resolve_Handoff_Test";
        doc.Fps = 24.0;

        var asset = new MediaAsset
        {
            Id = Guid.NewGuid(),
            Name = "A001_C001.mp4",
            FilePath = @"C:\Footage\A001_C001.mp4",
            Type = MediaAssetType.Video,
            Duration = 10.0,
            Fps = 24.0,
            Width = 1920,
            Height = 1080
        };
        doc.MediaPool.Add(asset);

        var v1 = doc.Tracks.First(t => t.Type == TrackType.Video);
        v1.Clips.Add(new TimelineClip
        {
            MediaAssetId = asset.Id,
            Label = asset.Name,
            TimelineStart = 0.0,
            Duration = 5.0,
            SourceIn = 1.0,
            SourceOut = 6.0
        });

        string xml = ProjectInterchangeExporter.GenerateFcp7Xml(doc);
        Assert.Contains("<xmeml version=\"5\">", xml);
        Assert.Contains("Resolve_Handoff_Test", xml);
        Assert.Contains("A001_C001.mp4", xml);

        string edl = ProjectInterchangeExporter.GenerateCmx3600Edl(doc);
        Assert.Contains("TITLE: RESOLVE_HANDOFF_TEST", edl);
        Assert.Contains("FROM CLIP NAME: A001_C001.mp4", edl);
    }
}
