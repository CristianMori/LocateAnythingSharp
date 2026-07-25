// Copyright 2026 Cristian Mori
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Runtime.InteropServices;
using System.Text;

namespace Mori.LocateAnything;

/// <summary>
/// Decode strategy of the underlying model. Hybrid (parallel box decoding with
/// autoregressive fallback) is the recommended default; Slow is pure
/// autoregressive; Fast is multi-token-prediction only.
/// </summary>
public enum DecodeMode { Hybrid = 0, Slow = 1, Fast = 2 }

/// <summary>One detected object: label + bounding box in pixel coordinates (x1,y1,x2,y2).</summary>
public sealed record Detection(string Label, float X1, float Y1, float X2, float Y2)
{
    public float Width  => X2 - X1;
    public float Height => Y2 - Y1;
    public override string ToString() => $"{Label} [{X1:F1},{Y1:F1},{X2:F1},{Y2:F1}]";
}

/// <summary>
/// Managed wrapper over the native open-vocabulary detection engine. Load once
/// (the model is read from disk and uploaded to the compute device), then call
/// Locate per image. Not thread-safe: serialize calls per engine instance.
/// </summary>
public sealed class LocateEngine : IDisposable
{
    private IntPtr _ctx;

    private LocateEngine(IntPtr ctx) => _ctx = ctx;

    /// <summary>Loads a GGUF model. threads=0 picks a sensible CPU thread count.</summary>
    public static LocateEngine Load(string modelPath, int threads = 0)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("GGUF model not found", modelPath);
        IntPtr ctx = NativeMethods.la_capi_load(modelPath, threads);
        if (ctx == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to load model '{modelPath}'.");
        return new LocateEngine(ctx);
    }

    /// <summary>Compute device the engine selected: "cpu" or a GPU name like "CUDA0".</summary>
    public string DeviceName
    {
        get
        {
            ThrowIfDisposed();
            return Marshal.PtrToStringUTF8(NativeMethods.la_capi_device_name(_ctx)) ?? "cpu";
        }
    }

    /// <summary>True when inference runs on a GPU/accelerator rather than the CPU.</summary>
    public bool IsGpu => !DeviceName.Equals("cpu", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the prompt format the model was trained on from plain category names,
    /// e.g. PromptFor("person", "car").
    /// </summary>
    public static string PromptFor(params string[] categories) =>
        "Locate all the instances that matches the following description: "
        + string.Join("</c>", categories) + ".";

    /// <summary>Detects the prompted categories in an image file on disk.</summary>
    public IReadOnlyList<Detection> Locate(string imagePath, string prompt, DecodeMode mode = DecodeMode.Hybrid)
    {
        ThrowIfDisposed();
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Image not found", imagePath);
        IntPtr json = NativeMethods.la_capi_locate_path(_ctx, imagePath, prompt, (int)mode);
        return CollectDetections(json);
    }

    /// <summary>Detects the prompted categories in an encoded (PNG/JPEG) image buffer.</summary>
    public IReadOnlyList<Detection> Locate(byte[] imageBytes, string prompt, DecodeMode mode = DecodeMode.Hybrid)
    {
        ThrowIfDisposed();
        IntPtr json = NativeMethods.la_capi_locate_buffer(_ctx, imageBytes, (nuint)imageBytes.Length, prompt, (int)mode);
        return CollectDetections(json);
    }

    /// <summary>
    /// Turns the native call result into typed detections. The returned JSON is
    /// released immediately; boxes and labels are read back through the accessor
    /// functions, which avoids parsing JSON on the managed side.
    /// </summary>
    private IReadOnlyList<Detection> CollectDetections(IntPtr json)
    {
        if (json == IntPtr.Zero)
        {
            string err = Marshal.PtrToStringUTF8(NativeMethods.la_capi_last_error(_ctx)) ?? "";
            throw new InvalidOperationException($"Detection failed: {err}");
        }
        NativeMethods.la_capi_free_string(json);

        int n = NativeMethods.la_capi_get_n_detections(_ctx);
        var result = new List<Detection>(n);
        var box = new float[4];
        for (int i = 0; i < n; i++)
        {
            if (NativeMethods.la_capi_get_detection_box(_ctx, i, box) != 0) continue;
            // Two-call sizing: first call returns the needed size incl. the NUL.
            int need = NativeMethods.la_capi_get_detection_label(_ctx, i, null, 0);
            var buf = new byte[need];
            NativeMethods.la_capi_get_detection_label(_ctx, i, buf, buf.Length);
            string label = Encoding.UTF8.GetString(buf, 0, Math.Max(0, need - 1));
            result.Add(new Detection(label, box[0], box[1], box[2], box[3]));
        }
        return result;
    }

    private void ThrowIfDisposed()
    {
        if (_ctx == IntPtr.Zero) throw new ObjectDisposedException(nameof(LocateEngine));
    }

    /// <summary>Releases the native context (model weights, device buffers).</summary>
    public void Dispose()
    {
        if (_ctx != IntPtr.Zero)
        {
            NativeMethods.la_capi_free(_ctx);
            _ctx = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    ~LocateEngine() => Dispose();
}
