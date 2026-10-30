# MASTER ENGINEERING BLUEPRINT: VORTEX CUT STUDIO (v5.0 - DEFINITIVE BUGFIX & STUDIO RELEASE)
**Target Stack:** C# (.NET 8/9) + Avalonia UI 11 + SkiaSharp + FFMediaToolkit + NAudio + Whisper.net + ONNX Runtime + LLamaSharp

---

## 1. CRITICAL BUG FIXES & ARCHITECTURAL REPAIRS (MANDATORY EXECUTION)

The Coding Agent MUST immediately audit the codebase and resolve these 6 critical bugs before adding new features. ZERO `// TODO` or dummy implementations are allowed.

### BUG FIX 1: Text & Typography Studio (`Spacing` & `Transparent Background` Broken)
- **Root Cause 1 (Background Box):** Text clips currently render with a hardcoded semi-transparent black background rectangle (`SKColors.Black.WithAlpha(...)`) or an uninitialized alpha surface, making it impossible to have clean floating text.
- **Fix 1 (Remove Default Text Background):**
  - On the `TextClipData` / `TimelineClip` model, set `HasBackgroundBox = false` by default.
  - When rendering text onto the SkiaSharp canvas (`VideoCompositor.cs`), clear the text layer buffer with `SKColors.Transparent` (`canvas.Clear(SKColors.Transparent)`).
  - ONLY draw the background rounded rectangle if `clip.TextProperties.EnableBackground == true`. When enabled, allow the user to pick the exact `BackgroundColor` and `BackgroundOpacity` (`0%` to `100%`). When unchecked (`false`), the text MUST have 100% transparent background.
- **Root Cause 2 (Character & Line Spacing Not Working):** `SkiaSharp` `SKCanvas.DrawText()` does not apply custom tracking/letter-spacing properly on all fonts, and multi-line splitting ignores line height.
- **Fix 2 (Manual Glyph Positioning for Spacing):**
  - Implement custom character-by-character or shaped glyph rendering in `TextRenderer.cs`:
    ```csharp
    public static void DrawTextWithSpacing(SKCanvas canvas, string text, float x, float y, SKFont font, SKPaint paint, float letterSpacing, float lineSpacingMultiplier)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        float baseLineHeight = font.Spacing * (lineSpacingMultiplier <= 0 ? 1.2f : lineSpacingMultiplier);
        
        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            string line = lines[lineIdx];
            float currentY = y + (lineIdx * baseLineHeight);
            
            // Calculate total line width including letterSpacing for Left/Center/Right alignment
            float totalWidth = 0f;
            for (int i = 0; i < line.Length; i++)
                totalWidth += font.MeasureText(line[i].ToString()) + (i < line.Length - 1 ? letterSpacing : 0f);
                
            float startX = x - (totalWidth / 2f); // For Center align
            float cursorX = startX;
            
            foreach (char c in line)
            {
                string ch = c.ToString();
                canvas.DrawText(ch, cursorX, currentY, SKTextAlign.Left, font, paint);
                cursorX += font.MeasureText(ch) + letterSpacing;
            }
        }
    }
    ```
  - Bind the **Letter Spacing** (`-20px` to `+100px`) and **Line Height** (`0.5x` to `3.0x`) sliders in the Inspector directly to `InvalidateVisual()` on the Program Monitor so dragging the slider updates the canvas in real time.

---

### BUG FIX 2: Transform & Crop (`Crop Left, Right, Top, Bottom` Not Working on Video & Text)
- **Root Cause:** The Inspector sliders for `Crop Left`, `Crop Right`, `Crop Top`, and `Crop Bottom` update view model properties, but `VideoCompositor.cs` either ignores them, applies them only to video clips (ignoring Text/Graphic clips), or fails to clip the `SKCanvas` before drawing.
- **Fix (Universal Transform & Crop Pipeline for ALL Visual Clips — Video, Images, and Text):**
  - Every visual clip (`Video`, `Image`, `Text/Title`) MUST pass through the unified `RenderVisualClip()` pipeline in `VideoCompositor.cs`:
    ```csharp
    canvas.Save();

    // 1. Apply Transform (Position X/Y, Scale X/Y, Rotation around Clip Anchor Center)
    float centerX = (canvasWidth / 2f) + clip.GetEvaluatedValue(PropertyType.PositionX, localTime);
    float centerY = (canvasHeight / 2f) + clip.GetEvaluatedValue(PropertyType.PositionY, localTime);
    float scaleX = clip.GetEvaluatedValue(PropertyType.ScaleX, localTime) / 100f;
    float scaleY = clip.GetEvaluatedValue(PropertyType.ScaleY, localTime) / 100f;
    float rotation = clip.GetEvaluatedValue(PropertyType.Rotation, localTime);

    canvas.Translate(centerX, centerY);
    canvas.RotateDegrees(rotation);
    canvas.Scale(scaleX, scaleY);
    canvas.Translate(-canvasWidth / 2f, -canvasHeight / 2f);

    // 2. Apply Crop (Left, Right, Top, Bottom in percentages 0% - 100%)
    float cropL = Math.Clamp(clip.GetEvaluatedValue(PropertyType.CropLeft, localTime), 0f, 100f) / 100f;
    float cropR = Math.Clamp(clip.GetEvaluatedValue(PropertyType.CropRight, localTime), 0f, 100f) / 100f;
    float cropT = Math.Clamp(clip.GetEvaluatedValue(PropertyType.CropTop, localTime), 0f, 100f) / 100f;
    float cropB = Math.Clamp(clip.GetEvaluatedValue(PropertyType.CropBottom, localTime), 0f, 100f) / 100f;

    float clipRectLeft = canvasWidth * cropL;
    float clipRectTop = canvasHeight * cropT;
    float clipRectRight = canvasWidth * (1f - cropR);
    float clipRectBottom = canvasHeight * (1f - cropB);

    if (clipRectRight > clipRectLeft && clipRectBottom > clipRectTop)
    {
        var cropRect = new SKRect(clipRectLeft, clipRectTop, clipRectRight, clipRectBottom);
        canvas.ClipRect(cropRect, SKClipOperation.Intersect, antialias: true);
        
        // 3. Draw Video Frame OR Text Layer inside the transformed & cropped space
        DrawClipContent(canvas, clip, localTime);
    }

    canvas.Restore();
    ```
  - This ensures **Crop L, R, Top, and Bottom** work identically on both **Video clips** and **Text/Title clips**, and animate smoothly when keyframed.

---

### BUG FIX 3: Unified Undo / Redo (`Ctrl+Z` / `Ctrl+Y`) for Inspector Sliders, Color Grading & Keyframes
- **Root Cause:** Currently, `CommandManager` (Undo/Redo) only records Timeline structural actions (Split, Move, Delete). When the user is inside the **Inspector**, **Color Grading**, **Text Studio**, or **Keyframe Editor** and presses `Ctrl+Z`, it undoes the timeline cut instead of undoing the parameter/keyframe change!Furthermore, when an Inspector slider or number box has keyboard focus, it swallows the `Ctrl+Z` key event.
- **Fix 1 (Property & Keyframe Commands in Global Undo Stack):**
  - Create `PropertyChangeCommand<T>` and `KeyframeMutationCommand` implementing `IEditCommand`:
    - **Slider Drag Coalescing:** When a user begins dragging a slider (`PointerPressed`), capture `initialValue`. While dragging (`PointerMoved`), update the live preview without pushing to the Undo stack on every tick. On `PointerReleased` (or when pressing `Enter` in the number box), push a single `PropertyChangeCommand(clip, propertyName, initialValue, finalValue)` onto the global `UndoRedoManager`.
    - **Keyframe Actions:** Adding (`◆`), deleting, moving a keyframe in time, or changing a keyframe's value MUST push a `KeyframeMutationCommand` to `UndoRedoManager`.
    - **Text & Color Edits:** Changing Font, Spacing, Text Content, Color Wheels, Curves, or Toggle Checkboxes MUST push a `PropertyChangeCommand`.
- **Fix 2 (Window-Level Tunneling KeyBindings for `Ctrl+Z` / `Ctrl+Y`):**
  - Register `Ctrl+Z` (Undo) and `Ctrl+Y` / `Ctrl+Shift+Z` (Redo) at the `MainWindow` level using Avalonia Tunneling (`RoutingStrategies.Tunnel`) so that even when focus is inside the Inspector panel, Keyframe mini-timeline, or a slider, `Ctrl+Z` always triggers the unified `UndoRedoManager.Undo()`.

---

### BUG FIX 4: Standalone Draggable Transition Blocks on the Timeline (Premiere Pro Style)
- **Root Cause:** Transitions were previously limited to a dropdown inside the Inspector. Users want transitions to behave like **Adobe Premiere Pro / CapCut**—as a visible, draggable **Transition Block** on the timeline that can be placed anywhere (at clip start, clip end, across a cut between two clips, or on its own overlay range) and resized by dragging its edges.
- **Fix (Timeline Transition Block Architecture):**
  1. Introduce `TimelineTransitionItem` (or allow `TimelineClip` with `ClipKind.Transition`) that renders as a distinct **beveled block with a diagonal cross-hatch icon** directly on the timeline track (`#7C3AED` / Studio Amber `#D97706` with crisp 1px border).
  2. **Flexible Placement Anywhere on Timeline:**
     - **Between Two Clips (Cut Joint):** Blends Outgoing Clip A and Incoming Clip B over the transition block's duration.
     - **Single Clip Start / End:** Acts as a directional intro/outro transition (e.g., Fade from Black, Whip Zoom In, Slide In).
     - **Free Placement / Overlay Range:** Can be dragged from the **Transitions Tab** and dropped anywhere onto any Video Track (`V1`, `V2`) or Audio Track (`A1`, `A2`), moved left/right freely, trimmed in duration by dragging its left/right edges, and deleted or copied via Right-Click or `Delete`.

---

### BUG FIX 5: Expanded Color Grading & Scopes (Adobe Premiere Lumetri + Illustrator Vector Precision)
- **Root Cause:** Basic sliders were insufficient and previously had broken contrast math.
- **Fix (Complete Studio Color Suite & Live Vector/Waveform Scopes):**
  - Add a dedicated **Color & Scopes** tab in the Inspector containing:
    1. **Live Video Scopes (Switchable & Multi-View):**
       - **RGB Parade:** Red, Green, and Blue vertical waveform distribution side-by-side.
       - **Luma Waveform (IRE 0–100):** Broadcast luminance monitor.
       - **Vectorscope (HSL / YUV Wheel):** Circular chrominance target with skin-tone line indicator.
       - **RGB Histogram:** Smooth overlapping tonal distribution.
    2. **Basic Correction:** Temperature, Tint, Exposure, Contrast (true mid-gray `0.5` pivot), Highlights, Shadows, Whites, Blacks, Saturation, Vibrance.
    3. **Creative RGB & Luma Bezier Curves (Adobe Illustrator-style Pen/Node Curve Editor):**
       - Interactive square curve grid where users can switch between **Master Luma**, **Red**, **Green**, and **Blue** channels.
       - Click on the diagonal curve line to add control points, drag points with smooth cubic spline interpolation (`Catmull-Rom` / `Bezier`), and right-click a point to remove it.
       - Generates a 256-entry `SKColorTable` LUT in real-time and applies it via `SKColorFilter.CreateTable(alpha, redLut, greenLut, blueLut)`.
    4. **HSL Secondary / Color Mixer (8 Hue Ranges):** Hue vs. Hue, Hue vs. Saturation, and Hue vs. Luma sliders for targeted color manipulation (Red, Orange/Skin, Yellow, Green, Aqua, Blue, Purple, Magenta).
    5. **3-Way Color Wheels:** Lift (Shadows), Gamma (Midtones), Gain (Highlights) with master luminance wheel sliders beneath each wheel.

---

### BUG FIX 6: Strict Terminology Cleanup (Zero "AI" Words or Technical Geek Jargon in UI)
- **Rule:** Keep all intelligent backend tools working 100% (Local LM Studio, Ollama, Whisper, ONNX Matte, plus Cloud API Keys for ChatGPT, Gemini, Claude, Groq), **but remove ALL occurrences of the word "AI" and ugly technical jargon from the user interface.**
- **Mandatory UI Text Replacements:**
  - ❌ `Isolate Foreground Subject (RVM Matte)` -> ✅ **`Remove Background`**
  - ❌ `AI Co-Pilot / LLM Timeline Agent` -> ✅ **`Smart Edit Assistant`** or **`Auto-Edit Console`**
  - ❌ `AI Provider Settings (ONNX / GGUF / API)` -> ✅ **`Automation & Cloud Connections`**
  - ❌ `Transcribe Dialogue (Whisper.net)` -> ✅ **`Auto Captions & Transcript`**
  - ❌ `Strip Silence (RMS dB Threshold)` -> ✅ **`Cut Silent Pauses`**
  - ❌ `Dialogue Isolate (DeepFilterNet)` -> ✅ **`Clean Voice Audio`**
  - ❌ `Foley Synthesizer (AudioLDM)` -> ✅ **`Sound Generator`**
  - ❌ `TC 00:00:03:24 [1920x1080 • 24fps • NV12]` -> ✅ **`00:03.24`** (Clean, readable timecode)

---

## 2. COMPLETE FEATURE MATRIX & UI VERIFICATION

Every panel and button in **Vortex Cut Studio** must be wired to real, tested C# logic:

### 2.1 Left Panel (Media, Effects, Transitions, Text, Captions, Audio)
1. **Media Bin:** Import video/audio/images (`Ctrl+I` or Drag-and-Drop), thumbnail grid, proxy toggle, right-click -> *Add to Timeline*, *Open in Source Preview*, *Remove*.
2. **Effects Catalog:** Drag-and-drop onto any clip (Gaussian Blur, Camera Shake, Lens Vignette, Sharpen, Film Grain, Chromatic Aberration, VHS Glitch, Drop Shadow, Mirror/Flip).
3. **Transitions Catalog (Draggable Timeline Blocks):**
   - **Video:** Cross Dissolve, Dip to Black, Dip to White, Whip Zoom In/Out, Push Left/Right/Up/Down, Directional Wipe, Flash Cut, Luma Fade.
   - **Audio:** Constant Power Crossfade, Exponential Fade In, Exponential Fade Out.
4. **Titles & Text Studio:** Drag preset title blocks onto `V2`/`C1`, edit text directly on canvas or in Inspector, full Font Family selector, Font Weight, Font Size, **Letter Spacing (Working 100%)**, **Line Spacing (Working 100%)**, Text Fill Color, Outline Stroke (Color + Thickness), Drop Shadow (Color + Blur + Angle/Distance), and **Optional Background Box (Unchecked = 100% Transparent by default)**.
5. **Auto Captions & Cut Pauses:** One-click speech-to-text transcription, click word to seek, highlight text and press `Delete` to ripple-cut video, and one-click *Cut Silent Pauses*.

### 2.2 Center Panel (Interactive Canvas Monitor & Quick Toolbar)
1. **Direct Canvas Manipulation:** Clicking any Video or Text clip displays an interactive wireframe bounding box on the monitor with 8 scale handles, 1 rotation handle, and visual crop guides.
2. **Premiere-Style Timeline Toolbar (Directly Above Timeline):**
   - `Select (V)` | `Razor Cut (C)` | `Slip Trim (Y)` | `Hand Pan (H)` | `Text Tool (T)`
   - `Split Clip (Ctrl+B)` | `Delete & Close Gap (Shift+Del)` | `Freeze Frame` | `Speed/Duration` | `Add Marker (M)` | `Magnet Snap (N)`
   - Right side: `Undo (Ctrl+Z)` | `Redo (Ctrl+Y)` | `Zoom Out (-)` — `[Zoom Slider]` — `Zoom In (+)` | `Fit Timeline (Shift+Z)`

### 2.3 Right Panel (Inspector with Synchronized Keyframe Mini-Timeline)
1. **Transform & Crop (Working 100% on Video & Text):**
   - `Position X / Y`, `Scale` (Uniform or separate X/Y), `Rotation`, `Opacity`, `Blend Mode` (Normal, Screen, Multiply, Overlay, Add).
   - `Crop Left (%)`, `Crop Right (%)`, `Crop Top (%)`, `Crop Bottom (%)`, `Corner Radius`.
2. **Synchronized Keyframe Mini-Timeline:**
   - Every parameter row has `[<] [◆] [>]` keyframe controls and a `↺` Reset button.
   - Expanding the Keyframe Editor reveals a **Synchronized Mini-Timeline with a Clear Seconds/Minutes Ruler (`00:00.0s`, `00:02.0s`, `00:04.0s`)**.
   - Users can drag keyframe diamonds `◆` left/right along the time ruler, select multiple keyframes, press `Delete` to remove them, right-click a keyframe to choose interpolation (**Linear**, **Ease In**, **Ease Out**, **Smooth Bezier**, **Hold**), and press **`Ctrl+Z` / `Ctrl+Y` to undo/redo any keyframe or slider edit**.

---

## 3. STEP-BY-STEP EXECUTION PLAN FOR ANTIGRAVITY

1. **STEP 1 (Fix Undo/Redo Architecture):** Refactor `UndoRedoManager` so that every Inspector slider change (`PointerReleased` / `Enter` key), Checkbox toggle, Color Curve node edit, Text edit, and Keyframe (`◆`) addition/move/deletion pushes a reversible `IEditCommand`. Bind `Ctrl+Z` and `Ctrl+Y` with `RoutingStrategies.Tunnel` at the `MainWindow` root so Undo/Redo works seamlessly inside the Inspector and Keyframe editor.
2. **STEP 2 (Fix Text Studio & Remove Black Background):** Update `TextRenderer.cs` and `VideoCompositor.cs` so text renders on a `SKColors.Transparent` layer by default (`EnableBackground = false`). Implement character-by-character glyph advancement so **Letter Spacing** and **Line Height** sliders work accurately in real time.
3. **STEP 3 (Fix Transform & Crop L/R/Top/Bottom):** Update `VideoCompositor.cs` so `CropLeft`, `CropRight`, `CropTop`, and `CropBottom` calculate an `SKRect` clip mask in local transformed space for BOTH Video clips and Text clips.
4. **STEP 4 (Implement Standalone Timeline Transition Blocks):** Update `TimelineCanvasControl.cs` and `TimelineViewModel.cs` so Transitions can be dropped anywhere on timeline tracks as movable, resizable blocks (Premiere Pro style) and rendered smoothly by `VideoCompositor.cs` and `AudioMixerEngine.cs`.
5. **STEP 5 (Upgrade Color Grading & Scopes):** Add the interactive **RGB/Luma Bezier Curve Editor**, **HSL Secondary Sliders**, **3-Way Color Wheels**, and **Live RGB Parade / Vectorscope / Waveform** controls.
6. **STEP 6 (Terminology Cleanup & Full Build QA):** Scrub all XAML and C# UI strings to remove the word "AI" and technical jargon while keeping local (LM Studio / Ollama / Whisper / ONNX) and cloud (ChatGPT / Gemini / Claude) automation connections 100% functional. Run `dotnet build` and verify zero errors.