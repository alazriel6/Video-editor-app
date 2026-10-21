# MASTER ENGINEERING BLUEPRINT: VORTEX CUT STUDIO (NLE + HYBRID AI)
**Architecture, Bug-Fix & Full Feature Specification for AI Coding Agent (Antigravity)**
**Target Stack:** C# (.NET 8/9) + Avalonia UI 11 + LibVLCSharp / FFMediaToolkit + SkiaSharp + NAudio + Whisper.net + ONNX Runtime

---

## 1. CRITICAL ROOT-CAUSE FIXES (FROM SCREENSHOT AUDIT)

> **MANDATORY INSTRUCTIONS FOR ANTIGRAVITY AGENT:**
> Inspecting the current build (`StudioCut NLE 2.0` screenshot with `1.mp4`), the agent previously wrote placeholder/mock rendering code instead of real video decoding and scrolling logic. You MUST immediately fix these 5 critical defects:

### Defect 1: Monitor Shows a Dummy Vector Graphic Instead of Actual Imported Video (`1.mp4`)
* **Root Cause:** `VideoMonitorControl` is currently drawing a hardcoded SkiaSharp placeholder (blue grid + yellow circle + green rectangle) instead of decoding frames from `1.mp4`. Furthermore, dummy assets (`A_Cam_Interview_01.mov`, `B_Roll_Studio_Product_02.mov`) are hardcoded into the Media Pool.
* **Mandatory Fix:**
  1. **DELETE ALL MOCK RENDERING CODE** (the yellow circle / green body avatar and grid background) and **REMOVE** hardcoded dummy media items on startup.
  2. Implement **Real Video Frame Decoding** using **FFMediaToolkit** (or **LibVLCSharp.Avalonia** / **FFmpeg.AutoGen**) that reads actual video streams from the file path (`MediaAsset.FilePath`, e.g., `1.mp4`), extracts the exact `SKBitmap` / `WriteableBitmap` frame for the current timestamp, and renders it inside `VideoMonitorControl`.
  3. When `1.mp4` is imported, automatically demux/link its audio stream so **Track `V1`** receives the video clip (with real extracted filmstrip thumbnails) and **Track `A1`** receives the linked audio clip (with real PCM waveform peaks).

### Defect 2: Timeline Cannot Scroll Left/Right & Clips Cannot Be Dragged Smoothly
* **Root Cause:** `TimelineCanvasControl` lacks a `HorizontalScrollOffset` property, a bottom `ScrollBar`, and pointer capture logic (`e.Pointer.Capture(this)`) during `OnPointerPressed` / `OnPointerMoved`. Notice how `1.mp4` (264.20s long) gets cut off at `00:00:18:00` with no way to scroll right.
* **Mandatory Fix:**
  1. Add a **Horizontal ScrollBar** at the bottom of the timeline AND support:
     * **Mouse Wheel** = Scroll Timeline Left/Right horizontally.
     * **Ctrl + Mouse Wheel** = Zoom Timeline In/Out centered on the mouse cursor.
     * **Middle-Mouse Drag** or **Hand Tool (`H`) Drag** = Pan timeline left/right smoothly.
  2. Implement full **Clip Drag & Trim State Machine** inside `TimelineCanvasControl`:
     * Dragging the **center body** of a clip changes `TimelineStart` (horizontal time movement) or moves it across compatible tracks (`V1 <-> V2`, `A1 <-> A2`).
     * Dragging the **left 8px edge** trims `SourceIn` + `TimelineStart` (with `Cursor = SizeWestEast`).
     * Dragging the **right 8px edge** trims `SourceOut` + `Duration` (with `Cursor = SizeWestEast`).

### Defect 3: Editing Tools Look Like Plain Text Buttons Instead of Premiere / DaVinci Tool Dock
* **Root Cause:** Tools (`Select [V]`, `Razor [C]`, etc.) are currently plain text buttons scattered in a long row.
* **Mandatory Fix:** Build a dedicated **Premiere Pro / DaVinci Resolve Tool Strip** right above/beside the timeline with clear **Vector SVG Icons + Tooltips + Active Highlight (`#3B82F6`)**, grouped logically into **Cursor Tools**, **Timeline Edit Actions**, and **AI Quick Actions**.

### Defect 4: Missing Keyframe Animation System
* **Root Cause:** Inspector sliders (`Scale`, `Position X/Y`, `Opacity`, `Rotation`, `Volume`) are static values with no keyframe animation support.
* **Mandatory Fix:** Add a **Keyframe Diamond Button (`◇`)** next to every animatable property in the Inspector, a **Mini Keyframe Timeline / Curve Track** on clips and inside the Inspector, and Previous/Next Keyframe navigation buttons (`<` `◇` `>`).

### Defect 5: App Name & AI Integration Clarity
* **Mandatory Fix:**
  1. Rename the application to **Vortex Cut Studio** (Window Title: `Vortex Cut Studio — Professional Video & AI Editor`).
  2. Explicitly include **user-friendly AI features** alongside full manual control—supporting **LM Studio** (`http://localhost:1234/v1`), **Ollama** (`http://localhost:11434/v1`), **Local Whisper/ONNX**, AND **Cloud API Keys** (**ChatGPT / OpenAI**, **Google Gemini**, **Anthropic Claude**, **Groq**, **ElevenLabs**).

---

## 2. UI/UX DESIGN SYSTEM: CLEAN, PREMIUM DARK MODE (USER-FRIENDLY PRO NLE)

### 2.1 Visual Style Rules (Anti-Slop, High Readability)
* **Strict Solid Dark Palette:** No ugly neon gradients, no blurry glassmorphism, no random semi-transparent overlays.
* **Color Tokens:**
  * `Workspace Background`: `#0F1014`
  * `Panel Surface`: `#17181E`
  * `Header / Toolbar Surface`: `#1F2129`
  * `Control / Button Surface`: `#282B36` (Hover: `#343846`)
  * `Border Hairline`: `#2C2F3B` (1px solid)
  * `Primary Accent (Active Tool / Selected Tab)`: `#3B82F6` (Clean Studio Blue)
  * `AI Tool Accent (Subtle & Clean Badge)`: `#10B981` (Emerald Studio Indicator for AI-ready tools)
  * `Keyframe Active Gold`: `#F59E0B` (Amber/Gold diamond indicator when keyframing is active, just like Premiere/Resolve)
  * `Playhead Red`: `#EF4444`
  * `Video Clip Color`: `#254B7C` (Header bar `#3468AB`)
  * `Audio Clip Color`: `#1E5E4A` (Header bar `#2B8569`)
  * `Subtitle Clip Color`: `#7C4D25` (Header bar `#AD6B34`)

### 2.2 Workspace Layout (Premiere Pro + DaVinci Resolve Hybrid)

```text
+----------------------------------------------------------------------------------------------------------+
| [Logo] Vortex Cut Studio   File  Edit  Clip  Sequence  Effects  AI Studio  View  Help   [Undo][Redo]     |
|                            Project: Commercial_Edit    [AI Provider: LM Studio / Gemini Connected] [Export]|
+---------------------------+----------------------------------------------+-------------------------------+
| [Media] [Effects] [Sub]   |  [Source Monitor]  [Program Monitor]   100%  | [Inspector] [AI Assistant]    |
| [Audio/SFX] [Transitions] | +------------------------------------------+ | Clip: 1.mp4                   |
|                           | |                                          | |                               |
| [+ Import Media] [Record] | |      REAL DECODED VIDEO FRAME HERE       | | TRANSFORM & MOTION      [Reset]|
| +-----------------------+ | |      (Interactive Canvas: Click & Drag   | | Position X/Y  [<][◇][>] 0, 0  |
| | [Thumb] 1.mp4         | | |       bounding box to move/scale video)  | | Scale         [<][◇][>] 100%  |
| | 1920x1080 • 30fps     | | +------------------------------------------+ | Rotation      [<][◇][>] 0.0°  |
| | 04:24.20   [+ Add]    | | 00:00:03:19            [Safe Zone] [Fit] 04:24| Opacity       [<][◇][>] 100%  |
| +-----------------------+ | [|<] [-1F] [Play/Pause (Space)] [+1F] [>|]   |                               |
|                           | [Mark In (I)] [Mark Out (O)] [Insert] [Over] | KEYFRAME GRAPH (Clip Time)    |
|                           |                                              | |---◆---------◆------------◆--|
+---------------------------+----------------------------------------------+-------------------------------+
| TIMELINE TOOLBAR (Icon + Label + Shortcut Tooltip)                                                       |
| [↖ Select (V)] [✂ Blade/Cut (C)] [↔ Trim/Slip (Y)] [✋ Hand/Pan (H)] [T Text] | [✂ Split] [🗑 Ripple Del] |
| [🧲 Snap (N)] [🔗 Link Audio] [◆ Add Keyframe (K)] [🚩 Marker (M)] | [🤖 Auto Cut Silence] [📝 Auto Sub] |
+--------------+------------------------------------------------------------------------------------+------+
| TRACK HEADER | 00:00:00     00:00:15     00:00:30     00:00:45     00:01:00     00:01:15          | dBFS |
| C1 Subtitles | [ "Halo semuanya..." ] [ "Hari ini kita..." ]                                      |  ||  |
| V2 Overlay   |               [ Logo.png ◆-------◆ ]                                               |  ||  |
| V1 Video 1   | [Thumb|1.mp4 =================◆=======|Transition|=======1.mp4 (Part 2)==========] |  ||  |
| A1 Audio 1   | [Waveform 1.mp4 ▄▆▃▁▁▄▇█▄▂▁▁▁▅▇▃▂=================▄▆█▄▂==========================] |  ||  |
| A2 Music/SFX | [BGM_Track.mp3 ▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄▃▄▅▄]           |  ||  |
|              | <================ [ HORIZONTAL TIMELINE SCROLLBAR ] =============================> | L  R |
+--------------+------------------------------------------------------------------------------------+------+
```

---

## 3. EXPANDED & EASY-TO-USE NLE TOOLSET (DA VINCI / PREMIERE INSPIRED)

Place a dedicated **Timeline Action Bar** directly between the Monitors and the Timeline Tracks so all tools are 1 click away:

### 3.1 Primary Pointer Tools (Left Group — Mutually Exclusive Mode)
1. **Selection Tool (`V`):** Select clips, drag clips horizontally (time) or vertically (tracks), drag clip edges to trim, drag keyframes.
2. **Blade / Razor Tool (`C` or `B`):** Hovering over any clip shows a vertical dashed red cut-preview line; left-clicking immediately splits the clip (and its linked audio) at that exact frame.
3. **Ripple / Slip Tool (`Y`):** Trim a clip edge and automatically shift all subsequent clips so no blank gap is left behind.
4. **Hand / Pan Tool (`H`):** Click and drag anywhere on the timeline background to pan left/right/up/down smoothly.
5. **Text Overlay Tool (`T`):** Click to insert a customizable Title / Text clip directly at the playhead on `V2` or `C1`.

### 3.2 Instant Edit Action Buttons (Center Group — One-Click Execution)
1. **Split at Playhead (`Ctrl+B` or `S`):** Instantly cuts the selected clip (or all unlocked tracks) right at the red playhead line—works even when Selection Tool is active (CapCut/DaVinci speed workflow).
2. **Delete (`Del`) vs Ripple Delete (`Shift+Del`):**
   * `Delete`: Removes clip, leaves space.
   * `Ripple Delete`: Removes clip AND closes the gap automatically.
3. **Freeze Frame (`Shift+F`):** Captures the exact video frame at the playhead and inserts a 3-second freeze-frame segment.
4. **Speed / Duration (`Ctrl+R`):** Opens a quick popover (`0.25x`, `0.5x`, `1x`, `1.5x`, `2x`, `4x`, `Reverse`).
5. **Detach / Link Audio (`Ctrl+Alt+L`):** Separates the audio stream of `1.mp4` onto `A1` so video and audio can be trimmed or moved independently.
6. **Add Marker (`M`):** Drops a color-coded comment marker on the timeline ruler or clip.

### 3.3 Quick AI Action Buttons on Timeline Bar (Right Group)
1. **Auto Subtitles:** 1-click speech-to-subtitle track (`C1`) generation using Local Whisper or Cloud API.
2. **Smart Silence Cut:** 1-click dead-air detector with visual preview before cutting.
3. **Enhance Voice:** 1-click noise removal & vocal clarity filter on selected audio clip.
4. **AI Edit Assistant (`Ctrl+K`):** Opens/focuses the AI Assistant panel to perform automated edits via prompt.

---

## 4. FULL KEYFRAME ANIMATION SYSTEM (LIKE PREMIERE PRO / DAVINCI)

Users must be able to animate **Scale (Zoom in/out)**, **Position X/Y (Pan)**, **Rotation**, **Opacity (Fade)**, **Crop (Left/Right/Top/Bottom)**, and **Audio Volume (dB)** over time.

### 4.1 Data Model for Keyframes (`StudioCut.Core/Models/KeyframeTrack.cs`)
```csharp
public enum EasingType
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
    Hold
}

public class KeyframePoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Time in seconds relative to the start of the clip (0.0 to Clip.Duration)
    public double ClipRelativeTime { get; set; }
    public float Value { get; set; }
    public EasingType Easing { get; set; } = EasingType.EaseInOut;
}

public class AnimatableProperty
{
    public string PropertyName { get; set; } = string.Empty; // "Scale", "PosX", "PosY", "Rotation", "Opacity", "Volume"
    public float DefaultValue { get; set; }
    public bool IsKeyframingEnabled { get; set; }
    public List<KeyframePoint> Keyframes { get; set; } = new();

    public float EvaluateAt(double clipLocalTime)
    {
        if (!IsKeyframingEnabled || Keyframes.Count == 0)
            return DefaultValue;

        var sorted = Keyframes.OrderBy(k => k.ClipRelativeTime).ToList();
        if (clipLocalTime <= sorted[0].ClipRelativeTime) return sorted[0].Value;
        if (clipLocalTime >= sorted[^1].ClipRelativeTime) return sorted[^1].Value;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var k1 = sorted[i];
            var k2 = sorted[i + 1];
            if (clipLocalTime >= k1.ClipRelativeTime && clipLocalTime <= k2.ClipRelativeTime)
            {
                double duration = k2.ClipRelativeTime - k1.ClipRelativeTime;
                if (duration <= 0.0001) return k2.Value;
                float t = (float)((clipLocalTime - k1.ClipRelativeTime) / duration);
                t = ApplyEasing(t, k1.Easing);
                return k1.Value + (k2.Value - k1.Value) * t;
            }
        }
        return DefaultValue;
    }

    private static float ApplyEasing(float t, EasingType easing) => easing switch
    {
        EasingType.EaseIn => t * t,
        EasingType.EaseOut => 1f - (1f - t) * (1f - t),
        EasingType.EaseInOut => t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f,
        EasingType.Hold => 0f,
        _ => t
    };
}
```

### 4.2 How Keyframing Works in the UI (User-Friendly Flow)
1. **Inspector Controls:** Next to `Scale`, `Position X`, `Position Y`, `Rotation`, `Opacity`, and `Volume`, render a **3-button cluster**:
   * `[ < ]` Jump playhead to Previous Keyframe.
   * `[ ◇ ]` Toggle/Add Keyframe at current playhead position (Turns gold `◆` `#F59E0B` when the playhead is on a keyframe).
   * `[ > ]` Jump playhead to Next Keyframe.
2. **Auto-Keyframing Behavior:** Once a property has at least 1 keyframe (`IsKeyframingEnabled = true`), moving the playhead to another timestamp and dragging the `Scale` or `Position` slider **automatically inserts or updates a keyframe (`◆`)** at that timestamp—no tedious extra clicks required!
3. **Visual Keyframe Diamonds on Timeline & Inspector:**
   * Draw gold diamond icons (`◆`) directly on the selected clip inside `TimelineCanvasControl` AND inside a mini **Keyframe Track Lane** in the Inspector.
   * Users can **left-click and drag any `◆` diamond left/right** to change its timing, or **right-click a `◆` diamond** to choose `Linear`, `Smooth (Ease In/Out)`, or `Delete Keyframe`.

---

## 5. REAL VIDEO DECODING & TIMELINE SCROLL/PAN IMPLEMENTATION GUIDE

To prevent the coding agent from using fake placeholders again, implement these exact mechanisms:

### 5.1 Real Video Frame Extraction (`RealVideoDecoderService.cs`)
1. Initialize `FFMediaToolkit`:
   * Automatically locate `ffmpeg` shared libraries (`avcodec`, `avformat`, `avutil`, `swscale`) in `./runtimes/{os}/native/` or system PATH, and provide a 1-click **"Download FFmpeg Binaries"** prompt if missing.
2. When importing `1.mp4`:
   * Open `MediaFile.Open(filePath)` to read real `Info.Duration`, `Video.Info.FrameSize`, `Video.Info.AvgFrameRate`.
   * Create a `TimelineClip` on `V1` (`Duration = 264.20s`) and a linked `TimelineClip` on `A1` (`Duration = 264.20s`) when the user clicks **Add to Timeline (`+`)** or drags `1.mp4` onto the timeline.
3. During Playback (`Play` button / `Spacebar`) or Scrubbing:
   * A high-precision `DispatcherTimer` (interval `16.6ms` for 60fps) advances `CurrentTimeSeconds`.
   * `VideoCompositor` finds the active clip on `V1` and `V2` at `CurrentTimeSeconds`.
   * Computes `sourceTimestamp = clip.SourceIn + (CurrentTimeSeconds - clip.TimelineStart) * clip.Speed`.
   * Calls `mediaFile.Video.GetFrame(TimeSpan.FromSeconds(sourceTimestamp))` (buffered in a thread-safe LRU cache for smooth scrubbing) and converts the decoded `ImageData` directly into an `SKBitmap`.
   * Applies evaluated keyframe values (`Scale.EvaluateAt(localTime)`, `PosX.EvaluateAt(localTime)`, `Rotation.EvaluateAt(localTime)`, `Opacity.EvaluateAt(localTime)`) and active Transitions using `SKCanvas` matrices before drawing to `VideoMonitorControl`.

### 5.2 Working Timeline Horizontal Pan, Zoom & Drag (`TimelineCanvasControl.cs`)
Implement these exact properties and event handlers on `TimelineCanvasControl`:
* `public double ScrollOffsetSeconds { get; set; } = 0.0;` (Leftmost visible timestamp on the timeline)
* `public double PixelsPerSecond { get; set; } = 50.0;` (Zoom level: ranges from `2.0 px/s` for a 10-minute overview up to `600.0 px/s` for single-frame editing)
* **Coordinate Conversion Helpers:**
  * `double TimeToX(double timeSec) => TrackHeaderWidth + (timeSec - ScrollOffsetSeconds) * PixelsPerSecond;`
  * `double XToTime(double xPos) => Math.Max(0, ScrollOffsetSeconds + (xPos - TrackHeaderWidth) / PixelsPerSecond);`
* **Mouse & Scrollbar Interaction:**
  * Pair `TimelineCanvasControl` with a bottom horizontal `ScrollBar` whose `Maximum = Math.Max(60, SequenceTotalDuration + 30)` and `Value = ScrollOffsetSeconds`.
  * In `OnPointerWheelChanged(PointerWheelEventArgs e)`:
    * If `e.KeyModifiers.HasFlag(KeyModifiers.Control)`: Adjust `PixelsPerSecond *= (e.Delta.Y > 0 ? 1.25 : 0.8)` while keeping the time under `e.GetPosition(this).X` anchored.
    * Else: Pan horizontally via `ScrollOffsetSeconds = Math.Max(0, ScrollOffsetSeconds - (e.Delta.Y + e.Delta.X) * (50.0 / PixelsPerSecond))`.

---

## 6. UNIVERSAL HYBRID AI ARCHITECTURE (LM STUDIO, OLLAMA, GEMINI, CHATGPT, LOCAL ONNX)

Users can edit 100% manually OR use AI at any step. Open **AI Studio Settings** (`Engines` button in top bar) to configure providers easily.

### 6.1 Supported AI Providers (Plug & Play)

| Category | Local / Free / Offline Options | Cloud API Key Options (BYOK) |
| :--- | :--- | :--- |
| **LLM Timeline Co-Pilot & Smart Edit** | **LM Studio** (`http://localhost:1234/v1`)<br>**Ollama** (`http://localhost:11434/v1`)<br>**LLamaSharp** (Direct `.gguf` file loader) | **OpenAI (ChatGPT)** (`gpt-4o`, `gpt-4.1`)<br>**Google Gemini** (`gemini-2.5-flash`, `gemini-2.5-pro`)<br>**Anthropic Claude** (`claude-3-5-sonnet`)<br>**Groq / OpenRouter** (Any OpenAI-compatible URL) |
| **Speech-to-Text (Auto Subtitles & Silence Cut)** | **Whisper.net Local** (`ggml-tiny`, `ggml-base`, `ggml-large-v3-turbo`) | **OpenAI Whisper API**<br>**Groq Whisper Large-v3 API** (Ultra-fast) |
| **Video Background Removal (Matte)** | **ONNX Runtime Local** (`RobustVideoMatting` / `RMBG-2.0`) | **Replicate / Fal.ai Video Matting API** |
| **Voiceover (TTS) & Sound Effects (SFX)** | **Kokoro-82M ONNX** (Local TTS)<br>**Built-in Studio SFX Pack** + Local AudioLDM | **ElevenLabs API** (Text-to-Speech & Text-to-SFX)<br>**OpenAI TTS API** |

### 6.2 LM Studio & Cloud API Universal Client (`UniversalAiClient.cs`)
Build a clean, unified HTTP service that supports:
1. **LM Studio / Ollama Mode:**
   * Base URL field (default `http://localhost:1234/v1` for LM Studio, `http://localhost:11434/v1` for Ollama).
   * **"Test Connection & Fetch Loaded Models"** button that queries `GET /v1/models` and populates a dropdown with the exact model the user has loaded in LM Studio (e.g., `qwen2.5-7b-instruct`, `llama-3.2-8b`, `deepseek-r1`).
2. **Cloud API Key Mode:**
   * Provider dropdown: **OpenAI (ChatGPT)**, **Google Gemini**, **Anthropic Claude**, **Groq**, **ElevenLabs**, **Custom OpenAI-Compatible Endpoint**.
   * API Key input box with `[Show/Hide]` toggle and **"Verify Key"** button.
3. **AI Co-Pilot Capabilities (Inside the `AI Assistant` Tab):**
   * Users can chat naturally OR click **1-Click Preset Prompts**:
     * *"Cut all silent pauses longer than 0.6s"*
     * *"Add Smooth Zoom-In keyframe (1.0x to 1.15x) on the selected clip"*
     * *"Add Cross-Dissolve transitions (0.5s) between all clips on V1"*
     * *"Generate Indonesian/English Subtitles and highlight keywords"*
     * *"Create a cinematic 30-second highlight cut from 1.mp4"*
   * The AI responds with both a friendly explanation AND executes a structured JSON array of `TimelineEditAction` commands (`SplitClip`, `RippleDelete`, `AddKeyframe`, `AddTransition`, `AddTextOverlay`, `SetClipSpeed`) directly onto the timeline (fully undoable with `Ctrl+Z`).

---

## 7. EFFECTS, TRANSITIONS & COLOR GRADING SUITE

Make the **Effects & Transitions** panel rich, visual, and immediately usable:
1. **Video Transitions (Drag onto clip edges or click "Apply to Selected"):**
   * `Cross Dissolve` (Smooth alpha blend)
   * `Dip to Black` / `Dip to White`
   * `Directional Wipe` (Left, Right, Up, Down)
   * `Zoom Transition` (Whip zoom in/out with motion blur)
   * `Glitch / RGB Split Transition`
   * `Slide / Push Transition`
2. **Visual Effects & Color Grading (Inspector Color Tab):**
   * **Color Adjustments:** `Exposure`, `Contrast`, `Saturation`, `Temperature (Warm/Cool)`, `Tint`, `Highlights`, `Shadows`, `Vignette`. (Rendered in real time via `SKColorFilter.CreateColorMatrix`).
   * **Visual Filters:** `Cinematic Teal & Orange`, `Vintage Film`, `Black & White Noir`, `Sharpen`, `Gaussian Blur`, `Chromatic Aberration`, `Camera Shake`.

---

## 8. STEP-BY-STEP EXECUTION PLAN FOR ANTIGRAVITY AGENT

> **EXECUTION RULE:** Implement all phases with 100% real working C# logic. Do not use mock graphics or stub methods.

### PHASE 1: Fix Real Video Playback & Audio Extraction (`StudioCut.MediaEngine`)
1. Remove the hardcoded vector avatar drawing from `VideoMonitorControl.cs`.
2. Remove the fake startup items (`A_Cam_Interview_01.mov`, `B_Roll_Studio_Product_02.mov`) from `MediaPoolViewModel.cs`.
3. Implement `RealVideoDecoderService` using `FFMediaToolkit` (and automatic FFmpeg binary resolution) so importing any `.mp4`/`.mov`/`.mkv` file extracts real video frames, real duration, real resolution, and real audio PCM waveforms.
4. Ensure `VideoMonitorControl` displays the actual decoded `SKBitmap` of the imported video (`1.mp4`) both in **Source Monitor** (when double-clicked in Media Pool) and in **Program Monitor** (when placed on the timeline).
5. Ensure clicking **Play (`Space`)** plays both the real video frames and real audio via `NAudio` in sync.

### PHASE 2: Fix Timeline Scrolling, Zooming, Dragging & Tool Dock (`StudioCut.Desktop`)
1. Upgrade `TimelineCanvasControl.cs` to support `ScrollOffsetSeconds`, a functional horizontal `ScrollBar`, MouseWheel horizontal scrolling, `Ctrl + MouseWheel` cursor-centered zooming, and Middle-Click / Hand Tool (`H`) panning.
2. Implement complete mouse hit-testing and pointer-capture dragging on `TimelineCanvasControl`:
   * Scrubbing the red Playhead across the entire duration of `1.mp4` (e.g., all 264.20 seconds).
   * Dragging clips left/right and between tracks.
   * Trimming clip In/Out edges with magnetic snapping (`N`).
   * Splitting clips at the exact frame clicked with the Blade/Razor Tool (`C`) or `Split (Ctrl+B)`.
3. Redesign the **Timeline Toolbar** with clean vector icons, clear labels, keyboard shortcut badges, and quick action buttons (`Select`, `Blade`, `Slip`, `Hand`, `Text`, `Split`, `Ripple Delete`, `Speed`, `Freeze Frame`, `Add Keyframe`, `Auto Subtitles`, `Smart Silence Cut`).

### PHASE 3: Implement Full Keyframe Animation Engine (`StudioCut.Core` & `InspectorView`)
1. Add `AnimatableProperty` (`Scale`, `PosX`, `PosY`, `Rotation`, `Opacity`, `CropL/R/T/B`, `Volume`) to `TimelineClip`.
2. Update `InspectorView.axaml` to add `[<] [◇] [>]` keyframe controls next to every slider and an interactive **Keyframe Timeline Strip** inside the Inspector where users can see, drag, change easing (`Linear`, `EaseInOut`), and delete keyframe diamonds (`◆`).
3. Update `TimelineCanvasControl` to draw gold keyframe diamonds (`◆`) and volume/opacity automation curves directly on the clips.
4. Connect `VideoCompositor` so it evaluates `EvaluateAt(clipLocalTime)` on every frame, producing smooth real-time zoom, pan, rotation, and fade animations in the Program Monitor and during Final Export.

### PHASE 4: Implement Universal AI Hub (LM Studio, Ollama, Gemini, ChatGPT, Whisper)
1. Build `AiSettingsModal.axaml` ("AI & Processing Engines") with clear tabs:
   * **LLM Assistant Provider:** Choose between **LM Studio (Local Server)**, **Ollama (Local Server)**, **OpenAI (ChatGPT API Key)**, **Google Gemini (API Key)**, **Anthropic Claude (API Key)**, or **Custom OpenAI Endpoint**. Include a **"Test Connection"** button.
   * **Speech & Audio AI:** Choose between **Local Whisper.net** (with 1-click model downloader for `ggml-base.bin` / `ggml-small.bin`) or **OpenAI / Groq Whisper API Key**.
   * **Vision & Effects AI:** Local ONNX Runtime (`RobustVideoMatting`) vs Cloud API.
2. Build the **AI Assistant Panel** next to the Inspector with:
   * 1-Click Smart Action cards (*Auto Cut Silence*, *Generate Subtitles*, *Add Dynamic Zoom Keyframes*, *Apply Transitions to All Cuts*, *Auto Highlight Reel*).
   * Prompt input box where users can command the editor in Indonesian or English (e.g., *"Potong video 1.mp4 setiap 5 detik, kasih transisi Cross Dissolve, dan kasih efek zoom in perlahan"*), which the LLM translates into real timeline commands.

### PHASE 5: Transitions, Color Grading & Hardware-Accelerated MP4 Export
1. Implement real SkiaSharp shaders/blending for `Cross Dissolve`, `Dip to Black`, `Directional Wipe`, `Zoom Blur`, and `Glitch` transitions.
2. Add the **Color & Effects** tab in the Inspector (`Exposure`, `Contrast`, `Saturation`, `Temperature`, `Vignette`, and LUT presets).
3. Ensure the **Deliver / Export (`Ctrl+M`)** modal renders the actual decoded video frames, evaluated keyframe animations, transitions, subtitles, and mixed multi-track audio into a final `.mp4` file with a real-time progress bar.

---

## 9. VERIFICATION CHECKLIST BEFORE COMPLETION
- [ ] **Real Video Preview:** Importing `1.mp4` and placing it on `V1` displays the actual video frames of `1.mp4` in the Program Monitor (NO blue grid / yellow circle placeholder).
- [ ] **Full Timeline Scroll & Drag:** A 264-second video (`1.mp4`) can be scrolled smoothly from `00:00:00` to `00:04:24` using the horizontal scrollbar, mouse wheel, or Hand tool (`H`), and clips can be dragged, trimmed, and split anywhere.
- [ ] **Keyframes Work End-to-End:** Adding a `Scale` keyframe at `00:01.00` (`1.0x`) and another at `00:03.00` (`1.5x`) smoothly zooms the video in the Program Monitor when played.
- [ ] **LM Studio & Cloud API Keys Work:** Connecting to LM Studio (`http://localhost:1234/v1`) or entering a Gemini / ChatGPT API key successfully executes AI timeline edits and auto-captioning.