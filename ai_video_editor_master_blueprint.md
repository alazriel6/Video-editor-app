# Master Blueprint: AI-Powered Desktop Video Editor (C# / .NET 8)

> **Instruksi untuk AI Agent (Antigravity):**  
> Dokumen ini adalah spesifikasi teknis dan panduan eksekusi *end-to-end* untuk membangun aplikasi Desktop Video Editor berbasis AI (seperti CapCut/Descript) menggunakan **C# (.NET 8)** dan **Avalonia UI**. Ikuti arsitektur, struktur folder, daftar paket NuGet, dan tahapan fase di bawah ini secara berurutan hingga aplikasi selesai 100%.

---

## 1. Visi Produk & Fitur Utama
Aplikasi desktop pengedit video modern yang menggabungkan *Multi-Track Non-Linear Editing (NLE)* dengan *Hybrid AI Engine* (mendukung **Model Lokal/Offline** dan **Cloud API Key / BYOK**).

### Fitur Inti (Core Video Editing)
1. **Media Library & Bin:** Import video (`.mp4`, `.mov`, `.mkv`), audio (`.mp3`, `.wav`), dan gambar.
2. **Multi-Track Timeline:** Track Video, Track Overlay/PIP, Track Teks/Subtitle, dan Track Audio/SFX. Mendukung *Cut/Split*, *Trim*, *Drag-and-Drop*, *Snapping*, dan *Zoom Timeline*.
3. **Real-Time Preview Canvas:** Pemutaran 60fps menggunakan *zero-copy memory buffer* dari FFmpeg ke SkiaSharp/GPU, lengkap dengan *scrubbing* mulus.
4. **Efek Visual & Transisi:** Transisi *Fade, Dissolve, Wipe, Zoom, Glitch* menggunakan *Custom Skia/GLSL Shaders* dan filter warna (*LUTs / Color Grading*).
5. **Audio Mixer & SFX:** Pengaturan volume per klip, *fade in/out*, tampilan *waveform*, dan *auto-ducking* (mengecilkan musik otomatis saat ada vokal).
6. **Hardware-Accelerated Export:** Render ke MP4/MOV menggunakan encoder GPU (`h264_nvenc`, `hevc_amf`, `h264_qsv`, atau `libx264`).

### Fitur AI (Hybrid: Local Model & Cloud API)
1. **AI Auto-Captions & Text-Based Editing:** Transkripsi suara ke subtitle lengkap dengan *word-level timestamps*. Hapus teks di transkrip = otomatis memotong bagian video tersebut.
2. **AI Silence & Filler Word Removal:** Deteksi dan potong bagian hening (*dead air*) secara otomatis di *timeline*.
3. **AI Background Removal (Tanpa Green Screen):** Memisahkan objek/manusia dari latar belakang secara *frame-by-frame*.
4. **AI Voiceover (TTS) & Sound Effect Generator:** Buat narasi suara dan *sound effect* dari teks.
5. **AI Co-Pilot (Timeline Agent):** *Chat prompt* di mana pengguna mengetik instruksi (misal: *"Potong bagian jeda, tambahkan transisi zoom di tiap pergantian adegan, dan beri subtitle kuning"*), lalu AI memodifikasi struktur JSON Timeline secara langsung.

---

## 2. Tech Stack & Daftar Paket NuGet (.NET 8)

Gunakan paket NuGet resmi berikut di dalam solusi C#:

* **UI & Cross-Platform Framework:**
  * `Avalonia` (`11.1.x+`)
  * `Avalonia.Desktop`
  * `Avalonia.Themes.Fluent`
  * `CommunityToolkit.Mvvm` (Arsitektur MVVM)
* **Video & Audio Processing Engine:**
  * `FFMediaToolkit` (Decoding & Encoding frame video)
  * `FFmpeg.AutoGen` (Low-level binding C API FFmpeg untuk filter & hardware acceleration)
  * `SkiaSharp` & `Avalonia.Skia` (Rendering frame video, teks, dan shader transisi)
  * `NAudio` (Audio mixing, waveform rendering, real-time audio playback)
* **Hybrid AI Engine (Local & Cloud):**
  * `Whisper.net` & `Whisper.net.Runtime.Cuda` (Transkripsi suara lokal & deteksi timestamp)
  * `LLamaSharp` & `LLamaSharp.Backend.Cuda12` (Eksekusi LLM lokal format `.gguf` untuk AI Timeline Agent)
  * `Microsoft.ML.OnnxRuntime.Gpu` / `Microsoft.ML.OnnxRuntime.DirectML` (Menjalankan model *RobustVideoMatting* untuk hapus background dan model Audio/SFX lokal)
  * `Microsoft.SemanticKernel` (Abstraksi untuk Cloud API Key: OpenAI, Gemini, Claude, Groq, ElevenLabs)

---

## 3. Arsitektur Sistem & Alur Kerja (Flow)

### A. Diagram Alur Data Utama (High-Level Flow)

```
[Input Media (MP4/WAV)] 
       │
       ▼
┌─────────────────────────────────────────────────────────────┐
│                PROJECT STATE (Timeline JSON)                │
│  Menyimpan Track, Klip, Waktu In/Out, Efek, Transisi, Teks  │
└──────┬───────────────────────────────────────────────┬──────┘
       │ (Dibaca real-time)                            │ (Dimodifikasi oleh)
       ▼                                               ▼
┌──────────────────────────────────┐    ┌───────────────────────────────────┐
│     MEDIA & RENDER PIPELINE      │    │         HYBRID AI ENGINE          │
│ 1. FFMediaToolkit (Decode Frame) │    │ Pilih Provider via `IAiService`:  │
│ 2. ONNX (AI Masking jika aktif)  │    │ ├─ LOKAL: Whisper.net, LLamaSharp │
│ 3. SkiaSharp (Shader/Transisi)   │    │ │         ONNX Runtime (RVM/SFX)  │
│ 4. NAudio (Mix Multi-Track Audio)│    │ └─ CLOUD: OpenAI / Gemini /       │
│ 5. Tampil ke Preview / Export MP4│    │           ElevenLabs (API Key)    │
└──────────────────────────────────┘    └───────────────────────────────────┘
```

### B. Alur Kerja 4 Pipeline Utama

#### 1. Alur Preview Video Real-Time (Zero-Copy Rendering)
1. Pengguna menekan tombol **Play** atau menggeser **Playhead** ke waktu `T` (misal `00:12.40`).
2. `TimelineController` mencari klip video, overlay, teks, dan transisi yang aktif pada waktu `T`.
3. `VideoDecoderService` (`FFMediaToolkit`) mengambil *byte frame* dari file video sumber dan menyalinnya langsung ke memori `WriteableBitmap` / `SKBitmap` menggunakan `Span<byte>`.
4. Jika klip memiliki efek **AI Remove Background**, *frame* dilewatkan ke `OnnxSegmentationService` (*RobustVideoMatting*) untuk menghasilkan *Alpha Mask*.
5. `ShaderTransitionRenderer` (`SkiaSharp` `SKRuntimeEffect`) menggabungkan *Frame Klip A* dan *Frame Klip B* jika `T` berada di area transisi, lalu menggambar *Overlay Teks/Subtitle*.
6. Gambar akhir ditampilkan di komponen `VideoPreviewCanvas` di UI Avalonia.

#### 2. Alur AI Auto-Cut & Text-Based Editing
1. Pengguna memilih klip di *timeline* -> klik **"Transcribe / Auto-Cut"**.
2. `AudioExtractor` mengekstrak audio dari klip menjadi format `16kHz Mono PCM WAV`.
3. `TranscriptionService` mengecek pengaturan pengguna:
   * **Jika Mode Lokal:** Memanggil `Whisper.net` (model `ggml-base.bin` atau `ggml-large-v3-turbo.bin`).
   * **Jika Mode API Key:** Mengirim audio ke API OpenAI Whisper / Groq Whisper.
4. Hasil berupa daftar segmen kata lengkap dengan `StartTime`, `EndTime`, dan `Confidence`.
5. Jika terdapat jeda antar-kata $> 0.8$ detik (atau nilai *threshold* pengguna), `TimelineService` otomatis melakukan operasi `SplitClip()` dan menghapus segmen kosong tersebut (*Ripple Delete*).

#### 3. Alur AI Co-Pilot (Mengedit Video Lewat Prompt Chat)
1. Pengguna mengetik di panel AI Chat: *"Potong bagian yang membosankan di awal, tambahkan transisi glitch di setiap potongan, dan beri SFX whoosh"*.
2. `AiAgentService` mengambil **State JSON Timeline** saat ini + **Transkrip Video**.
3. Prompt dan skema JSON dikirim ke `LLamaSharp` (model lokal `Qwen2.5-7B-Instruct.gguf`) atau ke Cloud API (OpenAI/Gemini) menggunakan fitur *Structured Output / Function Calling*.
4. AI memanggil fungsi internal C# (`SplitClip`, `AddTransition`, `InsertSfxClip`, `AddSubtitleTrack`).
5. `TimelineViewModel` memperbarui UI secara instan, dan tindakan dicatat ke dalam `UndoRedoStack` sehingga pengguna tetap bisa menekan `Ctrl+Z`.

#### 4. Alur Final Export (Render ke MP4)
1. Pengguna menekan tombol **Export** -> memilih resolusi (1080p/4K), FPS (30/60), dan Encoder (GPU/CPU).
2. `ExportEngine` mengiterasi waktu dari `0` hingga `TotalDuration` dengan kenaikan $1 / \text{FPS}$.
3. Setiap *frame* dirender secara *off-screen* menggunakan *pipeline* `SkiaSharp` yang sama persis dengan *Preview Canvas* (menjamin hasil *export* 100% identik dengan *preview*).
4. *Frame* mentah di-*encode* menggunakan `FFMediaToolkit` / `FFmpeg.AutoGen`, digabungkan (*muxed*) dengan *master audio stream* dari `NAudio`, lalu disimpan menjadi `.mp4`.

---

## 4. Struktur Solusi & Folder Project (C#)

Buat struktur solusi multi-proyek (`.sln`) berikut agar kode modular dan bersih:

```text
AiVideoEditor.sln
│
├── src/
│   ├── AiVideoEditor.Core/                  # Domain Models, Interfaces, JSON Schema, Undo/Redo
│   │   ├── Models/
│   │   │   ├── ProjectDocument.cs           # Root JSON model untuk proyek
│   │   │   ├── TimelineTrack.cs             # Track Video, Audio, Text, Effect
│   │   │   ├── TimelineClip.cs              # Posisi di timeline, Source In/Out, Speed
│   │   │   ├── TransitionModel.cs           # Tipe transisi, durasi, shader params
│   │   │   └── TranscriptSegment.cs         # Data kata & timestamp dari Whisper
│   │   ├── Interfaces/
│   │   │   ├── IAiTranscriptionService.cs
│   │   │   ├── IAiAgentService.cs
│   │   │   ├── IAiSfxService.cs
│   │   │   ├── IVideoSegmentationService.cs
│   │   │   └── IVideoRenderEngine.cs
│   │   └── Commands/                        # Command Pattern untuk Undo/Redo (Split, Move, Trim)
│   │
│   ├── AiVideoEditor.MediaEngine/           # Pemrosesan FFmpeg, Skia Shaders, & NAudio
│   │   ├── Decoding/
│   │   │   ├── FfmpegBootstrapper.cs        # Load binary FFmpeg (avcodec, avformat, dll)
│   │   │   └── VideoFrameReader.cs          # Caching & Frame Extraction
│   │   ├── Rendering/
│   │   │   ├── CompositorEngine.cs          # Menggabungkan multi-track frame + teks
│   │   │   └── Shaders/                     # File SKSL (.sksl) untuk transisi Zoom, Glitch, Fade
│   │   ├── Audio/
│   │   │   ├── MultiTrackAudioPlayer.cs     # Playback & Mixing dengan NAudio
│   │   │   └── WaveformGenerator.cs         # Membuat data visual gelombang suara
│   │   └── Exporting/
│   │       └── VideoExporter.cs             # Render akhir ke MP4 dengan Hardware Acceleration
│   │
│   ├── AiVideoEditor.AiEngine/              # Implementasi Hybrid AI (Local & Cloud API)
│   │   ├── Configuration/
│   │   │   ├── AiProviderConfig.cs          # Pilihan: Local vs Cloud + Penyimpanan API Key
│   │   │   └── LocalModelDownloader.cs      # Download otomatis model GGUF/ONNX dari HuggingFace
│   │   ├── LocalProviders/
│   │   │   ├── LocalWhisperService.cs       # Implementasi Whisper.net
│   │   │   ├── LocalLlamaAgentService.cs    # Implementasi LLamaSharp (JSON Timeline Agent)
│   │   │   ├── OnnxRvmMattingService.cs     # Hapus background dengan RobustVideoMatting ONNX
│   │   │   └── LocalTtsAndSfxService.cs     # Kokoro TTS ONNX & AudioLDM/SFX Generator
│   │   └── CloudProviders/
│   │       ├── CloudTranscriptionService.cs # OpenAI / Groq Whisper API
│   │       ├── CloudLlmAgentService.cs      # OpenAI / Gemini / Claude via Semantic Kernel
│   │       └── ElevenLabsAudioService.cs    # Cloud TTS & Sound Effect Generation API
│   │
│   └── AiVideoEditor.App/                   # Aplikasi Desktop Utama (Avalonia UI - MVVM)
│       ├── Assets/                          # Icons, Default LUTs, Built-in Sound Effects
│       ├── Controls/
│       │   ├── VideoPreviewCanvas.cs        # Custom Avalonia Control untuk render 60fps
│       │   └── TimelineControl/             # Custom Control untuk Track, Playhead, & Drag-Drop Klip
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs
│       │   ├── TimelineViewModel.cs
│       │   ├── MediaBinViewModel.cs
│       │   ├── InspectorViewModel.cs        # Panel kanan untuk atur skala, posisi, warna, transisi
│       │   ├── AiCopilotViewModel.cs        # Panel chat AI & tombol aksi cepat AI
│       │   └── SettingsViewModel.cs         # Pengaturan API Key & Manager Model Lokal
│       └── Views/
│           ├── MainWindow.axaml
│           ├── TimelineView.axaml
│           ├── AiCopilotView.axaml
│           └── SettingsModal.axaml
```

---

## 5. Kontrak Data Inti (JSON Timeline Schema)

Seluruh fitur manual maupun AI bekerja dengan memanipulasi model data ini. Pastikan kode di `AiVideoEditor.Core` mengimplementasikan struktur ini:

```csharp
namespace AiVideoEditor.Core.Models;

public class ProjectDocument
{
    public string ProjectName { get; set; } = "Untitled Project";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double Fps { get; set; } = 30.0;
    public List<MediaAsset> MediaPool { get; set; } = new();
    public List<TimelineTrack> Tracks { get; set; } = new();
}

public enum TrackType { Video, Audio, Text, Effect }

public class TimelineTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public TrackType Type { get; set; }
    public bool IsMuted { get; set; }
    public bool IsLocked { get; set; }
    public List<TimelineClip> Clips { get; set; } = new();
}

public class TimelineClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MediaAssetId { get; set; }
    public string Label { get; set; } = string.Empty;
    
    // Posisi pada timeline (detik)
    public double TimelineStart { get; set; }
    public double Duration { get; set; }
    
    // Titik potong pada video asli (detik)
    public double SourceIn { get; set; }
    public double SourceOut { get; set; }
    
    // Transformasi Visual & Audio
    public float Opacity { get; set; } = 1.0f;
    public float Volume { get; set; } = 1.0f;
    public float Scale { get; set; } = 1.0f;
    public float PositionX { get; set; } = 0f;
    public float PositionY { get; set; } = 0f;
    
    // Fitur AI & Efek per Klip
    public bool RemoveBackgroundAi { get; set; } = false;
    public TransitionSpec? InTransition { get; set; }
    public TransitionSpec? OutTransition { get; set; }
    public TextOverlaySpec? TextData { get; set; }
}

public class TransitionSpec
{
    public string Type { get; set; } = "CrossFade"; // CrossFade, ZoomIn, Glitch, WipeLeft
    public double Duration { get; set; } = 0.5;
}
```

---

## 6. Rencana Eksekusi Bertahap untuk AI Agent (Step-by-Step)

Eksekusi pembangunan proyek ini dalam **6 Fase Berurutan**. Jangan melompat ke fase berikutnya sebelum fase saat ini berhasil di-*build* (`dotnet build` tanpa error).

### FASE 1: Setup Solution, Core Models, & Kerangka UI Avalonia
* **Tugas 1.1:** Inisialisasi `AiVideoEditor.sln` beserta 4 proyek (`Core`, `MediaEngine`, `AiEngine`, `App`) dengan target `.NET 8.0`.
* **Tugas 1.2:** Buat semua model data di `AiVideoEditor.Core` (`ProjectDocument`, `TimelineTrack`, `TimelineClip`, `TransitionSpec`, sistem serialisasi JSON, dan `UndoRedoManager`).
* **Tugas 1.3:** Desain `MainWindow.axaml` di `AiVideoEditor.App` dengan tema gelap modern ala CapCut menggunakan *Grid Layout* 4 Panel:
  * **Kiri Atas:** Panel *Media Pool / AI Tools / Transitions / SFX*.
  * **Tengah Atas:** *Video Preview Canvas* + kontrol *Play, Pause, Timecode*.
  * **Kanan Atas:** *Inspector Panel* (Transformasi, Audio, Warna) & *AI Co-Pilot Chat*.
  * **Bawah (Lebar Penuh):** *Multi-Track Timeline Editor* dengan *Toolbar* (Split `Ctrl+B`, Delete, Auto-Caption, Remove Silence).

### FASE 2: Media Engine (FFmpeg Decoding, Skia Preview, & NAudio)
* **Tugas 2.1:** Buat `FfmpegBootstrapper` untuk memuat binari FFmpeg secara otomatis sesuai OS.
* **Tugas 2.2:** Bangun `VideoFrameReader` menggunakan `FFMediaToolkit` dengan *LRU Frame Cache* agar *scrubbing* *timeline* berjalan mulus tanpa *stutter*.
* **Tugas 2.3:** Bangun `CompositorEngine` menggunakan `SkiaSharp` yang membaca `ProjectDocument` pada detik `currentTime`, menggabungkan *layer* video, merender teks/subtitle, dan menggambar hasilnya ke `VideoPreviewCanvas`.
* **Tugas 2.4:** Bangun `MultiTrackAudioPlayer` dengan `NAudio` (`MixingSampleProvider`) yang menyinkronkan waktu putar video dengan semua klip audio dan *sound effect* di *timeline*.

### FASE 3: Interaktivitas Timeline, Efek, & Shaders Transisi
* **Tugas 3.1:** Implementasikan interaksi *mouse* pada `TimelineControl`:
  * Geser *Playhead* (*scrubbing*).
  * *Drag & Drop* media dari *Media Pool* ke *Track*.
  * Geser posisi klip, *trim* ujung kiri/kanan klip, dan potong klip (*Split at Playhead*).
  * Gambar *audio waveform* di atas klip menggunakan hasil kalkulasi `WaveformGenerator`.
* **Tugas 3.2:** Buat *Shader Transisi* menggunakan `SKRuntimeEffect` (Skia Shading Language / SKSL) untuk transisi:
  * `CrossFade`, `ZoomBlur`, `Glitch`, dan `DirectionalWipe`.
* **Tugas 3.3:** Hubungkan *Inspector Panel* agar perubahan *Scale, Position, Opacity, Volume*, dan *Transition Duration* langsung memperbarui *Preview Canvas* secara *real-time*.

### FASE 4: Hybrid AI Architecture & Model Manager (Local vs API Key)
* **Tugas 4.1:** Buat `AiProviderConfig` dan UI `SettingsModal.axaml` yang memungkinkan pengguna memilih mode per fitur:
  * **Mode Cloud:** Input API Key (OpenAI, Gemini, Groq, ElevenLabs) yang disimpan secara terenkripsi.
  * **Mode Lokal:** UI *Model Manager* dengan tombol **"Download Model"** lengkap dengan *progress bar*.
* **Tugas 4.2:** Buat `LocalModelDownloader` untuk mengunduh file model langsung dari HuggingFace ke folder `%AppData%/AiVideoEditor/models/`:
  * Whisper: `ggml-base.bin` / `ggml-large-v3-turbo.bin`
  * LLM Agent: `qwen2.5-7b-instruct-q4_k_m.gguf`
  * Background Removal: `rvm_mobilenetv3_fp32.onnx`
  * TTS Lokal: `kokoro-v1.0.onnx`

### FASE 5: Implementasi Fitur AI (Whisper, RVM, SFX, & Co-Pilot Agent)
* **Tugas 5.1 (Auto-Caption & Silence Cut):** Implementasikan `LocalWhisperService` (`Whisper.net`) dan `CloudTranscriptionService`. Buat tombol **"Generate Auto-Captions"** yang otomatis mengisi *Text Track* dan tombol **"Remove Silence"** yang otomatis memotong bagian video tanpa suara.
* **Tugas 5.2 (AI Background Removal):** Implementasikan `OnnxRvmMattingService` menggunakan `Microsoft.ML.OnnxRuntime`. Saat toggle *"Remove Background"* dinyalakan pada klip di *Inspector*, *frame* klip tersebut diproses melalui model RVM ONNX sebelum digambar ke *Canvas*.
* **Tugas 5.3 (AI Voiceover & SFX):** Implementasikan pembuat suara narasi dan *sound effect* (via ElevenLabs API / OpenAI Audio atau model ONNX lokal) yang otomatis memasukkan file `.wav` hasil generate ke dalam *Audio Track*.
* **Tugas 5.4 (AI Timeline Co-Pilot):** Implementasikan `LocalLlamaAgentService` (`LLamaSharp`) dan `CloudLlmAgentService` (`SemanticKernel`). Berikan daftar *Tools/Plugin* C# kepada LLM:
  * `CutClipAt(Guid clipId, double timeInSeconds)`
  * `DeleteTimeRange(double start, double end)`
  * `ApplyTransitionToAllClips(string transitionType, double duration)`
  * `AddTextOverlay(string text, double start, double duration, string style)`
  * `AddSoundEffect(string sfxPrompt, double timelinePosition)`

### FASE 6: Final Video Exporter, Optimasi, & Packaging
* **Tugas 6.1:** Bangun `VideoExporter` yang menggabungkan output `CompositorEngine` (*frame-by-frame*) dan `NAudio` (*audio mixdown*) ke dalam encoder `FFMediaToolkit` dengan *Progress Bar* dan estimasi waktu selesai.
* **Tugas 6.2:** Tambahkan fitur *Save/Load Project* (`.aive` JSON file) dan *Auto-Save* setiap 2 menit.
* **Tugas 6.3:** Pastikan manajemen memori (`IDisposable` pada `SKBitmap`, `MediaFile`, dan `OrtValue` ONNX) bebas dari *memory leak*.

---

## 7. Checklist Definisi Selesai (Definition of Done - 100%)
- [ ] Aplikasi dapat dikompilasi dan dijalankan tanpa error (`dotnet run --project src/AiVideoEditor.App`).
- [ ] Pengguna dapat mengimpor video, memutarnya di *preview canvas*, memotong klip di *timeline*, dan menambahkan transisi maupun efek suara.
- [ ] Pengguna dapat membuka menu **AI Settings**, memasukkan API Key **ATAU** mengunduh model AI lokal.
- [ ] Fitur **Auto-Subtitle**, **Auto-Cut Silence**, **AI Background Removal**, dan **AI Chat Co-Pilot** berfungsi memodifikasi *timeline* secara nyata.
- [ ] Pengguna dapat melakukan **Export** proyek menjadi file `.mp4` utuh dengan semua efek, subtitle, dan audio tergabung sempurna.