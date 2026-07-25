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

namespace Mori.LocateAnything;

/// <summary>
/// Raw P/Invoke surface over the native engine (locate_anything.dll, see la_capi.h).
/// All strings cross the boundary as UTF-8; the native side owns every returned
/// pointer except the locate_* JSON results, which must be released with
/// <see cref="la_capi_free_string"/>.
/// </summary>
internal static partial class NativeMethods
{
    // Resolved from the application directory (or PATH) by the .NET loader.
    private const string Lib = "locate_anything";

    /// <summary>ABI version of the native C API; bump-checked by callers on load.</summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern int la_capi_abi_version();

    /// <summary>Loads a GGUF model; returns an opaque context or NULL on failure.</summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern IntPtr la_capi_load(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string ggufPath, int nThreads);

    /// <summary>Frees a context created by <see cref="la_capi_load"/>; safe on NULL.</summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern void la_capi_free(IntPtr ctx);

    /// <summary>Runs detection on an image file; returns malloc'd JSON or NULL on error.</summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern IntPtr la_capi_locate_path(
        IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string imagePath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string prompt,
        int mode);

    /// <summary>Runs detection on an in-memory encoded image (PNG/JPEG bytes).</summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern IntPtr la_capi_locate_buffer(
        IntPtr ctx,
        [In] byte[] bytes,
        nuint len,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string prompt,
        int mode);

    /// <summary>Number of detections produced by the last locate_* call.</summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern int la_capi_get_n_detections(IntPtr ctx);

    /// <summary>Copies box i as [x1,y1,x2,y2] pixels into outXyxy; 0 ok, -1 bad index.</summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern int la_capi_get_detection_box(IntPtr ctx, int i, [Out] float[] outXyxy);

    /// <summary>
    /// Copies label i into buf (UTF-8, NUL-terminated). Returns the required buffer
    /// size including the NUL, so callers size with a first NULL-buffer call.
    /// </summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern int la_capi_get_detection_label(IntPtr ctx, int i, byte[]? buf, int bufSize);

    /// <summary>Releases a JSON string returned by a locate_* call.</summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern void la_capi_free_string(IntPtr s);

    /// <summary>Last error message for this context ("" when none); owned by the context.</summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern IntPtr la_capi_last_error(IntPtr ctx);

    /// <summary>Compute device the engine selected: "cpu" or a GPU name such as "CUDA0".</summary>
    [DllImport(Lib, ExactSpelling = true)]
    internal static extern IntPtr la_capi_device_name(IntPtr ctx);
}
