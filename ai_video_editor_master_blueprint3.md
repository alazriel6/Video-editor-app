# MASTER ENGINEERING BLUEPRINT: VORTEX CUT STUDIO (PRO NLE + HYBRID AI)
**Full Production Architecture, Bug-Fix & NLE Standard Specification for AI Coding Agent (Antigravity)**
**Target Stack:** C# (.NET 8/9) + Avalonia UI 11 + FFMediaToolkit / FFmpeg + SkiaSharp + NAudio + Whisper.net + ONNX Runtime

---

## 1. ANTI-AI-SLOP & ZERO TECHNICAL JARGON RULES (MANDATORY)

> **CRITICAL UI/UX AUDIT FOR ANTIGRAVITY AGENT:**
> Previous builds suffered from "AI Slop" (useless technical labels, raw debug strings, uneditable sliders, missing context menus, and placeholder graphics). You MUST enforce these strict design rules:

1. **Remove All Useless Technical Jargon:**
   * **DELETE** raw debug strings from the UI such as `"1.mp4 [0.00s • 264.20s]"`, `"TC 00:00:03:24"` overlaid on top of the video canvas, `"Isolate Foreground Subject (RVM Matte)"`, `"Insert C1 Caption"`, `"Foley & Stem Mix"`, `"Primary Picture"`, `"dBFS"`, or raw codec dumps.
   * **USE CLEAN, HUMAN-FRIENDLY LABELS:**
     * Track names: `Subtitles`, `Video 2 (Overlay)`, `Video 1`, `Audio 1 (Voice)`, `Audio 2 (Music & SFX)`.
     * Tool names: `Select`, `Cut / Razor`, `Slip`, `Hand`, `Add Text`, `Split`, `Delete Gap`, `Freeze Frame`, `Speed`, `Auto Subtitles`, `Remove Silence`.
     * Effect names: `Remove Background` (instead of `"Isolate Foreground Subject (RVM Matte)"`), `Clean Voice Noise` (instead of `"ONNX DeepFilterNet"`).
2. **Strict Anti-AI-Slop Visual Standard:**
   * **NO** neon gradients, **NO** glowing purple/cyan borders, **NO** translucent glass blurs, **NO** fake vector avatars in the video monitor, **NO** giant pill buttons.
   * **High-Craft Dark UI:** Solid matte dark surfaces (`#111215` workspace, `#18191E` panels, `#22242C` headers/inputs, `#2D303B` 1px crisp borders, `#3B82F6` focus/active accent, `#F59E0B` gold keyframes, `#EF4444` playhead).
3. **Every Slider MUST Have an Editable Numeric Box Next to It:**
   * Never place a dry, uneditable text label (`1.00x` or `100%`) beside a slider.
   * Every parameter row MUST be: `[Property Label]  [<][◇][>]  [Horizontal Slider]  [Editable NumericUpDown / Scrubbable TextBox]  [Reset ↺]`.
   * Clicking the number box lets the user **type an exact value** (e.g., `125.5` + `Enter`) or **drag left/right on the number** to scrub it like Premiere Pro / DaVinci Resolve. Double-clicking any slider immediately resets it to its default value.

---

## 2. GLOBAL KEYBOARD SHORTCUTS & 100-STEP UNDO/REDO ENGINE

### 2.1 Command Pattern Undo/Redo Stack (`StudioCut.Core/Commands/UndoRedoManager.cs`)
Every action in the app (Moving a clip, Trimming, Splitting, Deleting, Changing a Slider, Adding a Keyframe, Color Grading, Typing Text, or Running an AI Edit) MUST be wrapped in an `IEditCommand` and pushed to `UndoRedoManager`:
```csharp
public interface IEditCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

public class UndoRedoManager : ObservableObject
{
    private readonly Stack<IEditCommand> _undoStack = new();
    private readonly Stack<IEditCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string UndoTooltip => CanUndo ? $"Undo {_undoStack.Peek().Description} (Ctrl+Z)" : "Nothing to Undo";
    public string RedoTooltip => CanRedo ? $"Redo {_redoStack.Peek().Description} (Ctrl+Y)" : "Nothing to Redo";

    public void ExecuteCommand(IEditCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
```
* **Important Slider Undo Rule:** When dragging a slider, record `initialValue` on `PointerPressed` and only push `PropertyChangeCommand(oldVal, newVal)` on `PointerReleased` (or `Enter` in the numeric text box) so dragging a slider doesn't flood the Undo stack with 100 micro-steps.

### 2.2 Mandatory Global Keyboard Shortcuts (`MainWindow.axaml.cs` `OnKeyDown`)
Bind these exact industry-standard NLE shortcuts at the root window level:
* `Ctrl + Z`: **Undo**
* `Ctrl + Y` AND `Ctrl + Shift + Z`: **Redo**
* `Ctrl + X`: **Cut** selected clip(s) to internal clipboard
* `Ctrl + C`: **Copy** selected clip(s) to internal clipboard
* `Ctrl + V`: **Paste** clip(s) at the current Playhead position on the active track
* `Ctrl + Alt + V`: **Paste Attributes** (copies Transform, Color Grading, or Audio Effects from copied clip onto selected clip)
* `Ctrl + D`: **Duplicate** selected clip right after itself (or apply default Video Transition)
* `Ctrl + B` or `S`: **Split Clip at Playhead**
* `Delete` or `Backspace`: **Lift Delete** (removes selected clip, leaves timeline gap)
* `Shift + Delete`: **Ripple Delete** (removes selected clip AND shifts subsequent clips left to close the gap)
* `Space`: **Play / Pause**
* `J` / `K` / `L`: **Reverse Play / Stop / Fast Forward (1x, 2x, 4x)**
* `Left Arrow` / `Right Arrow`: **Step 1 Frame Backward / Forward** (`Shift + Left/Right` = Step 1 Second)
* `Up Arrow` / `Down Arrow`: **Jump Playhead to Previous / Next Cut Point**
* `Home` / `End`: **Jump Playhead to Start (`00:00:00`) / End of Timeline**
* `I` / `O`: **Mark In / Mark Out**
* `V`: **Select Tool** | `C`: **Cut/Razor Tool** | `Y`: **Slip Tool** | `H`: **Hand Pan Tool** | `T`: **Text Tool** | `N`: **Toggle Snapping**
* `Ctrl + =` / `Ctrl + -`: **Zoom Timeline In / Out** (`\` = **Fit Entire Timeline to Window**)

---

## 3. RIGHT-CLICK CONTEXT MENUS (PREMIERE PRO & DAVINCI RESOLVE STYLE)

### 3.1 Right-Clicking a Clip on the Timeline (`ClipContextMenu`)
When the user right-clicks any Video, Audio, or Text clip on `TimelineCanvasControl`, select that clip and open a clean, native dark `ContextMenu` containing:
1. **Cut** (`Ctrl+X`)
2. **Copy** (`Ctrl+C`)
3. **Paste** (`Ctrl+V`)
4. **Paste Attributes / Effects...** (`Ctrl+Alt+V`)
5. **Duplicate Clip** (`Ctrl+D`)
6. `--- Separator ---`
7. **Split at Playhead** (`Ctrl+B`)
8. **Delete** (`Del`)
9. **Ripple Delete (Close Gap)** (`Shift+Del`)
10. `--- Separator ---`
11. **Speed / Duration...** (`Ctrl+R`) -> Opens quick modal (`0.25x` to `4.0x`, `Reverse Playback`, `Ripple Timeline on Speed Change`)
12. **Add Freeze Frame (3s)** (`Shift+F`)
13. **Detach Audio to Audio Track** / **Link Video & Audio** (`Ctrl+Alt+L`)
14. `--- Separator ---`
15. **Add Transition** -> Submenu: `Cross Dissolve`, `Dip to Black`, `Dip to White`, `Zoom Transition`, `Glitch`, `Audio Constant Power Crossfade`
16. **Copy Keyframes** / **Clear All Keyframes on Clip**
17. **Reset All Effects & Color**
18. **Reveal File in Explorer**

### 3.2 Right-Clicking Empty Space on Timeline (`EmptyTrackContextMenu`)
1. **Paste Clip Here** (`Ctrl+V`)
2. **Close Gap (Ripple Delete Blank Space)** — *Just like Premiere Pro: right-clicking an empty gap between two clips and clicking "Close Gap" shifts everything on the right to snap flush to the left!*
3. **Insert Text / Title Here** (`T`)
4. **Add Track Above / Below**
5. **Fit Timeline to View** (`\`)

---

## 4. PREMIERE / DAVINCI DUAL-PANE KEYFRAME TIMELINE (WITH TIME RULER)

Users need to clearly see **exact seconds/minutes (`00:00.00`)** when placing and moving keyframes!

### 4.1 Inspector Effect Controls Keyframe Editor (`InspectorKeyframeEditorControl`)
Model the Inspector after **Adobe Premiere Pro's "Effect Controls" panel**:
* Split each Inspector section (e.g., `Transform`, `Color`, `Audio`) or the bottom half of the Inspector into a **Synchronized Mini-Timeline for the Selected Clip**:
  * **Top Mini Time Ruler:** Shows exact time ticks for the selected clip (both **Clip Time `00:00.00 -> 00:15.00`** and **Sequence Timecode `00:01:12:04`**) with its own **Mini Red Playhead** that moves in real-time sync with the main timeline playhead!
  * **Property Rows with Editable Value Boxes:**
    ```text
    PROPERTY        KEYFRAME          SLIDER & EDITABLE VALUE      CLIP KEYFRAME TIMELINE (00:00s ————— 00:10s)
    -----------------------------------------------------------------------------------------------------------
    Scale           [<] [◆] [>]   [====●========] [ 115.0 % ] [↺]  |----◆ (100%)----------◆ (115%)------------|
    Position X      [<] [◇] [>]   [======●======] [   0.0 px] [↺]  |------------------------------------------|
    Position Y      [<] [◇] [>]   [======●======] [   0.0 px] [↺]  |------------------------------------------|
    Rotation        [<] [◇] [>]   [======●======] [   0.0 ° ] [↺]  |------------------------------------------|
    Opacity         [<] [◆] [>]   [============●] [ 100.0 % ] [↺]  |◆ (0%)---◆ (100%)-------------------------|
    ```
* **Direct Keyframe Interaction:**
  * Clicking `[◇]` inserts a gold keyframe diamond (`◆` `#F59E0B`) at the current playhead timestamp.
  * If a row already has keyframes enabled (`◆`), moving the playhead and typing a new number or dragging the slider **automatically creates/updates the keyframe** at that exact second.
  * Dragging a `◆` diamond left/right in the mini-timeline displays a live tooltip: `Time: 00:02.45s | Value: 125%`.
  * Right-clicking a `◆` diamond opens: `Linear`, `Smooth (Ease In/Out)`, `Ease In`, `Ease Out`, `Hold (Step)`, `Delete Keyframe`.
* **On-Clip Keyframe Curve (Main Timeline):**
  * Each clip on `V1`/`V2`/`A1` also renders a thin rubber-band curve (`Opacity` on Video clips, `Volume dB` on Audio clips) with draggable keyframe dots directly on the timeline track, just like DaVinci Resolve!

---

## 5. COMPLETE EFFECTS & TRANSITIONS LIBRARY (VIDEO + AUDIO)

Create a dedicated, well-organized **Effects & Transitions Library** panel in the Top-Left Quadrant with search bar, category tabs, and 1-click **"Apply"** or **Drag-and-Drop onto Clip/Timeline**:

### 5.1 Video Transitions (`StudioCut.MediaEngine/Shaders/VideoTransitionRenderer.cs`)
 Can be placed at the **Start (Fade In)**, **End (Fade Out)**, or **Between Two Clips (Cut Joint)** with adjustable duration (`0.1s` to `5.0s`):
1. **Cross Dissolve:** Smooth opacity blend between Clip A and Clip B.
2. **Dip to Black / Dip to White:** Fades Clip A to solid black/white, then fades into Clip B.
3. **Whip Zoom In / Zoom Out:** Fast exponential scale transition with radial zoom blur.
4. **Slide / Push (Left, Right, Up, Down):** Clip B pushes Clip A off the canvas with smooth EaseInOut motion.
5. **Directional Wipe (Soft Edge):** Horizontal, vertical, or diagonal wipe across the frame.
6. **Flash / Strobe Cut:** High-energy white exposure flash at the cut point.
7. **Glitch / RGB Chromatic Split:** Digital horizontal slice offset + red/cyan channel separation.

### 5.2 Video Effects & Filters (`StudioCut.MediaEngine/Shaders/VideoEffectProcessor.cs`)
Multiple effects can be stacked on a clip and toggled on/off in the Inspector:
1. **Camera Shake (Handheld Motion):** Procedural Perlin-noise position/rotation shake (Adjustable `Frequency` & `Amplitude`).
2. **Gaussian Blur / Directional Motion Blur:** Smooth SkiaSharp `SKImageFilter.CreateBlur(sigmaX, sigmaY)`.
3. **Sharpen / Unsharp Mask:** Convolution kernel for crisp detail enhancement.
4. **Vignette:** Smooth dark/light radial falloff around frame corners (`Amount`, `Midpoint`, `Feather`).
5. **Chromatic Aberration (RGB Split):** Offsets Red and Blue channels horizontally/radially.
6. **Film Grain & Vintage Halation:** Subtle organic cinema noise and warm highlight glow.
7. **Mirror / Flip:** `Flip Horizontal`, `Flip Vertical`, `Kaleidoscope`.
8. **Drop Shadow & Glow / Stroke Border:** Ideal for Picture-in-Picture (`V2`) overlays and transparent PNGs/cutouts.

### 5.3 Audio Transitions (`StudioCut.MediaEngine/Playback/AudioTransitionProcessor.cs`)
1. **Constant Power Crossfade (+3dB Equal Power):** Smooth cosine/sine curve crossfade between two adjacent audio clips so volume never dips in the middle.
2. **Exponential Fade In / Fade Out:** Smooth logarithmic ramp from `-∞ dB` to `0 dB` (draggable via top-left and top-right fade handles directly on any audio clip).

### 5.4 Audio Effects & DSP Rack (`StudioCut.MediaEngine/Playback/AudioDspChain.cs`)
Implemented via real-time `NAudio` `ISampleProvider` DSP filters in the Inspector **Audio Tab**:
1. **Volume & Gain (`-60 dB` to `+24 dB`)** + **Stereo Pan (`-100 L` to `+100 R`)** (Both keyframeable!).
2. **Parametric EQ Presets:** `Flat`, `Vocal Presence Boost`, `Podcast Warmth (Bass Cut + High Boost)`, `Bass Boost`, `Telephone / Lo-Fi Radio`, `Muffled (Underwater / Behind Wall)`.
3. **Dynamics Compressor & Limiter:** Evens out quiet whispers and loud shouts; prevents `0 dBFS` clipping.
4. **Reverb / Room Echo:** `Small Room`, `Studio Booth`, `Large Concert Hall`, `Cinematic Canyon Echo` (`Dry/Wet Mix %` slider).
5. **Pitch Shifter:** Shift voice pitch (`-12` to `+12` semitones) without changing playback speed.
6. **AI Clean Voice (Noise Reduction):** 1-click background noise & hum remover.

---

## 6. REDESIGNED COLOR GRADING SUITE (CLEAN UI + REAL SKIASHARP COLOR MATRIX)

Fix the broken/ugly Contrast & Exposure controls and build a **Lumetri / DaVinci-style Color Panel** inside the Inspector:

### 6.1 Clean Color Grading UI Layout
Group color controls into 3 clean, collapsible cards with **colored slider tracks** (e.g., Blue-to-Amber track for Temperature, Green-to-Magenta for Tint, Grayscale-to-Rainbow for Saturation) and **editable numeric boxes + reset buttons**:
1. **Light & Tone:**
   * `Exposure` (`-5.0` to `+5.0`, default `0.0`)
   * `Contrast` (`-100` to `+100`, default `0`)
   * `Highlights` (`-100` to `+100`, default `0`)
   * `Shadows` (`-100` to `+100`, default `0`)
   * `Brightness / Lift` (`-100` to `+100`, default `0`)
2. **Color & White Balance:**
   * `Temperature` (`-100 Cool Blue` to `+100 Warm Amber`, default `0`)
   * `Tint` (`-100 Green` to `+100 Magenta`, default `0`)
   * `Saturation` (`0% B&W` to `200% Vibrant`, default `100%`)
   * `Vibrance` (`-100` to `+100`, default `0`)
3. **Creative Look Presets (1-Click Pills):**
   * `None (Rec.709)`, `Teal & Orange Cinema`, `Moody Matte Dark`, `Warm Golden Hour`, `Crisp Cyberpunk`, `Vintage Kodak 2383`, `High-Contrast Noir B&W`.

### 6.2 Exact SkiaSharp 4x5 ColorMatrix Implementation (`ColorGradeShader.cs`)
Ensure changing Exposure, Contrast, Saturation, and Temperature immediately updates the video frame using a real `SKColorFilter.CreateColorMatrix(float[] matrix)`:
```csharp
public static SKColorFilter CreateColorGradeFilter(
    float exposure,      // -5.0 to +5.0
    float contrast,      // -100 to +100
    float saturation,    // 0 to 200 (100 = normal)
    float temperature,   // -100 to +100
    float tint)          // -100 to +100
{
    float expScale = MathF.Pow(2f, exposure * 0.5f);
    float c = (contrast / 100f) + 1f;
    float tOffset = 0.5f * (1f - c);
    float s = saturation / 100f;

    // Luminance weights (Rec. 709)
    const float lumR = 0.2126f, lumG = 0.7152f, lumB = 0.0722f;
    float sr = (1f - s) * lumR, sg = (1f - s) * lumG, sb = (1f - s) * lumB;

    float tempOffset = (temperature / 100f) * 0.12f;
    float tintOffset = (tint / 100f) * 0.10f;

    float[] matrix = new float[20]
    {
        c * (sr + s) * expScale, c * sg * expScale,       c * sb * expScale,       0, tOffset + tempOffset,
        c * sr * expScale,       c * (sg + s) * expScale, c * sb * expScale,       0, tOffset - tintOffset,
        c * sr * expScale,       c * sg * expScale,       c * (sb + s) * expScale, 0, tOffset - tempOffset,
        0,                       0,                       0,                       1, 0
    };
    return SKColorFilter.CreateColorMatrix(matrix);
}
```

---

## 7. INTERACTIVE WYSIWYG VIDEO CANVAS & PRO TEXT / TITLE EDITOR

### 7.1 Direct On-Canvas Manipulation (`VideoMonitorControl.cs`)
The Program Monitor must NOT be a passive image viewer—it must be an **Interactive Transform Canvas** like Premiere Pro / CapCut / DaVinci:
1. **Aspect Ratio Viewport:** Render a crisp 16:9 (`1920x1080`), 9:16 (`1080x1920` Vertical Shorts/Reels), or 1:1 (`1080x1080`) black composition stage centered inside the dark monitor workspace, with zoom controls (`Fit`, `50%`, `100%`, `200%`).
2. **Interactive Transform Gizmo (Bounding Box):**
   * When any Video Overlay (`V2`), Image, or Text Clip is selected, draw a crisp blue (`#3B82F6`) bounding box around it on the canvas with **8 white square corner/edge handles** and a **top rotation handle**.
   * **Click & Drag Inside Box:** Moves `Position X` and `Position Y` directly on screen (with yellow center-snap guidelines when aligned to horizontal/vertical center!).
   * **Drag Corner Handles:** Scales the video/text proportionally in real time.
   * **Double-Click Text on Canvas:** Enters direct **In-Place Text Editing** mode (or focuses the Inspector Text Box) so the user can type directly.

### 7.2 Rich Text & Subtitle Styling Engine (`TextClipProperties`)
When a Text Clip (`V2` Title or `C1` Subtitle) is selected, the Inspector displays a comprehensive **Text & Typography Studio**:
* **Text Content Box:** Multi-line editable text box (updates canvas live as you type).
* **Font Family Dropdown:** Populated with all installed system fonts (`SKFontManager.Default.FontFamilies`, e.g., `Inter`, `Montserrat`, `Impact`, `Bebas Neue`, `Arial`, `Poppins`, `Roboto`) + **Font Weight** (`Regular`, `Bold`, `Black`, `Italic`).
* **Font Size Slider & Numeric Box:** `12 pt` to `300 pt` + **Letter Spacing (Tracking)** + **Line Height**.
* **Text Alignment Buttons:** `[Align Left] [Center] [Align Right]` + **Quick Position Grid** (9-point snap grid: Top-Left, Center, Bottom-Center Subtitle position, etc.).
* **Visual Text Styling (Toggleable Layers):**
  1. **Fill Color:** Color picker swatch + Opacity.
  2. **Outline / Stroke:** Toggle + Color picker + `Stroke Width (0px - 30px)` (drawn cleanly via `SKPaintStyle.StrokeAndFill` so text is readable over any video).
  3. **Drop Shadow:** Toggle + Shadow Color + `Blur Radius`, `Offset X`, `Offset Y`.
  4. **Background Box (Caption Pill):** Toggle + Background Color + `Opacity`, `Corner Radius`, `Padding X/Y`.
  5. **Text Animation Presets (In / Out / Loop):** `None`, `Typewriter`, `Fade Up By Word`, `Pop / Bounce In`, `Slide In`, `Karaoke Word Highlight`.

---

## 8. PERFORMANCE & RESPONSIVENESS OPTIMIZATION (60 FPS GUARANTEE)

To eliminate UI lag, stutter, and memory bloat:
1. **Asynchronous Background Frame Decoder & LRU Cache (`FrameCacheManager.cs`):**
   * Never call synchronous disk I/O (`GetFrame`) directly inside Avalonia's UI `Render()` thread!
   * Maintain a thread-safe `ConcurrentDictionary<long, SKBitmap>` ring buffer (caching ~45 frames around the playhead).
   * A dedicated background worker thread decodes upcoming frames ahead of the playhead during playback, and coalesces rapid scrub requests (`CancellationTokenSource`) so dragging the playhead across a 4-minute video (`1.mp4`) stays locked at **60 FPS**.
2. **Downsampled Preview Scaling (Draft / Full Quality Toggle):**
   * Add a quick resolution selector in the bottom-right of the Program Monitor: `Full (1080p)`, `1/2 (540p Fast)`, `1/4 (270p Ultra Fast)`—just like Premiere Pro! Decoding into a 540p buffer during rapid scrubbing reduces CPU/GPU load by **75%**.
3. **Virtualized Timeline Waveform & Thumbnail Rendering:**
   * In `TimelineCanvasControl.Render()`, compute `visibleStartTime = ScrollOffsetSeconds` and `visibleEndTime = ScrollOffsetSeconds + (Bounds.Width / PixelsPerSecond)`.
   * **Cull (skip drawing)** any clips, waveform peaks, or keyframe diamonds outside `[visibleStartTime, visibleEndTime]`.

---

## 9. STEP-BY-STEP EXECUTION PLAN FOR ANTIGRAVITY AGENT

> **EXECUTION MANDATE:** Implement every phase below completely. Verify `dotnet build` succeeds with 0 errors and all features work interactively.

### PHASE 1: UI Cleanup, Editable Numeric Sliders & Global Undo/Redo (`StudioCut.Core` & `StudioCut.Desktop`)
1. Strip out all technical debug strings (`dBFS`, `RVM Matte`, `Primary Picture`, `Foley & Stem Mix`, raw timecode overlay on video) and replace with clean studio labels.
2. Build a reusable `StudioSliderRow` control combining `[Label] + [<][◇][>] Keyframe Buttons + [Slider] + [Editable Scrubbable NumericBox] + [Reset Button]`.
3. Implement `UndoRedoManager` (100-step stack) and wire `Ctrl+Z`, `Ctrl+Y`, `Ctrl+Shift+Z`, `Ctrl+X`, `Ctrl+C`, `Ctrl+V`, `Ctrl+D`, `Ctrl+B`, `Del`, `Shift+Del`, and `Space` in `MainWindow`.

### PHASE 2: Timeline Right-Click Context Menus, Smooth Scrolling & Keyframe Mini-Timeline
1. Attach comprehensive right-click `ContextMenu` to `TimelineCanvasControl` (both for Clips: *Cut, Copy, Paste, Duplicate, Split, Ripple Delete, Speed/Duration, Freeze Frame, Detach Audio, Add Transition, Clear Keyframes*; and for Empty Gaps: *Close Gap, Paste Here, Insert Text*).
2. Ensure horizontal scrolling (Scrollbar, MouseWheel, Middle-click pan, Hand tool `H`), cursor-centered zooming (`Ctrl+Wheel`), and clip drag/trim work smoothly across full-length videos (`1.mp4`).
3. Build the **Premiere-style Dual-Pane Keyframe Editor** in the Inspector with a synchronized **Mini Time Ruler (showing exact seconds `00:00.0s`)** so users can see, add, drag, and change easing on keyframes effortlessly.

### PHASE 3: Real Video Playback, Interactive Canvas Bounding Box & Pro Text Editor
1. Ensure `RealVideoDecoderService` + `FrameCacheManager` decodes real video frames from `1.mp4` asynchronously with zero UI blocking and supports `Full / 1/2 / 1/4` preview quality.
2. Add the **Interactive Transform Gizmo** in `VideoMonitorControl` so users can click and drag video layers or text directly on the monitor canvas to move, scale, and rotate them.
3. Implement the full **Text & Title Studio** in the Inspector (Font family picker, size, bold/italic, fill color, outline stroke, drop shadow, background box, alignment grid, and text animations).

### PHASE 4: Complete Video/Audio Effects, Transitions & Color Grading Matrix
1. Build the **Effects & Transitions Library** panel (Top-Left tab) with **Video Transitions** (*Cross Dissolve, Dip to Black, Whip Zoom, Slide, Wipe, Glitch*), **Video Effects** (*Camera Shake, Blur, Sharpen, Vignette, RGB Split, Film Grain*), **Audio Transitions** (*Constant Power Crossfade, Fade In/Out*), and **Audio DSP Effects** (*EQ, Compressor, Reverb, Pitch Shift, Clean Voice*).
2. Upgrade the **Color Grading** panel with clean light/color sliders and real-time `SKColorFilter.CreateColorMatrix` rendering plus 1-click LUT presets.

### PHASE 5: Hybrid AI Studio (LM Studio, Ollama, ChatGPT, Gemini, Local Whisper)
1. Ensure seamless manual + AI hybrid workflow: users can connect **LM Studio** (`http://localhost:1234/v1`), **Ollama**, or **API Keys (OpenAI ChatGPT, Google Gemini, Claude, Groq)** in the **AI Studio Settings** modal, and use 1-click AI tools (*Auto Subtitles*, *Remove Silence*, *Remove Background*, *AI Timeline Co-Pilot*) that push undoable commands onto `UndoRedoManager`.