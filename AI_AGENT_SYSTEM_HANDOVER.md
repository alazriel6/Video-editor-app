# VORTEX CUT STUDIO (`AiVideoEditor`) — SYSTEM ARCHITECTURE & CODEBASE HANDOVER SPECIFICATION
> **Target Audience:** Incoming AI Coding Agents & Senior .NET Engineers  
> **Repository:** `AiVideoEditor`  
> **Current Date / Release State:** Late October 2026 (v5.0 Studio Milestone)  
> **Target Framework:** C# 12 / .NET 8 (`net8.0`)  
> **UI Framework:** Avalonia UI 11 (Desktop / Windows / macOS / Linux)  
> **Primary Graphics & Compositing:** SkiaSharp 2.88 + Custom SkSL Shaders  
> **Media & Export Pipeline:** FFmpeg 6.x/7.x + FFMediaToolkit + OpenCVSharp + NAudio 2.2  
> **Intelligent Assistant & Processing:** Whisper.net, ONNX Runtime (RVM), LLamaSharp / Semantic Kernel, Universal Cloud AI (Gemini, OpenAI, Claude, Groq, Ollama, LM Studio)

---

## 1. Project Overview & Design Philosophy

`AiVideoEditor` (branded in the UI as **Vortex Cut Studio**) is a desktop Non-Linear Video Editor (NLE) inspired by Adobe Premiere Pro, DaVinci Resolve, and CapCut. It is architected for:
1. **Studio-Grade Real-Time Performance:** Low-latency canvas playback via SkiaSharp and hardware-accelerated decode caching.
2. **Premiere Pro-Style Workflow:** Drag-and-drop standalone transition blocks, razor cutting, slip trimming, multi-track audio/video timeline, keyframe animation mini-timeline, Bezier RGB curves, 3-way color wheels, and real-time scopes.
3. **Deep AI Automation Without Geek Jargon:** Speech-to-text subtitle generation, silence stripping, automatic Foley sound synthesis, subject isolation/background removal (RVM), and natural language timeline operations via Semantic Kernel — all presented in clean, non-intimidating terminology.
4. **Resilient Undo/Redo Engine:** Unified command pattern that coalesces continuous slider and color wheel drags, records keyframe actions, and handles window-level `Ctrl+Z` / `Ctrl+Y` routing.
5. **High-Efficiency Hardware Export:** Zero-copy / low-allocation memory piping directly into hardware-accelerated FFmpeg child processes (NVENC, QSV, AMF, CPU).

---

## 2. Solution & Project Hierarchy

The Visual Studio Solution (`AiVideoEditor.sln`) is organized into clean layered projects:

```
AiVideoEditor/
├── src/
│   ├── AiVideoEditor.Core/          # Domain models, services, commands, interfaces
│   ├── AiVideoEditor.MediaEngine/   # SkiaSharp rendering, audio DSP, video decode, FFmpeg export
│   ├── AiVideoEditor.AiEngine/      # Whisper, ONNX RVM, LLM agents, cloud & local AI routers
│   └── AiVideoEditor.App/           # Avalonia UI 11 application, MVVM ViewModels, Custom Controls
└── tests/
    └── AiVideoEditor.Tests/         # XUnit unit tests and performance benchmarks
```

### Dependency Flow:
```
[AiVideoEditor.App]
      │
      ├──► [AiVideoEditor.MediaEngine] ──► [AiVideoEditor.Core]
      │
      └──► [AiVideoEditor.AiEngine] ─────► [AiVideoEditor.MediaEngine]
                                    └────► [AiVideoEditor.Core]
```

---

## 3. Detailed Component & Subsystem Architecture

### 3.1 `AiVideoEditor.Core` (Domain & Business Logic)

Contains pure domain contracts, data entities, serializable project state, math solvers, and the command pipeline. Has zero UI dependencies.

#### Key Models (`AiVideoEditor.Core.Models`):
- **`TimelineClip.cs`**: Core unit of the timeline.
  - `Kind`: `ClipKind.Media`, `ClipKind.Text`, `ClipKind.Transition`.
  - Timing: `TimelineStart`, `Duration`, `SourceIn`, `SourceOut`, `Speed`, `ReversePlayback`, `IsFreezeFrame`, `FreezeSourceTimestamp`.
  - Transform & Cropping (Unified for Video, Image, and Text): `PositionX`, `PositionY`, `Scale`, `ScaleX`, `ScaleY`, `Rotation`, `Opacity`, `CropLeft` (0-100%), `CropRight`, `CropTop`, `CropBottom`, `CornerRadius`, `BlendMode` ("Normal", "Screen", "Multiply", "Overlay", "Add"), `FlipHorizontal`, `FlipVertical`.
  - Visual Effects Stack: `CameraShakeAmount`, `GaussianBlurSigma`, `SharpenAmount`, `ChromaticAberrationAmount`, `FilmGrainAmount`.
  - Audio DSP Settings: `GainDb`, `Volume`, `Pan`, `DialogueIsolateEnabled`, `EqPreset`, `CompressorEnabled`, `ReverbPreset`, `ReverbDryWet`, `PitchSemitones`.
  - Lumetri Color Suite: `Exposure` (-4 to +4 EV), `Brightness`, `Contrast` (around mid-gray `0.5` pivot), `Highlights`, `Shadows`, `Whites`, `Blacks`, `Saturation`, `Vibrance`, `Temperature`, `Tint`, `Vignette`.
  - 3-Way Color Wheels: `LiftWheelRed/Green/Blue/Lum`, `GammaWheelRed/Green/Blue/Lum`, `GainWheelRed/Green/Blue/Lum`.
  - Bezier RGB Curves: `MasterCurvePoints`, `RedCurvePoints`, `GreenCurvePoints`, `BlueCurvePoints` (list of `CurveControlPoint(X, Y)`).
  - HSL Secondary Mixer: `HslHueShift[8]`, `HslSatShift[8]`, `HslLumShift[8]` for 8 hue sectors (Red, Orange, Yellow, Green, Aqua, Blue, Purple, Magenta).
  - Standalone Transition Block Properties: `TransitionEffectType` ("Cross Dissolve", "Dip To Black", "Dip To White", "Push Left", "Wipe", "Zoom Blur", "Glitch", etc.), `IsAudioTransition`, `TransitionSpeed`, `TransitionEasing`, `TransitionDirection`, `TransitionStartOpacity`, `TransitionEndOpacity`.
  - Keyframing Tracks: `ScaleTrack`, `PosXTrack`, `PosYTrack`, `RotationTrack`, `OpacityTrack`, `VolumeTrack`, `CropLeftTrack`, `CropRightTrack`, `CropTopTrack`, `CropBottomTrack`.
  - Text Overlay Data: `TextOverlaySpec` (Font, size, weight, spacing, transparent background support, outline stroke, drop shadow).
- **`ProjectDocument.cs`**:
  - Contains project metadata, `Settings` (resolution, fps, sample rate), `MediaPool` (imported assets with O(1) cached lookup via `FindAsset(Guid)`), `Tracks` (collection of `TimelineTrack`), `Markers`, In/Out points.
- **`TimelineTrack.cs`**:
  - `TrackCode` (`V1`, `V2`, `A1`, `A2`, `C1`), `Type` (`Video`, `Audio`, `Subtitle`), `IsMuted`, `IsSolo`, `IsLocked`, `Height`, `Clips`.
- **`KeyframeTrack.cs`**:
  - `AnimatableProperty`, `KeyframePoint` (ClipRelativeTime, Value, Interpolation: Linear, EaseIn, EaseOut, EaseInOutCubic, Hold).
- **`TranscriptSegment.cs`**:
  - Transcribed speech words/sentences with `Start`, `End`, `Text`, `Confidence`.
- **`TransitionModel.cs`**:
  - Transition specifications and easing calculator (`TransitionEasingHelper`).

#### Services & Math (`AiVideoEditor.Core.Services`):
- **`TimelineService.cs`**:
  - High-level timeline manipulation: `AddAssetWithLinkedAudioToTimeline`, `AddStandaloneTransitionBlock`, `CutClipAt`, `SplitAllClipsAtPlayhead`, `SplitClipsEveryInterval`, `InsertFreezeFrameAtPlayhead`, `DuplicateClip`, `PasteClipAtTime`, `CloseGapAtTime`, `RippleDeleteClip`, `DeleteTimeRangeAndRipple`, `AutoRemoveSilences`, `ApplyTransitionToAllClips`, `NormalizeTrackAudioGain`, `AddTextOverlay`, `PopulateSubtitleTrackFromTranscript`.
- **`UndoRedoManager.cs`**:
  - Stack-based undo/redo engine (100-step depth).
  - Supports continuous gesture coalescing (`RecordOrCoalesceClipEdit`) so dragging a slider or color wheel doesn't flood 60 undo steps per second.
  - Supports `ClipStateEditCommand`, `SplitClipCommand`, `DeleteClipCommand`, `MoveClipCommand`, `AddClipCommand`, `RippleDeleteCommand`, and `KeyframeMutationCommand`.
- **`MonotonicCubicCurveSolver.cs`**:
  - Fritsch-Carlson monotonic cubic spline interpolation for custom curve LUT generation, preventing overshoots in RGB grading curves.
- **`TimecodeCalculator.cs`**:
  - SMPTE `HH:MM:SS:FF` and clean UI timecode `MM:SS.FF` calculations based on project framerate.
- **`ProjectSerializer.cs`**:
  - JSON serialization/deserialization with schema version validation and backward compatibility.

---

### 3.2 `AiVideoEditor.MediaEngine` (Audio, Video, Rendering, Export)

Responsible for heavy multimedia processing, decoding, Skia compositing, SkSL shaders, and FFmpeg interoperability.

#### 1. Video Decoding & Caching (`AiVideoEditor.MediaEngine.Decoding`):
- **`VideoFrameReader.cs`**:
  - Decodes frames from source media using OpenCV / FFMediaToolkit.
  - Dual LRU frame cache with thread-safe locking and background asynchronous prefetching for forward scrubbing.
  - Media asset probing (`ProbeMediaDetailed`) to extract resolution, duration, framerate, video codec, color space, audio channels, and sample rate.
- **`ThumbnailStripService.cs`**:
  - Generates small thumbnail strips along timeline clips for visual navigation, caching thumbnails at fixed time increments.
- **`FfmpegBootstrapper.cs`**:
  - Automatically locates, verifies, or extracts FFmpeg/FFprobe binaries on the host system.

#### 2. Multi-Track Audio Engine (`AiVideoEditor.MediaEngine.Audio`):
- **`MultiTrackAudioPlayer.cs`**:
  - Low-latency real-time multi-track playback using NAudio (`WaveOutEvent` / `WasapiOut`).
  - Sample-accurate mixing across all active timeline audio tracks.
  - Real-time stereo VU meter calculation (`CurrentLeftDb`, `CurrentRightDb`).
  - Per-clip DSP pipeline (`ClipAudioDspSampleProvider`): Gain, stereo pan, 3-band EQ, dynamic range compressor, reverb wet/dry mix, speed pitch preservation.
  - Variable speed playback (`VariableSpeedSampleProvider`).
  - `MixdownToWavAsync`: Renders an exact sample-accurate 48 kHz 32-bit float master stereo WAV for export or playback cache, with constant-power audio crossfading.
- **`WaveformGenerator.cs`**:
  - Analyzes audio streams, computes peak envelopes, and generates `.pkf` cache files for fast rendering of audio waveforms on timeline clips.
- **`AudioExtractor.cs`**:
  - Demuxes audio streams from video files using FFmpeg into high-fidelity PCM WAV files.

#### 3. Compositing & GPU Shaders (`AiVideoEditor.MediaEngine.Rendering`):
- **`CompositorEngine.cs`**:
  - Master visual compositing engine. Renders the final 1080p/4K frame at any timeline timestamp `currentTime`.
  - Evaluates track visibility, track ordering (bottom-to-top: V1, V2, etc.), clip in/out boundaries, and opacity.
  - Handles standalone transition blocks: detects adjacent/overlapping clips, renders offscreen surfaces, and passes them to `ShaderTransitionRenderer`.
  - Evaluates keyframes for Transform (Position X/Y, Scale X/Y, Rotation) and applies canvas matrix transformations.
  - Applies 4-edge cropping (`ClipRect` with `CropLeft/Right/Top/Bottom`) to both video and text layers.
  - Direct integration with `ColorScienceProcessor` for real-time color grading filters.
  - Safe margins overlay (90% action safe, 80% title safe guides).
  - Scope data extraction during playback for live RGB Parade and Vectorscope updating.
- **`TextRenderer.cs`**:
  - Custom Skia typography renderer.
  - Calculates glyph positioning manually to enable **Letter Spacing / Tracking** (`-20px` to `+100px`) and **Line Height** (`0.5x` to `3.0x`).
  - Renders multi-line text with Left/Center/Right alignment.
  - Supports stroke outline, drop shadow, and optional background boxes. Default background is 100% transparent.
- **`ShaderTransitionRenderer.cs`**:
  - High-performance SkSL runtime shaders and canvas blended transitions.
  - Supported transitions:
    - `CrossDissolve` / `FadeIn` / `FadeOut`: High-speed alpha fast-path.
    - `DipToBlack` / `DipToWhite` / `FlashCut`: Two-stage eased luminance curve.
    - `Slide` / `Push` (Left, Right, Up, Down): Motion blur velocity + seam drop shadow.
    - `Wipe` / `DirectionalWipe`: Soft feathered linear gradient border.
    - `WhipZoom`: Smooth radial zoom with mirrored border tiling.
    - `Glitch`: Custom SkSL RGB chromatic slice displacement shader.
    - `LumaFade`: Custom SkSL luminance threshold dissolve shader.
- **`ColorScienceProcessor.cs`**:
  - Studio color science engine based on standard Rec.709 color space.
  - Generates 256-entry `SKColorTable` LUTs from cubic Bezier spline control points for Master, Red, Green, and Blue curves.
  - Constructs combined `SKColorFilter` pipeline: Exposure EV multiplication -> Mid-gray 0.5 contrast slope -> Color balance / Lift-Gamma-Gain -> Temperature/Tint matrix -> Vibrance/Saturation.
  - Multi-threaded SIMD/unsafe HSL secondary manipulation in BGRA pixel space.
  - Real-time scope calculations: `SampleScopeData` computes 64-bin RGB Histograms, 48x48 RGB Parade waveforms, 48x48 Luma IRE waveforms, and 64x64 YUV Vectorscope chrominance plots.

#### 4. Video Export Engine (`AiVideoEditor.MediaEngine.Exporting`):
- **`FFmpegExportEngine.cs`**:
  - High-throughput video export engine implementing `IVideoRenderEngine`.
  - Architecture:
    1. **Stage 1 (Audio Pre-render):** Mixes down all timeline audio tracks to an intermediate 48 kHz master WAV file with sample-accurate volume and crossfades.
    2. **Stage 2 (Video Pipe Streaming):** Spawns a background FFmpeg child process with hardware encoder auto-detection:
       - Nvidia NVENC: `h264_nvenc` / `hevc_nvenc`
       - Intel QuickSync: `h264_qsv`
       - AMD AMF: `h264_amf`
       - Fallback: Multi-threaded CPU `libx264` (`-preset veryfast -crf 18`)
    3. Feeds raw BGRA / YUV420p video frames via StandardInput OS pipe with minimal buffer allocations.
    4. Muxes the audio WAV file in a single container pass without desynchronization.
    5. Supports In/Out range export, custom resolutions, custom frame rates, and Audio-Only export (.wav / .mp3).
    6. Emits real-time progress callbacks with percentage, FPS, current timecode, and estimated completion time (ETA).

---

### 3.3 `AiVideoEditor.AiEngine` (Intelligent Hybrid Engine)

Orchestrates local offline models and cloud AI APIs through a unified routing interface (`IAiTranscriptionService`, `IAiAgentService`, `IAiSfxService`, `IVideoSegmentationService`).

#### 1. Core Architecture:
- **`HybridAiRouter.cs`**:
  - Dynamically routes requests to Local or Cloud providers based on `AiProviderConfig.cs`:
    - Speech-to-Text: `LocalWhisperService` vs `CloudTranscriptionService` (OpenAI Whisper API).
    - Assistant / Copilot: `LocalLlamaAgentService` vs `CloudLlmAgentService`.
    - Voiceover / SFX: `LocalTtsAndSfxService` vs `ElevenLabsAudioService`.
    - Background Removal: `OnnxRvmMattingService`.
- **`UniversalAiClient.cs`**:
  - Universal REST client capable of querying and fetching models from:
    - **Cloud:** Google Gemini (`gemini-2.5-flash`, `gemini-2.5-pro`), OpenAI (`gpt-4o`, `gpt-4o-mini`), Anthropic Claude (`claude-3-5-sonnet`), Groq (`llama-3.3-70b-versatile`).
    - **Local Servers:** LM Studio (`http://localhost:1234/v1`), Ollama (`http://localhost:11434/v1`), Local OpenAI-compatible endpoints.
- **`TimelineAgentPlugin.cs` (Semantic Kernel Plugin)**:
  - Exposes timeline actions as native tool-calls / functions to LLMs:
    - `CutClipAt(clipId, timeInSeconds)`
    - `SplitEveryInterval(intervalSeconds)`
    - `AddZoomKeyframes(startSec, endSec, startScale, endScale)`
    - `DeleteTimeRange(start, end)` (Ripple delete)
    - `ApplyTransitionToAllClips(transitionType, duration)`
    - `SetAudioGain(trackCode, gainDb)`
    - `AddTextOverlay(text, start, duration, style)`
    - `AddSoundEffect(sfxPrompt, timelinePosition)`
    - `RemoveSilence(thresholdSeconds)`
- **`AiProviderConfig.cs`**:
  - Secure configuration storage using Windows Data Protection API (DPAPI via `ProtectedData.Protect/Unprotect`) for API keys.
  - Model directory discovery (`%AppData%/VortexCutStudio/models`).
- **`OnnxRvmMattingService.cs`**:
  - Offline video subject matting using Robust Video Matting (RVM) in ONNX Runtime. Separates foreground humans/objects from video frames without a green screen.

---

### 3.4 `AiVideoEditor.App` (Avalonia UI 11 Presentation Layer)

The user interface is built with Avalonia UI 11 following the MVVM pattern (utilizing `CommunityToolkit.Mvvm`).

#### 1. Themes & Styling:
- **`StudioDarkTheme.axaml`**: Custom dark studio design system inspired by DaVinci Resolve and Adobe Creative Cloud (`#111215` deep background, `#1A1D24` surfaces, `#2A2F3D` borders, Studio Amber `#F59E0B` and Electric Violet `#7C3AED` accent highlights).

#### 2. Views & Layout (`AiVideoEditor.App.Views`):
- **`MainWindow.axaml`**:
  - **Top Menu & Toolbar:** File/Edit/View/Sequence menus, Razor (`C`), Selection (`V`), Slip (`Y`), Text (`T`), Split (`Ctrl+B`), Ripple Delete (`Shift+Del`), Freeze Frame, Undo/Redo, Magnet Snap, Zoom sliders.
  - **Dual Monitors:** Source Clip Monitor (for previewing imported assets before trimming) and Program Monitor (master timeline preview).
  - **Left Studio Drawer:**
    - *Media Bin:* Asset import, proxy toggle, thumbnails, duration metadata.
    - *Effects Catalog:* Visual filters (Blur, Camera Shake, Film Grain, Vignette, Glitch).
    - *Transitions Catalog:* Draggable transition blocks for cut joints and clip boundaries.
    - *Text & Typography Studio:* Floating text generators, styling presets.
    - *Auto Captions & Cut Pauses:* One-click speech transcription, clickable word seeking, silent gap removal.
  - **Center Timeline:** Multi-track canvas with timecode ruler, playhead, clip blocks, transition blocks, waveform visualizer.
  - **Right Inspector Panel:**
    - *Transform & Crop:* Sliders and number boxes for position, scale, rotation, 4-edge crop, blend modes.
    - *Color Grading & Scopes:* Lumetri sliders, switchable Live Scopes (RGB Parade, Luma Waveform, Vectorscope, Histogram), 3-Way Color Wheels (Lift, Gamma, Gain), and Bezier Curve Editor.
    - *Audio DSP:* Gain, pan, 3-band EQ, compressor, reverb, dialogue isolate.
    - *Keyframe Mini-Timeline:* Synchronized keyframe graph with time ruler and interpolation selectors.
- **`AiCopilotView.axaml`** (**Smart Edit Assistant**):
  - Chat console for issuing natural language commands ("Cut every silent pause longer than 0.5s", "Add a zoom in on the second clip", "Apply Cross Dissolve to all clips").
- **`SettingsModal.axaml`** (**Automation & Cloud Connections**):
  - Configuration dialog for LM Studio, Ollama, Whisper GGUF, OpenAI, Gemini, Claude, and Groq connections.

#### 3. Custom Controls (`AiVideoEditor.App.Controls`):
- **`TimelineCanvasControl.cs`**:
  - High-performance SkiaSharp timeline rendering.
  - Handles mouse interaction: playhead scrubbing, dragging clips across tracks, trimming clip start/end boundaries, dragging standalone transition blocks, razor cutting on click.
- **`VideoPreviewCanvas.cs`**:
  - SkiaSharp canvas monitor with direct interactive bounding box manipulation (8 corner/edge resize handles, rotation handle, visual crop guides).
- **`VideoScopeControl.cs`**:
  - Vector-drawn real-time scopes: RGB Parade, Luma Waveform with IRE markings (0 to 100), circular Chrominance Vectorscope with skin-tone reference line, and overlapping RGB Histograms.
- **`RgbCurveEditorControl.cs` / `CurveEditorControl.cs`**:
  - Adobe Illustrator-style cubic spline curve editor. Click to add points, drag to modify, right-click to delete, with channel switching (Master, Red, Green, Blue).
- **`ColorWheelControl.cs`**:
  - Circular color wheel with radial hue/saturation puck and master luminance slider underneath.
- **`KeyframeGraphControl.cs`**:
  - Synchronized mini-timeline ruler with keyframe diamonds `◆`, supporting click-to-seek, dragging keyframes in time, multi-selection, and interpolation popups.
- **`ScrubbableNumberBox.cs`**:
  - Numeric input box that supports horizontal mouse dragging (scrubbing) for rapid value adjustment.
- **`AudioVuMeterControl.cs`**:
  - Segmented stereo audio level meter with color gradient (-60 dB green to -6 dB yellow to 0 dB red clip indicator).

---

## 4. Summary of Completed Features vs Architecture Rules

| Feature Area | Status | Key Code Implementation |
|---|---|---|
| **Text Studio Background** | ✅ **Fixed** | Default is 100% transparent (`BackgroundBoxEnabled = false`). Background box only rendered when explicitly toggled. |
| **Letter & Line Spacing** | ✅ **Fixed** | Implemented character-by-character glyph advance measurement in `TextRenderer.DrawTextWithSpacing`. |
| **Universal 4-Edge Crop** | ✅ **Fixed** | Unified `CropLeft`, `CropRight`, `CropTop`, `CropBottom` applied via `SKCanvas.ClipRect` to Video, Image, and Text clips. |
| **Standalone Transitions** | ✅ **Complete** | Premiere Pro-style transition blocks represented on timeline tracks (`ClipKind.Transition`), movable, trimmable, and rendered by `ShaderTransitionRenderer`. |
| **Lumetri Color & Scopes** | ✅ **Complete** | Rec.709 pipeline, 3-Way Color Wheels, Cubic Bezier LUT generation (`MonotonicCubicCurveSolver`), 8-channel HSL Secondary, Live RGB Parade, Luma IRE Waveform, Vectorscope, Histogram. |
| **Keyframe Mini-Timeline** | ✅ **Complete** | Dedicated time ruler synchronized with playhead; keyframe addition (`◆`), time dragging, easing interpolation (Linear, EaseIn, EaseOut, EaseInOutCubic, Hold). |
| **Unified Undo / Redo** | ✅ **Complete** | `UndoRedoManager` coalesces continuous slider/wheel drag gestures and listens to window-level tunneling `Ctrl+Z` / `Ctrl+Y`. |
| **Hardware Video Export** | ✅ **Complete** | Async frame piping to FFmpeg process with NVENC/QSV/AMF/CPU auto-fallback and sample-accurate 48 kHz audio mixdown. |
| **Speech-to-Text & Subtitles** | ✅ **Complete** | Whisper.net (local offline) or OpenAI Whisper API; creates subtitle clips on Track `C1`. |
| **Silence Stripping** | ✅ **Complete** | Automatically scans audio peaks, finds silent gaps below threshold dB, and ripple-cuts the timeline. |
| **Universal AI Routing** | ✅ **Complete** | Connects to local LM Studio, Ollama, GGUF models, or Cloud APIs (Gemini 2.5, OpenAI, Claude 3.5, Groq). |
| **Clean UI Terminology** | ✅ **Enforced** | No technical geek jargon in user-facing UI (e.g. "Remove Background", "Smart Edit Assistant", "Auto Captions & Transcript"). |

---

## 5. File Location Reference Index

For rapid navigation by incoming agents:

```
Domain Models & Math:
  - TimelineClip & TextOverlaySpec:    src/AiVideoEditor.Core/Models/TimelineClip.cs
  - ProjectDocument & MediaAsset:      src/AiVideoEditor.Core/Models/ProjectDocument.cs
  - TimelineService:                   src/AiVideoEditor.Core/Services/TimelineService.cs
  - UndoRedoManager:                   src/AiVideoEditor.Core/Commands/UndoRedoManager.cs
  - MonotonicCubicCurveSolver:         src/AiVideoEditor.Core/Services/MonotonicCubicCurveSolver.cs

Media & Rendering:
  - Compositor Engine:                 src/AiVideoEditor.MediaEngine/Rendering/CompositorEngine.cs
  - SkSL Shader Transitions:           src/AiVideoEditor.MediaEngine/Rendering/Shaders/ShaderTransitionRenderer.cs
  - Text & Glyph Renderer:             src/AiVideoEditor.MediaEngine/Rendering/TextRenderer.cs
  - Color Science & Scopes:            src/AiVideoEditor.MediaEngine/Shaders/ColorScienceProcessor.cs
  - MultiTrack Audio & VU Player:      src/AiVideoEditor.MediaEngine/Audio/MultiTrackAudioPlayer.cs
  - Frame Decoding & Cache:            src/AiVideoEditor.MediaEngine/Decoding/VideoFrameReader.cs
  - FFmpeg Hardware Export Engine:     src/AiVideoEditor.MediaEngine/Exporting/FFmpegExportEngine.cs

AI & Smart Tools:
  - Hybrid AI Router:                  src/AiVideoEditor.AiEngine/HybridAiRouter.cs
  - Timeline Semantic Kernel Plugin:   src/AiVideoEditor.AiEngine/TimelineAgentPlugin.cs
  - Universal AI Client:               src/AiVideoEditor.AiEngine/UniversalAiClient.cs
  - AI Configuration & DPAPI:          src/AiVideoEditor.AiEngine/Configuration/AiProviderConfig.cs

UI Controls & Views:
  - Main Window:                       src/AiVideoEditor.App/Views/MainWindow.axaml & .cs
  - Timeline Canvas Control:           src/AiVideoEditor.App/Controls/TimelineControl/TimelineCanvasControl.cs
  - Video Monitor Preview Canvas:      src/AiVideoEditor.App/Controls/VideoPreviewCanvas.cs
  - Video Scope Control:               src/AiVideoEditor.App/Controls/VideoScopeControl.cs
  - RGB Bezier Curve Editor:           src/AiVideoEditor.App/Controls/RgbCurveEditorControl.cs
  - 3-Way Color Wheels:                src/AiVideoEditor.App/Controls/ColorWheelControl.cs
  - Keyframe Graph Control:            src/AiVideoEditor.App/Controls/KeyframeGraphControl.cs
  - Inspector ViewModel:               src/AiVideoEditor.App/ViewModels/InspectorViewModel.cs
  - MainWindow ViewModel:              src/AiVideoEditor.App/ViewModels/MainWindowViewModel.cs
```

---

## 6. Guidelines & Best Practices for Future AI Agents

When implementing new features or refactoring existing code in this repository:
1. **Zero Dummy Code:** Never insert `// TODO: implement later` or placeholder stubs. Every method must have complete, working C# logic.
2. **Preserve Memory Efficiency:** In `MediaEngine` and `CompositorEngine`, avoid per-frame allocations on the managed heap. Reuse buffers and leverage SkiaSharp `SKBitmap` / `SKImage` pooling where applicable to prevent GC stutter during 60 FPS playback.
3. **Thread Safety:** Frame decoding (`VideoFrameReader`) and Audio mixing (`MultiTrackAudioPlayer`) execute on background threads. Always protect shared state with proper locking and never block the Avalonia UI dispatcher.
4. **Maintain Terminology Rules:** Keep UI strings user-friendly and professional (Premiere Pro / Resolve style). Do not introduce raw engineering labels or excessive "AI" acronyms in the visual UI.
5. **Undo/Redo Integrity:** Any new user-facing parameter or timeline mutation must be registered as a reversible command in `UndoRedoManager`. Continuous drag gestures must support coalescing.
