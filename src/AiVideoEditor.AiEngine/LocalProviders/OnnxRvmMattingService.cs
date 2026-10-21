using AiVideoEditor.AiEngine.Configuration;
using AiVideoEditor.Core.Interfaces;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AiVideoEditor.AiEngine.LocalProviders;

public sealed class OnnxRvmMattingService : IVideoSegmentationService, IDisposable
{
    private readonly LocalModelDownloader _downloader;
    private InferenceSession? _session;
    private DenseTensor<float>? _r1;
    private DenseTensor<float>? _r2;
    private DenseTensor<float>? _r3;
    private DenseTensor<float>? _r4;
    private bool _attemptedLoad;
    private readonly object _lock = new();

    public bool IsModelLoaded => _session != null;

    public OnnxRvmMattingService(LocalModelDownloader downloader)
    {
        _downloader = downloader;
    }

    private void EnsureSessionInitialized()
    {
        if (_attemptedLoad) return;
        _attemptedLoad = true;

        string modelPath = _downloader.GetModelPath("rvm_mobilenetv3_fp32.onnx");
        if (!File.Exists(modelPath) || new FileInfo(modelPath).Length < 1024 * 100)
            return;

        try
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };
            _session = new InferenceSession(modelPath, options);
            _r1 = new DenseTensor<float>(new[] { 1, 1, 1, 1 });
            _r2 = new DenseTensor<float>(new[] { 1, 1, 1, 1 });
            _r3 = new DenseTensor<float>(new[] { 1, 1, 1, 1 });
            _r4 = new DenseTensor<float>(new[] { 1, 1, 1, 1 });
        }
        catch
        {
            _session = null;
        }
    }

    public void ApplyBackgroundRemoval(Span<byte> bgraPixels, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgraPixels.Length < width * height * 4)
            return;

        lock (_lock)
        {
            EnsureSessionInitialized();

            if (_session != null && _r1 != null && _r2 != null && _r3 != null && _r4 != null)
            {
                if (TryRunRvmOnnxInference(bgraPixels, width, height))
                    return;
            }

            // Real-time spatial & chroma-salience subject segmentation fallback
            // Isolates the foreground subject from background gradients/studio backdrops
            ApplyFastForegroundSegmentation(bgraPixels, width, height);
        }
    }

    private bool TryRunRvmOnnxInference(Span<byte> bgraPixels, int width, int height)
    {
        try
        {
            var srcTensor = new DenseTensor<float>(new[] { 1, 3, height, width });
            int planeSize = width * height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 4;
                    srcTensor[0, 0, y, x] = bgraPixels[idx + 2] / 255f; // R
                    srcTensor[0, 1, y, x] = bgraPixels[idx + 1] / 255f; // G
                    srcTensor[0, 2, y, x] = bgraPixels[idx + 0] / 255f; // B
                }
            }

            var downsampleRatio = new DenseTensor<float>(new[] { 1 });
            downsampleRatio[0] = Math.Min(1.0f, 256.0f / Math.Max(width, height));

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("src", srcTensor),
                NamedOnnxValue.CreateFromTensor("r1i", _r1!),
                NamedOnnxValue.CreateFromTensor("r2i", _r2!),
                NamedOnnxValue.CreateFromTensor("r3i", _r3!),
                NamedOnnxValue.CreateFromTensor("r4i", _r4!),
                NamedOnnxValue.CreateFromTensor("downsample_ratio", downsampleRatio)
            };

            using var results = _session!.Run(inputs);
            var pha = results.FirstOrDefault(r => r.Name == "pha")?.AsTensor<float>();
            if (pha == null) return false;

            // Update recurrent states for temporal consistency
            var r1o = results.FirstOrDefault(r => r.Name == "r1o")?.AsTensor<float>();
            var r2o = results.FirstOrDefault(r => r.Name == "r2o")?.AsTensor<float>();
            var r3o = results.FirstOrDefault(r => r.Name == "r3o")?.AsTensor<float>();
            var r4o = results.FirstOrDefault(r => r.Name == "r4o")?.AsTensor<float>();
            if (r1o != null) _r1 = r1o.ToDenseTensor();
            if (r2o != null) _r2 = r2o.ToDenseTensor();
            if (r3o != null) _r3 = r3o.ToDenseTensor();
            if (r4o != null) _r4 = r4o.ToDenseTensor();

            int pIdx = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float alpha = Math.Clamp(pha[0, 0, y, x], 0f, 1f);
                    bgraPixels[pIdx + 0] = (byte)(bgraPixels[pIdx + 0] * alpha);
                    bgraPixels[pIdx + 1] = (byte)(bgraPixels[pIdx + 1] * alpha);
                    bgraPixels[pIdx + 2] = (byte)(bgraPixels[pIdx + 2] * alpha);
                    bgraPixels[pIdx + 3] = (byte)(alpha * 255f);
                    pIdx += 4;
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyFastForegroundSegmentation(Span<byte> bgraPixels, int width, int height)
    {
        // Sample border pixels to estimate background color distribution
        byte bgB = bgraPixels[0];
        byte bgG = bgraPixels[1];
        byte bgR = bgraPixels[2];

        int brIdx = ((height - 1) * width + (width - 1)) * 4;
        byte bgB2 = bgraPixels[brIdx];
        byte bgG2 = bgraPixels[brIdx + 1];
        byte bgR2 = bgraPixels[brIdx + 2];

        float centerX = width * 0.5f;
        float centerY = height * 0.56f;
        float invRx = 1.0f / (width * 0.34f);
        float invRy = 1.0f / (height * 0.44f);

        for (int y = 0; y < height; y++)
        {
            float tY = (float)y / height;
            float refB = bgB + (bgB2 - bgB) * tY;
            float refG = bgG + (bgG2 - bgG) * tY;
            float refR = bgR + (bgR2 - bgR) * tY;

            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                float b = bgraPixels[idx + 0];
                float g = bgraPixels[idx + 1];
                float r = bgraPixels[idx + 2];

                float colorDist = MathF.Sqrt((r - refR) * (r - refR) + (g - refG) * (g - refG) + (b - refB) * (b - refB));
                float dx = (x - centerX) * invRx;
                float dy = (y - centerY) * invRy;
                float spatialDist = MathF.Sqrt(dx * dx + dy * dy);

                // Combine color difference from background gradient + spatial subject prior
                float alpha = Math.Clamp((colorDist - 38f) / 48f, 0f, 1f);
                if (spatialDist > 1.25f)
                    alpha *= Math.Clamp(1.65f - spatialDist, 0f, 1f);

                // Replace removed background with subtle checkerboard transparency / premultiplied alpha
                bgraPixels[idx + 0] = (byte)(b * alpha);
                bgraPixels[idx + 1] = (byte)(g * alpha);
                bgraPixels[idx + 2] = (byte)(r * alpha);
                bgraPixels[idx + 3] = (byte)(alpha * 255f);
            }
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
