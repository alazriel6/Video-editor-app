# MASTER ENGINEERING BLUEPRINT: VORTEX CUT STUDIO (RELEASE CANDIDATE v4.0)

**Zero-Gimmick Production Architecture, Precision UI Fix, Color Science & Hardware Export Specification for AI Coding Agent (Antigravity)**
**Target Stack:** C# (.NET 8/9) + Avalonia UI 11 + FFMediaToolkit / FFmpeg CLI Pipe + SkiaSharp + NAudio + Whisper.net + ONNX Runtime

---

## 1. CRITICAL BUG FIXES & ZERO-GIMMICK MANDATE (READ FIRST)

> **STRICT ENGINEERING AUDIT FOR ANTIGRAVITY AGENT:**
> The user reported four critical regressions that MUST be fixed immediately with production-grade code—NO placeholders, NO non-functional buttons, and NO visual "AI Slop":
> 1. **Broken Numeric Input Boxes Next to Sliders:** Avalonia's default `NumericUpDown` renders huge, ugly up/down spinner buttons that stretch row heights, clip text, or break layout alignment.
> 2. **Broken Contrast & Exposure Math:** In `SKColorFilter.CreateColorMatrix`, translation offsets (columns 4/index 4, 9, 14, 19) in SkiaSharp's implementation or pixel shaders were either scaled by `255f` incorrectly or lacked pivot clamping, causing the image to blow out to pure white/black when moving Contrast or Exposure slightly.
> 3. **Gimmick / Dead UI Controls:** Every single button, menu item, effect, transition, and tool MUST execute real C# logic on the `TimelineSequence`, `VideoCompositor`, and `AudioMixerEngine`. If a button exists in XAML, it MUST be bound to a working `ICommand`.
> 4. **Broken / Fake Export Encoder:** Exporting a video must NOT be a simulated progress bar or a simple file copy. It MUST render every composite frame (Video + Overlays + Transitions + Color Grade + Text) and mix all audio tracks (`A1` + `A2` + Effects) into a real **FFmpeg Hardware/Software Encoder Pipeline** producing a playable `.mp4` or `.mov`.

---

## 2. PRECISION PARAMETER ROW & SCRUBBABLE VALUE INPUT (DAVINCI / PREMIERE STYLE)

### 2.1 Why the Previous Numeric Input Looked Weird & How to Fix It
In Premiere Pro and DaVinci Resolve, parameter rows are ultra-compact (`26px` height) and NEVER use bulky HTML/Windows-style up/down spinner arrows.

**MANDATORY XAML & CONTROL SPECIFICATION (`Controls/PrecisionParameterRow.axaml`):**
- **DO NOT** use standard `NumericUpDown` with `ShowButtonSpinner="True"`.
- Build a dedicated 5-column `Grid` row with exact pixel widths and `Height="26"`:
  ```text
  Column="0" Width="95"  -> Property Name Label (e.g., "Contrast", #B0B4C0, 11.5px, VerticalAlignment="Center")
  Column="1" Width="48"  -> Keyframe Nav Cluster: [<] [◆] [>] (16x16px flat icon buttons, zero border unless hovered)
  Column="2" Width="*"   -> Slim Precision Slider (Track height 3px #2A2D37, Active fill #3B82F6, Thumb 10x10px solid circle)
  Column="3" Width="58"  -> Compact Scrubbable Value Box (Border #2E323E, Background #141519, CornerRadius="2", Height="22")
  Column="4" Width="22"  -> Reset Button [↺] (Resets to default value; dimmed #4E5260 when already at default, bright #E2E4EC when modified)
  ```

### 2.2 Dual-Mode Scrubbable & Editable Value Box (`Controls/ScrubbableNumberBox.cs`)
Implement a custom control for Column 3 that behaves identically to Premiere Pro / DaVinci Resolve:
1. **Scrub Mode (Default):**
   - Cursor is `SizeWestEast` (`↔`).
   - Displaying formatted text centered in `JetBrains Mono` `11px` (e.g., `100.0%`, `+1.25`, `-12.4 dB`, `0.0°`).
   - Clicking and dragging left/right immediately increments/decrements `Value` by `StepSize * deltaX` (Holding `Shift` = `10x` faster; Holding `Alt` = `0.1x` fine-tune).
2. **Direct Type Mode (On Single Click without Drag or Double-Click):**
   - Switches internally to a borderless `TextBox` with all numeric text selected (`SelectAll()`).
   - User types a number (e.g., `135` or `-15.5`) and presses `Enter` (or clicks away `LostFocus`):
     - Parses `double.TryParse`, clamps between `[Minimum, Maximum]`, commits to `UndoRedoManager`, and exits edit mode.
     - Pressing `Escape` cancels and restores the previous value.

---

## 3. FIXED COLOR SCIENCE & DAVINCI-STYLE 3-WAY COLOR WHEELS + SCOPES

### 3.1 Why Contrast & Exposure Were Broken (And the Exact Fix)
In SkiaSharp (`SKColorFilter.CreateColorMatrix(float[] rowMajor20)`), the matrix operates on normalized `[0.0f .. 1.0f]` color channels in newer Skia versions, OR if applied improperly, multiplying the translation column by exposure blows out midtones. Furthermore, linear contrast `(color - 0.5) * c + 0.5` without soft knee clamping causes ugly RGB clipping.

**Implement `StudioCut.MediaEngine/Shaders/ColorScienceProcessor.cs` using exact Rec.709 Color Math:**
```csharp
public static class ColorScienceProcessor
{
    public static SKColorFilter BuildColorPipeline(ColorGradingParameters p)
    {
        // 1. Exposure: smooth stops (-4.0 to +4.0 EV)
        float expGain = MathF.Pow(2.0f, Math.Clamp(p.Exposure, -4f, 4f) * 0.65f);

        // 2. Contrast: centered strictly at 0.5 mid-gray pivot (-100 to +100)
        // Maps -100 -> 0.25x slope, 0 -> 1.0x slope, +100 -> 2.25x slope
        float normContrast = Math.Clamp(p.Contrast, -100f, 100f) / 100f;
        float contrastSlope = normContrast >= 0f 
            ? 1.0f + (normContrast * 1.25f) 
            : 1.0f + (normContrast * 0.75f);
        float contrastOffset = 0.5f * (1.0f - contrastSlope);

        // 3. Brightness / Lift (-100 to +100) + Shadows/Highlights approximation
        float liftOffset = (Math.Clamp(p.Brightness, -100f, 100f) / 100f) * 0.20f
                         + (Math.Clamp(p.Shadows, -100f, 100f) / 100f) * 0.12f;
        float highlightGain = 1.0f + (Math.Clamp(p.Highlights, -100f, 100f) / 100f) * 0.25f;

        // 4. White Balance: Temperature (-100 Cool to +100 Warm) & Tint (-100 Green to +100 Magenta)
        float temp = (Math.Clamp(p.Temperature, -100f, 100f) / 100f) * 0.18f;
        float tint = (Math.Clamp(p.Tint, -100f, 100f) / 100f) * 0.14f;

        float rScale = expGain * highlightGain * (1.0f + temp + tint * 0.5f) * contrastSlope;
        float gScale = expGain * highlightGain * (1.0f - tint) * contrastSlope;
        float bScale = expGain * highlightGain * (1.0f - temp + tint * 0.5f) * contrastSlope;

        float totalOffsetR = contrastOffset + liftOffset + p.LiftWheelRed * 0.15f;
        float totalOffsetG = contrastOffset + liftOffset + p.LiftWheelGreen * 0.15f;
        float totalOffsetB = contrastOffset + liftOffset + p.LiftWheelBlue * 0.15f;

        // 5. Saturation (0% to 200%, 100% = 1.0) + Vibrance
        float sat = Math.Clamp(p.Saturation, 0f, 200f) / 100f;
        float vib = (Math.Clamp(p.Vibrance, -100f, 100f) / 100f) * 0.5f;
        float effectiveSat = Math.Clamp(sat + vib, 0f, 2.5f);

        const float lumR = 0.2126f, lumG = 0.7152f, lumB = 0.0722f;
        float sr = (1.0f - effectiveSat) * lumR;
        float sg = (1.0f - effectiveSat) * lumG;
        float sb = (1.0f - effectiveSat) * lumB;

        // Apply Gain Wheel multipliers
        rScale *= (1.0f + p.GainWheelRed * 0.5f);
        gScale *= (1.0f + p.GainWheelGreen * 0.5f);
        bScale *= (1.0f + p.GainWheelBlue * 0.5f);

        float[] matrix = new float[20]
        {
            rScale * (sr + effectiveSat), rScale * sg,                  rScale * sb,                  0f, totalOffsetR,
            gScale * sr,                  gScale * (sg + effectiveSat), gScale * sb,                  0f, totalOffsetG,
            bScale * sr,                  bScale * sg,                  bScale * (sb + effectiveSat), 0f, totalOffsetB,
            0f,                           0f,                           0f,                           1f, 0f
        };

        var baseFilter = SKColorFilter.CreateColorMatrix(matrix);

        // 6. Optional Gamma Wheel via 256-entry LUT table so midtones bend smoothly without clipping black/white!
        if (MathF.Abs(p.GammaWheelLum) > 0.001f || MathF.Abs(p.GammaWheelRed) > 0.001f ||
            MathF.Abs(p.GammaWheelGreen) > 0.001f || MathF.Abs(p.GammaWheelBlue) > 0.001f)
        {
            byte[] rLut = BuildGammaTable(1.0f - (p.GammaWheelLum + p.GammaWheelRed) * 0.45f);
            byte[] gLut = BuildGammaTable(1.0f - (p.GammaWheelLum + p.GammaWheelGreen) * 0.45f);
            byte[] bLut = BuildGammaTable(1.0f - (p.GammaWheelLum + p.GammaWheelBlue) * 0.45f);
            byte[] aLut = BuildIdentityTable();
            var gammaFilter = SKColorFilter.CreateTable(aLut, rLut, gLut, bLut);
            return SKColorFilter.CreateCompose(gammaFilter, baseFilter);
        }

        return baseFilter;
    }

    private static byte[] BuildGammaTable(float gamma)
    {
        gamma = Math.Clamp(gamma, 0.25f, 3.0f);
        byte[] table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            float normalized = i / 255f;
            float corrected = MathF.Pow(normalized, gamma);
            table[i] = (byte)Math.Clamp((int)MathF.Round(corrected * 255f), 0, 255);
        }
        return table;
    }
}
```

### 3.2 DaVinci Resolve 3-Way Color Wheels + Real-Time Video Scopes (`Controls/ColorWheelControl.cs`)
Inside the Inspector **Color** tab, include:
1. **Three Interactive Circular Color Wheels (Side-by-Side):**
   - **Lift (Shadows)** | **Gamma (Midtones)** | **Gain (Highlights)**.
   - Rendered cleanly with SkiaSharp: dark circular Hue/Saturation ring with a draggable crosshair puck in the center and a horizontal master luminancejog wheel underneath, plus a `[↺]` reset icon per wheel.
2. **Real-Time Luma Waveform & RGB Histogram Scope (`Controls/VideoScopeControl.cs`):**
   - Toggleable mini-panel at the top of the Color tab showing a live **RGB Histogram / Luma Parade** computed from the current Program Monitor `SKBitmap` (sampled on a 160x90 grid so it takes `< 0.4ms` CPU time!).

---

## 4. REAL HARDWARE-ACCELERATED FFMPEG EXPORT & ENCODER PIPELINE

The Export / Deliver system (`StudioCut.MediaEngine/Render/FFmpegExportEngine.cs`) MUST be 100% functional, supporting full multi-track video compositing and multi-track audio mixing.

### 4.1 Automatic FFmpeg Binary Detection & Hardware Encoder Probing
On startup and when opening the **Deliver / Export Modal (`Ctrl+M`)**:
1. Locate `ffmpeg` and `ffprobe` (check app directory `./ffmpeg/`, system `PATH`, or `FFMpegCore.GlobalFFOptions`).
2. Probe available hardware encoders by running `ffmpeg -hide_banner -encoders`:
   - **NVIDIA NVENC:** `h264_nvenc`, `hevc_nvenc`
   - **Intel QuickSync (QSV):** `h264_qsv`, `hevc_qsv`
   - **AMD AMF:** `h264_amf`, `hevc_amf`
   - **Apple VideoToolbox (macOS):** `h264_videotoolbox`, `hevc_videotoolbox`
   - **Universal CPU Fallback (Guaranteed to work on every PC):** `libx264`, `libx265`, `prores_ks`

### 4.2 Exact Two-Stage / Pipe Export Architecture (Zero Desync, Zero Frame Drops)
Never rely on fragile complex FFmpeg filtergraphs that fail when clips have custom SkiaSharp text, keyframes, or color matrices. Instead, use the **Industry-Standard NLE Frame Server Pipe Architecture**:

1. **Step 1 — Render Mixed Master Audio to Temporary WAV (`temp_master_audio.wav`):**
   - Instantiate `AudioMixerEngine` offline at `48000 Hz`, `16-bit/24-bit Stereo`.
   - Step from `Frame 0` (or `Mark In`) to `TotalTimelineFrames` (or `Mark Out`), mixing all unmuted audio tracks (`A1`, `A2`), applying clip volume keyframes, Constant Power Crossfades, Fade In/Out, Speed rates, and DSP effects (EQ/Compressor).
   - Write to `temp_master_audio.wav` using `NAudio.Wave.WaveFileWriter`.
2. **Step 2 — Pipe Composite Video Frames Directly into FFmpeg `stdin` (`rawvideo`):**
   - Launch `ffmpeg` process with arguments:
     ```text
     -y -f rawvideo -pix_fmt bgra -s {Width}x{Height} -r {Fps} -i pipe:0 -i "{temp_master_audio.wav}" -c:v {SelectedEncoder} -preset {Preset} -b:v {BitrateMbps}M -pix_fmt yuv420p -c:a aac -b:a 320k -movflags +faststart "{OutputFilePath}"
     ```
   - Loop `for (long frame = startFrame; frame < endFrame; frame++)`:
     - Call `VideoCompositor.RenderCompositeFrame(sequence, frame, width, height)` which composites `V1`, `V2`, Transitions, Keyframed Transforms, Color Grading Matrix, Video Effects, and `C1`/`V2` Styled Text into an `SKBitmap` (`SKColorType.Bgra8888`).
     - Write raw pixel bytes (`bitmap.GetPixels()` span) directly into `ffmpegProcess.StandardInput.BaseStream`.
     - Update the Export Modal UI:
       - **Live Preview Thumbnail** of the frame currently being encoded.
       - **Exact Percentage (`64.2%`)**, **Frame Counter (`Frame 1,540 / 2,400`)**, **Render Speed (`48.5 fps`)**, **Elapsed Time**, and **Estimated Time Remaining (ETA)**.
       - **Cancel Render** button (`CancellationTokenSource` cleanly terminates FFmpeg and deletes partial temp files) and **"Open Output Folder"** button upon 100% completion.

### 4.3 Export Modal Presets (`Views/DeliverExportModal.axaml`)
Provide clean, one-click studio presets alongside custom overrides:
- **YouTube / Web Master 1080p MP4** (`1920x1080`, H.264, `16 Mbps`, AAC `320 kbps`)
- **4K UHD Ultra Master MP4** (`3840x2160`, H.264/HEVC, `45 Mbps`, AAC `320 kbps`)
- **Vertical Shorts / Reels / TikTok 1080x1920** (`1080x1920`, 9:16, H.264, `18 Mbps`)
- **Apple ProRes 422 Master MOV** (`prores_ks -profile:v 2`, uncompressed PCM 24-bit audio)
- **Audio Only Master** (`.wav` Uncompressed 48kHz OR `.mp3` 320kbps)
- **Export Range Selector:** `Entire Timeline` vs `Mark In / Out Range ([I] - [O])`.

---

## 5. COMPLETE AUDIT OF PREMIERE PRO / DAVINCI NLE FEATURES (100% WORKING, ZERO GIMMICKS)

Every item in this table MUST be wired and verified before completion:

| Category | Feature Name | Exact Implementation & Verification |
| :--- | :--- | :--- |
| **Monitor & Transport** | **Source vs Program Monitor** | Switching tabs or double-clicking a Media Pool asset previews the raw clip in Source Monitor; editing/scrubbing timeline switches to Program Monitor. |
| **Monitor & Transport** | **J-K-L Shuttle & Frame Step** | `J` plays backward (`-1x`, `-2x`), `K` pauses, `L` plays forward (`1x`, `2x`, `4x`). `Left`/`Right` steps 1 exact frame. |
| **Monitor & Transport** | **Interactive Canvas Gizmo** | Clicking a clip/text in Program Monitor draws a `#3B82F6` bounding box with 8 resize handles + rotation handle + yellow center-alignment snap lines. |
| **Monitor & Transport** | **Preview Quality Selector** | Dropdown at bottom-right of monitor: `Full (100%)`, `Half (1/2 Fast)`, `Quarter (1/4 Ultra)` + Aspect Ratio switcher (`16:9`, `9:16`, `1:1`). |
| **Timeline Editing** | **3-Point Editing (Mark In/Out)** | Press `I` and `O` in Source Monitor or Timeline, then click `Insert (F9)` (pushes timeline right) or `Overwrite (F10)` (replaces timeline range). |
| **Timeline Editing** | **Linked Video + Audio Clips** | Dropping a video with audio onto `V1` automatically places its linked audio clip on `A1`. Moving, splitting, or trimming one moves both simultaneously unless **Unlinked (`Ctrl+Alt+L`)**. |
| **Timeline Editing** | **Roll, Ripple, Slip & Slide Tools** | `V` Select/Trim, `C` Razor Split, `Y` Slip Tool (shifts `SourceIn`/`SourceOut` inside clip without moving clip on timeline), `H` Hand Pan. |
| **Timeline Editing** | **Timeline Markers (`M`)** | Pressing `M` adds a colored marker on the timeline ruler or clip with a custom note/comment. Magnetic snapping snaps to markers. |
| **Timeline Editing** | **Track Header Controls** | Every track has working **Mute (`M`)**, **Solo (`S`)**, **Lock (`L`)**, and **Visibility (`👁`)** toggles, plus right-click to `Add Track` / `Delete Track`. |
| **Keyframes** | **Dual-Pane Effect Controls** | Every slider row has `[<] [◆] [>]` + Scrubbable Number Box + Reset `[↺]` + synchronized Mini-Timeline showing exact seconds (`00:00.0s`) and draggable gold keyframe diamonds (`◆`). |
| **Effects & Transitions** | **Drag & Drop or 1-Click Apply** | 7 Video Transitions, 8 Video Effects, Constant Power Audio Crossfade, and 6 Audio DSP Filters (EQ, Compressor, Reverb, Pitch, Clean Voice). |
| **Text & Titles** | **WYSIWYG Title Designer** | Add Text (`T`), edit directly on canvas or Inspector: System Fonts, Size, Weight, Fill, Outline Stroke, Drop Shadow, Background Box, 9-Zone Alignment Grid, and In/Out Text Animations. |
| **Hybrid AI Tools** | **Local + Cloud AI Engine** | Supports **LM Studio** (`http://localhost:1234/v1`), **Ollama**, **Whisper.net Local**, and API Keys (**ChatGPT, Gemini, Claude, Groq**) for *Auto Subtitles*, *Remove Silence*, *Remove Background*, and *AI Command Console*. |

---

## 6. STEP-BY-STEP EXECUTION PLAN FOR ANTIGRAVITY AGENT

> **EXECUTION INSTRUCTIONS:** Execute Phases 1 through 5 sequentially. Do not leave any stub methods or unimplemented buttons. Run `dotnet build` after every phase and fix all compiler warnings/errors.

### PHASE 1: Fix Parameter Sliders, Scrubbable Numeric Inputs & Clean Inspector Layout
1. Create `ScrubbableNumberBox.cs` and `PrecisionParameterRow.axaml` (Height `26px`, 5-column layout: Label, `[<][◆][>]`, Slim Slider, Scrubbable/Typeable Number Box, `[↺]` Reset button).
2. Replace all broken `NumericUpDown` or static value labels across `InspectorView.axaml` (Transform, Crop, Color, Audio, Speed, Transitions, Text) with `PrecisionParameterRow`.
3. Ensure double-clicking any slider or clicking `[↺]` immediately resets that parameter to its default value and records an undoable `PropertyChangeCommand`.

### PHASE 2: Fix Color Science Matrix, Add 3-Way Color Wheels & Live Video Scope
1. Replace the broken Contrast/Exposure filter with `ColorScienceProcessor.cs` (Section 3.1) so `Exposure`, `Contrast`, `Highlights`, `Shadows`, `Brightness`, `Temperature`, `Tint`, `Saturation`, and `Vibrance` grade the video smoothly with zero clipping glitches.
2. Build `ColorWheelControl.cs` (Lift, Gamma, Gain circular wheels) and `VideoScopeControl.cs` (live RGB Histogram / Luma Waveform) inside the Color Inspector tab.
3. Wire all 7 Cinematic Look Presets (`Rec.709 Reset`, `Teal & Orange`, `Moody Matte`, `Golden Hour`, `Cyberpunk`, `Kodak 2383`, `Noir B&W`) so clicking a preset updates the sliders and color matrix immediately.

### PHASE 3: Complete NLE Timeline Polish (Linked Audio, Context Menus, Keyframe Time Ruler, Markers)
1. Implement **Linked Video/Audio Clip Synchronization**: importing/dragging a video onto `V1` automatically creates its linked audio waveform clip on `A1` (`LinkedClipId`). Moving, splitting (`Ctrl+B` / Razor `C`), or trimming one updates both unless unlinked via Right-Click -> `Unlink Video & Audio (Ctrl+Alt+L)`.
2. Verify all Right-Click Context Menus on Clips and Empty Gaps (`Close Gap`, `Cut`, `Copy`, `Paste`, `Paste Attributes`, `Duplicate`, `Split`, `Ripple Delete`, `Speed/Duration`, `Freeze Frame`, `Add Transition`) execute 100% working commands on `UndoRedoManager`.
3. Ensure the **Synchronized Mini Keyframe Timeline** in the Inspector clearly displays its time ruler (`00:00.0s`) and lets users drag keyframe diamonds (`◆`) horizontally with live time/value tooltips.

### PHASE 4: Build Real Hardware-Accelerated FFmpeg Export Pipeline (`DeliverExportModal`)
1. Implement `FFmpegExportEngine.cs` with hardware encoder auto-detection (`h264_nvenc`, `h264_qsv`, `h264_amf`, `libx264`, `libx265`, `prores_ks`).
2. Implement the Two-Stage Export Pipeline (Section 4.2): render mixed multi-track audio to temporary WAV, then step frame-by-frame through `VideoCompositor` and pipe raw `bgra` frames into FFmpeg `stdin`.
3. Upgrade `DeliverExportModal.axaml` with Preset Cards, Resolution/FPS/Bitrate/Encoder selectors, live frame preview during encoding, accurate progress bar + FPS speed + ETA, Cancel button, and "Open Output Folder" button.

### PHASE 5: Final End-to-End Verification & Zero-Gimmick Audit
1. Audit every button in the Top Bar, Media Pool, Monitor Transport, Timeline Tool Strip, Inspector Tabs, Effects Library, and AI Studio Modal—confirm zero dead buttons or dummy data remain.
2. Run `dotnet build` and verify clean compilation with 0 errors.