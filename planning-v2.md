# MASTER ENGINEERING BLUEPRINT: STUDIO-GRADE NLE DESKTOP APPLICATION
**Architecture & Execution Specification for AI Coding Agent (Antigravity)**
**Target Stack:** C# (.NET 8/9) + Avalonia UI 11 + SkiaSharp + FFMediaToolkit + Whisper.net + ONNX Runtime + LLamaSharp

---

## 1. PRODUCT VISION & STRICT UI/UX GUARDRAILS

### 1.1 Core Philosophy
Build a professional, high-precision Non-Linear Video Editor (NLE) modeled after **DaVinci Resolve Studio** and **Adobe Premiere Pro**. The application must feel like an industrial-grade creative tool: dense, responsive, keyboard-driven, and tactile. 

Intelligent automation engines (local transcription, silence cutting, smart masking, dialogue isolation, and natural-language timeline manipulation) are deeply integrated as **native studio tools**—never marketed or labeled with gimmicky "AI" buzzwords.

### 1.2 Strict Anti-AI-Slop & Branding Rules (MANDATORY)
The Coding Agent MUST strictly enforce these rules across all XAML views, view models, and strings:
1. **ZERO "AI" Nomenclature in the UI:**
   - **FORBIDDEN WORDS:** "AI", "Magic", "Sparkles", "Copilot", "Bot", "Assistant", "Smart AI", "Generate with AI", or sparkle/wand icons (`✨`, `🪄`).
   - **REQUIRED STUDIO TERMINOLOGY:**
     - Speech-to-Text / Whisper -> **"Dialogue Transcriber"** or **"Text-Based Editing"**
     - LLM Timeline Agent -> **"Command Console"**, **"Batch Script Assistant"**, or **"Edit Decision Automation"**
     - Silence Removal -> **"Strip Silence"** or **"Ripple Cut Dead Air"**
     - Background Removal (RVM/SAM2) -> **"Subject Matte Extractor"** or **"Auto Rotoscope"**
     - Voice Synthesis / TTS -> **"Voice Over Synthesizer"**
     - Audio SFX Generator -> **"Foley & Atmos Synthesizer"**
     - Audio Denoising -> **"Dialogue Isolate"**
2. **NO Visual "AI Slop":**
   - **FORBIDDEN STYLING:** Neon purple/pink/cyan gradients, glowing borders, glassmorphism (`AcrylicBorder`, `BackgroundBlur`), random semi-transparent overlays (`Opacity="0.3"`), giant rounded pill buttons (`CornerRadius="20"`), or floating chat bubbles.
   - **REQUIRED STYLING:** Solid, opaque, high-contrast dark surfaces, 1px crisp structural borders, compact rectangular controls (`CornerRadius="2"` or `"3"`), tabular monospace timecodes, and precision sliders/scrubbers.

---

## 2. PRECISION DARK MODE DESIGN SYSTEM (DAVINCI / PREMIERE INSPIRED)

### 2.1 Color Palette Tokens (`Themes/StudioDarkTheme.axaml`)
All colors must be strictly opaque (`#FF......`) unless rendering a video selection marquee or timeline clip highlight.

| Token Name | Hex Code | Usage |
| :--- | :--- | :--- |
| `Brush.Surface.Workspace` | `#121214` | Root window background & gaps between docked panels |
| `Brush.Surface.Panel` | `#1A1A1E` | Primary background for Media Pool, Inspector, Monitors, Timeline |
| `Brush.Surface.Header` | `#222226` | Panel tab headers, toolbar strips, track headers |
| `Brush.Surface.Elevated` | `#28282E` | Inputs, dropdowns, buttons, modal dialogs |
| `Brush.Surface.Hover` | `#32323A` | Hover state for buttons and list items |
| `Brush.Border.Hairline` | `#2E2E34` | 1px solid structural divider between panels and tracks |
| `Brush.Border.Focus` | `#4A90E2` | 1px border on active panel or focused input |
| `Brush.Text.Primary` | `#E6E6EB` | Primary labels, active tabs, clip titles |
| `Brush.Text.Secondary` | `#9A9AA6` | Property names, inactive tabs, secondary metadata |
| `Brush.Text.Muted` | `#5E5E68` | Disabled controls, empty state hints |
| `Brush.Accent.Primary` | `#3B82F6` | Active tool selection, active toggle state (Premiere Blue) |
| `Brush.Accent.Playhead` | `#EF4444` | Timeline Playhead needle & Recording indicator (Resolve Red) |
| `Brush.Track.VideoClip` | `#2B4C7E` | Video clip body on timeline (1px border `#4371B5`) |
| `Brush.Track.AudioClip` | `#235D4B` | Audio clip body on timeline (1px border `#388E73`) |
| `Brush.Track.SubtitleClip` | `#6B4C35` | Caption/Subtitle clip body on timeline (1px border `#9C7152`) |
| `Brush.Meter.Safe` | `#22C55E` | Audio VU Meter (-60 dB to -12 dB) |
| `Brush.Meter.Warn` | `#EAB308` | Audio VU Meter (-12 dB to -3 dB) |
| `Brush.Meter.Peak` | `#EF4444` | Audio VU Meter (-3 dB to 0 dB+) |

### 2.2 Typography & Layout Density
- **Primary UI Font:** `Inter` or `Segoe UI` (`11px` for dense properties, `12px` for panel headers and menus).
- **Timecode & Numeric Font:** `JetBrains Mono` or `Cascadia Mono` (`12px` to `16px`, `FontFeatureSettings="tnum"` for zero-jitter timecode scrubbing `01:04:22:18`).
- **Panel Docking Layout:** Classic 4-Quadrant NLE Workspace separated by `GridSplitter` (2px width, `#121214`):
  - **Top-Left Quadrant (30% width, 55% height):** Tabbed panel containing **Media Pool (Bins)**, **Effects & Transitions**, **Transcript Editor**, and **Sound Library**.
  - **Top-Center Quadrant (45% width, 55% height):** **Source Monitor** & **Program Monitor** (switchable or dual-view) with transport bar, timecode readout, and safe-margin overlays.
  - **Top-Right Quadrant (25% width, 55% height):** **Inspector** (Transform, Cropping, Opacity, Audio Gain, Matte Extraction, Color Wheels) & **Automation Console**.
  - **Bottom Quadrant (100% width, 45% height):** **Tool Strip** (Selection `V`, Razor `C`, Slip `Y`, Hand `H`, Snapping `N`), **Multi-Track Timeline Canvas**, and **Master Audio VU Meter** on the far right.

---

## 3. SYSTEM ARCHITECTURE & SOLUTION STRUCTURE

```text
StudioCutNLE/
├── StudioCutNLE.sln
├── src/
│   ├── StudioCut.Core/                  # Domain Models, NLE Timeline Engine, Serialization
│   │   ├── Models/
│   │   │   ├── ProjectDocument.cs       # Root project container (.scproj)
│   │   │   ├── MediaAsset.cs            # Imported file metadata, proxy path, waveform cache
│   │   │   ├── TimelineSequence.cs      # Frame rate, resolution, tracks collection
│   │   │   ├── Track.cs                 # V1, V2, A1, A2, C1 (Mute, Solo, Lock, Height)
│   │   │   ├── TimelineClip.cs          # SourceIn, SourceOut, TimelineStart, Duration, Effects
│   │   │   ├── TransitionSpec.cs        # CrossDissolve, DipToBlack, Wipe, Push
│   │   │   └── TranscriptSegment.cs     # Word-level & phrase-level timecoded text
│   │   ├── Commands/                    # Command Pattern for 100-step Undo/Redo stack
│   │   │   ├── IEditCommand.cs
│   │   │   ├── SplitClipCommand.cs      # Razor tool execution
│   │   │   ├── RippleDeleteCommand.cs   # Strip silence / gap closing
│   │   │   ├── MoveTrimClipCommand.cs   # Roll, Ripple, Slip, Slide edits
│   │   │   └── BatchTimelineMutation.cs # Applied by Automation Console
│   │   └── Services/
│   │       ├── ProjectSerializer.cs     # System.Text.Json save/load
│   │       └── TimecodeCalculator.cs    # Frame <-> SMPTE Timecode (24/25/29.97/30/60 fps)
│   │
│   ├── StudioCut.MediaEngine/           # Video/Audio Decode, Skia Compositor, FFmpeg Export
│   │   ├── Ingest/
│   │   │   ├── MediaProber.cs           # FFprobe / FFMediaToolkit metadata reader
│   │   │   ├── ThumbnailStripService.cs # Generates cached filmstrip bitmaps per clip
│   │   │   ├── WaveformPeakService.cs   # Extracts min/max PCM peaks (.pkf cache)
│   │   │   └── ProxyGeneratorService.cs # Optional 540p H.264 fast-scrub proxy builder
│   │   ├── Playback/
│   │   │   ├── FrameCacheManager.cs     # Ring buffer of decoded SkiaSharp SKBitmaps
│   │   │   ├── VideoCompositor.cs       # Renders active V-tracks, transforms, transitions, LUTs
│   │   │   ├── AudioMixerEngine.cs      # NAudio multi-track mixer, real-time dB metering, SFX
│   │   │   └── ClockSynchronizer.cs     # High-resolution master audio clock driving video frames
│   │   └── Render/
│   │       └── FFmpegRenderPipeline.cs  # Hardware-accelerated (NVENC/QSV/AMF) final export
│   │
│   ├── StudioCut.Automation/            # Local Models & External Provider Engines (No "AI" branding)
│   │   ├── Configuration/
│   │   │   ├── EngineProviderConfig.cs  # Local ONNX/GGUF paths vs External API Key endpoints
│   │   │   └── HardwareAccelerator.cs   # Detects CUDA, DirectML, CoreML, or CPU fallback
│   │   ├── Transcription/
│   │   │   ├── WhisperLocalEngine.cs    # Whisper.net word-level timestamp transcriber
│   │   │   ├── CloudTranscriptionClient.cs # External API fallback (OpenAI/Groq Whisper)
│   │   │   └── SilenceAnalyzer.cs       # RMS dB threshold + transcript gap detector
│   │   ├── Vision/
│   │   │   ├── SubjectMatteExtractor.cs # ONNX Runtime (RobustVideoMatting / SAM2)
│   │   │   └── SceneCutDetector.cs      # Histogram frame-difference optical cut detector
│   │   ├── AudioProcessing/
│   │   │   ├── DialogueIsolateEngine.cs # ONNX DeepFilterNet / Demucs vocal isolation
│   │   │   └── FoleySynthesizerService.cs # Local AudioLDM2/SFX or External ElevenLabs SFX API
│   │   └── EditScripting/
│   │       ├── TimelineActionPlanner.cs # Converts user instructions into structured JSON edits
│   │       ├── LocalLlamaExecutor.cs    # LLamaSharp GGUF local inference
│   │       └── RemoteEndpointExecutor.cs # Semantic Kernel / OpenAI-compatible HTTP client
│   │
│   └── StudioCut.Desktop/               # Avalonia UI 11 Frontend
│       ├── Themes/
│       │   └── StudioDarkTheme.axaml    # Strict opaque dark palette & control templates
│       ├── Controls/
│       │   ├── TimelineCanvasControl.cs # Custom SkiaSharp-rendered 60fps NLE timeline
│       │   ├── VideoMonitorControl.cs   # Custom SkiaSharp hardware-accelerated video viewport
│       │   ├── AudioVuMeterControl.cs   # Real-time stereo peak/RMS dB bar
│       │   └── TimecodeScrubberControl.cs # Drag-to-scrub SMPTE timecode control
│       ├── ViewModels/
│       │   ├── MainWorkspaceViewModel.cs
│       │   ├── MediaPoolViewModel.cs
│       │   ├── MonitorViewModel.cs
│       │   ├── TimelineViewModel.cs
│       │   ├── InspectorViewModel.cs
│       │   ├── TranscriptPanelViewModel.cs
│       │   └── EngineSettingsViewModel.cs
│       └── Views/
│           ├── MainWindow.axaml
│           ├── MediaPoolView.axaml
│           ├── MonitorView.axaml
│           ├── TimelineView.axaml
│           ├── InspectorView.axaml
│           ├── TranscriptPanelView.axaml
│           └── EngineSettingsModal.axaml
```

---

## 4. CORE NLE WORKFLOWS (IMPORT, PLAYBACK, TIMELINE, EXPORT)

### 4.1 Video & Audio Import Pipeline (Media Pool)
1. **Supported Formats:** `.mp4`, `.mov`, `.mkv`, `.avi`, `.webm`, `.wav`, `.mp3`, `.flac`, `.aac`, `.png`, `.jpg`.
2. **Ingest Flow (Drag & Drop or `Ctrl+I`):**
   - User drops files into the **Media Pool** (supports organizing into folder **Bins**).
   - `MediaProber` uses `FFMediaToolkit` to immediately extract:
     - Video Codec, Resolution (`1920x1080`, `3840x2160`), Exact Frame Rate (`23.976`, `29.97`, `60`), Frame Count, Pixel Format, Color Space.
     - Audio Codec, Sample Rate (`48000 Hz`), Channels (`Stereo`/`Mono`), Duration.
   - **Background Jobs (Non-Blocking):**
     - `WaveformPeakService` decodes the audio stream in chunks and writes a binary `.pkf` peak cache file so the timeline can draw ultra-crisp audio waveforms instantaneously at any zoom level.
     - `ThumbnailStripService` extracts poster frames and low-res filmstrip frames into `%LocalAppData%/StudioCut/Cache/Thumbnails/`.
     - Optional: If 4K+ media is imported and "Auto-Generate Optimized Media" is enabled, `ProxyGeneratorService` encodes a background 540p all-I-frame proxy for zero-latency scrubbing.

### 4.2 Dual Monitor System (Source & Program Monitors)
- **Source Monitor:** Double-clicking any asset in the Media Pool loads it into the Source Monitor. Users can scrub, press `I` (Mark In) and `O` (Mark Out), and click **Insert (`F9`)** or **Overwrite (`F10`)** to drop the sub-clip onto the active timeline track at the playhead.
- **Program Monitor:** Displays the live composite of the `TimelineSequence` at the current playhead frame.
- **Hardware Accelerated Rendering:** Both monitors use a custom `ISkiaSharpApiLeaseFeature` control (`VideoMonitorControl`) that paints decoded `SKBitmap` frames directly onto the GPU surface with zero XAML layout overhead.

### 4.3 Custom SkiaSharp Multi-Track Timeline (`TimelineCanvasControl`)
Standard UI lists (`ItemsControl`) lag when rendering hundreds of clips, waveforms, and keyframes. The timeline MUST be built as a custom SkiaSharp canvas supporting:
- **Track Headers (Left Column):** Track Name (`V1`, `V2`, `A1`, `A2`, `C1`), Lock Toggle, Hide/Mute Toggle, Solo Toggle, Track Height resize handle.
- **Timecode Ruler (Top Bar):** Dynamic tick marks adapting to zoom level (`Ctrl + MouseWheel` or `-`/`=` keys) down to individual frame boundaries.
- **Interactive Editing Operations:**
  - **Selection Tool (`V`):** Select single or marquee-select multiple clips; drag horizontally across time or vertically across tracks; drag left/right clip edges to **Trim In/Out**.
  - **Razor Tool (`C`):** Click anywhere on a clip to execute `SplitClipCommand` at the exact frame.
  - **Ripple Delete (`Shift + Delete`):** Deletes selected clip(s) and automatically shifts all subsequent clips left to close the gap.
  - **Magnetic Snapping (`N`):** Snaps clip edges and the playhead to adjacent cuts, markers, and the playhead position (visualized by a crisp 1px vertical guideline).
  - **Transitions & Keyframes:** Dragging a transition handle at the top corner of a clip creates a diagonal fade ramp; dragging a transition from the Effects panel onto a cut joint creates a cross-dissolve box bridging both clips.

---

## 5. INTEGRATED STUDIO AUTOMATION (HYBRID LOCAL & EXTERNAL API)

All automated tools run through a unified `IAutomationProvider` interface. In **Preferences -> Processing Engines** (`Ctrl+,`), the user can configure each module to run via **Local Hardware Engine** (ONNX / GGUF / Whisper.cpp) or **External Endpoint** (Bring Your Own API Key for OpenAI, Gemini, Anthropic, Groq, or ElevenLabs).

### 5.1 Text-Based Editing & Dialogue Transcriber (Whisper Engine)
- **Workflow:** Right-click a clip or sequence -> **"Transcribe Dialogue"**.
- **Engine:** Runs `Whisper.net` locally (`ggml-base`, `ggml-small`, or `ggml-large-v3-turbo`) or sends extracted 16kHz mono `.wav` to configured external API.
- **UI Presentation (`TranscriptPanelView`):**
  - Displays a clean, timecoded script synchronized with the Program Monitor playhead.
  - Clicking any word jumps the timeline playhead to that exact millisecond.
  - **Strike-to-Cut:** Selecting a sentence or paragraph in the Transcript panel and pressing `Delete` automatically executes a `RippleDeleteCommand` on the corresponding video/audio range in the timeline.
  - **Generate Captions Track:** Clicking **"Create Subtitle Track"** converts the transcript into editable `TimelineClip` items on Track `C1` with customizable broadcast styling (Font, Box Background, Stroke, Y-Offset).

### 5.2 Strip Silence (Automated Dead-Air Cutter)
- **Workflow:** Top Menu -> `Sequence` -> **"Strip Silence..."** (Opens a compact modal with numeric parameters).
- **Parameters:**
  - *Audio Threshold:* `-38 dB` (Slider `-60 dB` to `-10 dB`)
  - *Minimum Silence Duration:* `400 ms`
  - *Pad Start (Pre-Roll):* `80 ms`
  - *Pad End (Post-Roll):* `120 ms`
- **Execution:** Scans the cached waveform peaks (`.pkf`) in milliseconds, highlights silent regions on the timeline in a preview overlay, and upon clicking **Apply Ripple Cut**, slices and removes all dead air in a single undoable transaction.

### 5.3 Subject Matte Extractor (Rotoscoping / Background Removal)
- **Workflow:** Select a video clip -> Inspector Panel -> **Matte & Masking** section -> Toggle **"Isolate Foreground Subject"**.
- **Engine:** Executes `RobustVideoMatting (RVM)` or `SAM 2` via `Microsoft.ML.OnnxRuntime.DirectML` / `CUDA`.
- **Result:** Generates an alpha matte stream in the background cache, allowing the user to place new backgrounds, color grades, or text layers on `V1` behind the subject on `V2`.

### 5.4 Audio Post-Processing & Foley Synthesizer
- **Dialogue Isolate:** In the Inspector Audio tab, a **"Dialogue Isolate"** checkbox runs `DeepFilterNet` (ONNX) to strip HVAC hum, wind, and traffic noise in real-time or via background render cache.
- **Foley & Atmos Generator:** Inside the **Sound Library** tab, users can search local SFX folders or type a descriptive acoustic cue (e.g., *"Heavy wooden door creak, dry room"*) to synthesize a `.wav` asset directly into the active Media Bin using a local `AudioLDM2` ONNX pipeline or an external ElevenLabs Sound Effects API key.

### 5.5 Batch Edit Console (Natural Language to Edit Decision List)
- **UI Design:** Located in a compact dockable tab titled **"Command Console"**—styled like DaVinci Resolve's scripting console or a sleek command palette (`Ctrl+K`), NOT a chat messenger.
- **How It Works:**
  1. User enters a direct instruction: e.g., `"Cut all pauses longer than 0.5s, apply a 12-frame Cross Dissolve between all clips on V1, and normalize A1 dialogue to -14 LUFS"`.
  2. `TimelineActionPlanner` serializes the current `TimelineSequence` metadata + transcript timestamps into a compact JSON schema and dispatches it to either **LLamaSharp** (local `.gguf` model like `Qwen2.5-Coder-7B-Instruct`) or the user's configured **External API Key** (OpenAI / Claude / Gemini).
  3. The engine returns a deterministic array of `IEditCommand` operations (`SplitClip`, `RippleDelete`, `AddTransition`, `SetAudioGain`).
  4. The commands are executed against the timeline and pushed onto the **Undo Stack** (`Ctrl+Z` immediately reverts the entire batch operation).

---

## 6. TIMELINE SERIALIZATION SCHEMA (`.scproj`)

```json
{
  "projectVersion": "2.0",
  "projectName": "Commercial_Edit_Master",
  "settings": {
    "width": 1920,
    "height": 1080,
    "fpsNumerator": 24000,
    "fpsDenominator": 1001,
    "sampleRate": 48000
  },
  "mediaPool": [
    {
      "assetId": "asset_001",
      "name": "A_Cam_Interview_01.mov",
      "filePath": "D:/Footage/A_Cam_Interview_01.mov",
      "proxyPath": "C:/Users/Editor/AppData/Local/StudioCut/Proxies/asset_001_540p.mp4",
      "waveformCachePath": "C:/Users/Editor/AppData/Local/StudioCut/Peaks/asset_001.pkf",
      "durationFrames": 3600,
      "hasVideo": true,
      "hasAudio": true
    }
  ],
  "tracks": [
    {
      "trackId": "V1",
      "trackType": "Video",
      "index": 0,
      "isMuted": false,
      "isLocked": false,
      "clips": [
        {
          "clipId": "clip_101",
          "assetId": "asset_001",
          "name": "A_Cam_Interview_01.mov",
          "timelineStartFrame": 0,
          "durationFrames": 240,
          "sourceInFrame": 120,
          "sourceOutFrame": 360,
          "transform": { "positionX": 0.0, "positionY": 0.0, "scaleX": 1.0, "scaleY": 1.0, "rotation": 0.0, "opacity": 1.0 },
          "subjectMatteEnabled": false,
          "transitionIn": null,
          "transitionOut": { "type": "CrossDissolve", "durationFrames": 12 }
        }
      ]
    },
    {
      "trackId": "A1",
      "trackType": "Audio",
      "index": 0,
      "isMuted": false,
      "isLocked": false,
      "clips": [
        {
          "clipId": "clip_201",
          "assetId": "asset_001",
          "linkedClipId": "clip_101",
          "timelineStartFrame": 0,
          "durationFrames": 240,
          "sourceInFrame": 120,
          "sourceOutFrame": 360,
          "gainDb": 0.0,
          "pan": 0.0,
          "dialogueIsolateEnabled": true,
          "fadeInFrames": 4,
          "fadeOutFrames": 12
        }
      ]
    }
  ]
}
```

---

## 7. STEP-BY-STEP EXECUTION PHASES FOR ANTIGRAVITY AGENT

> **INSTRUCTIONS FOR CODING AGENT:** Execute these phases sequentially. Never leave `// TODO` placeholders in core logic. Ensure every phase compiles cleanly (`dotnet build`) before moving to the next.

### PHASE 1: Solution Scaffolding & Studio Dark Theme Foundation
1. Initialize the .NET 8 solution `StudioCutNLE.sln` with the four projects: `StudioCut.Core`, `StudioCut.MediaEngine`, `StudioCut.Automation`, and `StudioCut.Desktop`.
2. Install required NuGet packages:
   - `Avalonia`, `Avalonia.Desktop`, `Avalonia.Skia`, `CommunityToolkit.Mvvm`
   - `FFMediaToolkit`, `FFMpegCore`, `NAudio`, `SkiaSharp`
   - `Whisper.net`, `Whisper.net.Runtime`, `Microsoft.ML.OnnxRuntime.DirectML`, `LLamaSharp`
3. Build `StudioDarkTheme.axaml` implementing the exact hex tokens in Section 2.1. Ensure zero gradients, zero acrylic blur, 1px `#2E2E34` borders, and monospace timecode styles.
4. Construct `MainWindow.axaml` with the 4-Quadrant NLE docking grid (`MediaPoolView`, `MonitorView`, `InspectorView`, `TimelineView`) separated by resizable `GridSplitter` controls and a top native-styled menu strip (`File`, `Edit`, `Clip`, `Sequence`, `Workspace`, `Engines`).

### PHASE 2: Media Pool, File Import, Probing & Waveform Caching
1. Implement `MediaAsset`, `ProjectDocument`, and `TimecodeCalculator` in `StudioCut.Core`.
2. In `StudioCut.MediaEngine`, implement `MediaProber` using `FFMediaToolkit` to inspect imported video/audio files.
3. Implement `WaveformPeakService` to read audio streams via `NAudio` / `FFmpeg` and generate binary `.pkf` min/max peak arrays for fast timeline rendering.
4. Implement `ThumbnailStripService` to extract poster thumbnails for the Media Pool grid/list view.
5. Wire up `MediaPoolViewModel` and `MediaPoolView.axaml` supporting native OS File Picker (`Import Media...` / `Ctrl+I`) and Drag-and-Drop directly from Windows Explorer/macOS Finder into Bins.

### PHASE 3: Video Decoding, Audio Mixing & Dual Monitor Viewport
1. Build `FrameCacheManager` and `VideoCompositor` in `StudioCut.MediaEngine` to decode video frames on demand and composite active `V1`/`V2` tracks (applying Position, Scale, Rotation, Opacity, and Cross-Dissolve blending using `SkiaSharp`).
2. Build `AudioMixerEngine` using `NAudio` (`MixingSampleProvider`) to mix multiple audio tracks, apply clip gain (`dB`), pan, fade ramps, and calculate real-time Stereo Peak/RMS levels.
3. Build `VideoMonitorControl.cs` (custom Avalonia control using `ISkiaSharpApiLeaseFeature`) and `AudioVuMeterControl.cs`.
4. Implement transport controls in `MonitorViewModel`: Play/Pause (`Space`), J-K-L shuttle speed control (`J` reverse, `K` pause, `L` forward 1x/2x/4x), Step Back/Forward 1 Frame (`Left`/`Right` arrows), and Mark In/Out (`I`/`O`).

### PHASE 4: Precision SkiaSharp Multi-Track Timeline & Command System
1. Implement the `CommandManager` (Undo/Redo stack) and core commands (`InsertClipCommand`, `SplitClipCommand`, `MoveTrimClipCommand`, `RippleDeleteCommand`).
2. Build `TimelineCanvasControl.cs` using SkiaSharp to render:
   - Timecode ruler with frame-accurate tick marks.
   - Track headers (`V1`, `V2`, `A1`, `A2`, `C1`) with Mute, Solo, and Lock toggles.
   - Video clips with filmstrip thumbnails and Audio clips with high-density waveforms from `.pkf` caches.
   - Red playhead needle (`#EF4444`) with top scrub handle.
3. Implement full mouse interaction on `TimelineCanvasControl`:
   - Dragging assets from `MediaPoolView` directly onto timeline tracks.
   - Clicking with Razor tool (`C`) to split clips.
   - Dragging clip bodies to move and clip edges to trim In/Out points with magnetic snapping (`N`).
   - Selecting a clip updates `InspectorViewModel` to edit Transform, Volume, Fade, and Transitions in real time.

### PHASE 5: Studio Automation Engines (Hybrid Local & External API)
1. Create `EngineSettingsModal.axaml` allowing users to switch each capability between **Local Hardware Engine** (selecting model folder & GPU device) and **External Provider** (entering API Endpoint & Key, stored encrypted locally).
2. Implement `WhisperLocalEngine` (`Whisper.net`) and `CloudTranscriptionClient` connected to `TranscriptPanelView.axaml` for word-level text navigation, strike-to-cut ripple editing, and one-click `C1` Subtitle Track generation.
3. Implement `SilenceAnalyzer` and the **Strip Silence** modal dialog to automatically slice and ripple-delete dead air based on dB threshold and minimum duration.
4. Implement `SubjectMatteExtractor` (ONNX Runtime) and `DialogueIsolateEngine` wired to toggles inside the **Inspector** panel.
5. Implement `TimelineActionPlanner` and the **Command Console** panel so users can execute complex batch timeline edits via natural language (translated into undoable `IEditCommand` batches).

### PHASE 6: Hardware-Accelerated Export Pipeline & Project Persistence
1. Implement `ProjectSerializer` to save and load `.scproj` files (`Ctrl+S` / `Ctrl+O`) alongside an automatic 3-minute crash-recovery autosave.
2. Build the **Deliver / Export Modal** (`Ctrl+M`) with broadcast presets:
   - *H.264 / H.265 Master (MP4 / MOV)* with NVENC / QSV / AMF hardware encoding.
   - *Apple ProRes 422 (MOV)* for studio mastering.
   - *Audio Only (WAV 48kHz 24-bit / MP3 320kbps)*.
3. Implement `FFmpegRenderPipeline` to step frame-by-frame through `VideoCompositor`, pipe raw BGRA frames and mixed PCM audio to the FFmpeg encoder process, and display an accurate render progress bar, elapsed time, and estimated time remaining.

---

## 8. QUALITY ASSURANCE & VERIFICATION CHECKLIST
- [ ] **UI Audit:** Verify zero instances of the word "AI", sparkle icons, gradients, or translucent glass effects anywhere in the application.
- [ ] **Import & Scrub Test:** Import a 1080p/4K `.mp4` file; verify metadata appears in Media Pool, waveform draws on `A1`, and scrubbing the playhead updates the Program Monitor at 60fps.
- [ ] **NLE Editing Test:** Test `I`/`O` sub-clip insertion, Razor split (`C`), Trim edges, Ripple Delete (`Shift+Del`), and `Ctrl+Z` / `Ctrl+Y` Undo/Redo accuracy.
- [ ] **Automation Test:** Run **Strip Silence** and **Transcribe Dialogue** (both Local and External Key modes) and verify all generated cuts remain non-destructive and fully editable on the timeline.
- [ ] **Render Test:** Export a multi-track timeline with cuts, cross-dissolves, captions, and audio gain adjustments into a playable `.mp4` file with zero audio/video sync drift.
