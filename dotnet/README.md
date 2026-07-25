# LocateAnythingSharp

.NET bindings for NVIDIA's LocateAnything-3B — text-prompted, open-vocabulary
object detection that runs fully local, from single images to live camera
streams.

Copyright © 2026 Cristian Mori. Licensed under the [Apache License 2.0](LICENSE).

Give the engine an image and a text prompt ("cat", "remote control", any
description) and it returns labeled bounding boxes. Inference runs fully
locally through a native engine DLL, on an NVIDIA GPU when one is available
(RTX 20 series and newer) with automatic fallback to CPU. The core library and
NuGet package id is `Mori.LocateAnythingSharp`; the C# namespace is
`Mori.LocateAnything`.

## Projects

| Project | Purpose |
| --- | --- |
| `Mori.LocateAnythingSharp` | Class library: P/Invoke bindings (`NativeMethods`), the safe `LocateEngine` wrapper, `GpuSupport` first-run GPU/driver checks, and `ModelLocator`. |
| `LocateAnything.Demo` | Console app: loads a model once, detects in a loop, reports per-image latency. |
| `LocateAnything.Demo.Image` | WinForms app: open a local image, type categories, see labeled boxes drawn over it. |
| `LocateAnything.Demo.Camera` | WinForms app: pick a camera, watch the live stream, and continuously detect on the newest frame with a box overlay (video stays smooth; boxes update ~1/s on GPU). |

All demos find the model via the `LOCATEANYTHING_MODEL` environment variable or
a `models` folder next to (or above) the executable.

## Quick start

From the repository root, `setup.bat` builds everything (native engine, model
download, .NET solution, packaging). Then:

```powershell
dotnet run --project LocateAnything.Demo --no-build -- path\to\image.jpg "person,car" 3
```

Minimal usage from code:

```csharp
using Mori.LocateAnything;

using var engine = LocateEngine.Load("locate-anything-q8_0.gguf");
Console.WriteLine($"device: {engine.DeviceName}");           // "CUDA0" or "cpu"
var boxes = engine.Locate("street.jpg", LocateEngine.PromptFor("person", "car"));
foreach (var d in boxes) Console.WriteLine(d);               // person [x1,y1,x2,y2]
```

## GPU behavior

The native engine embeds GPU kernels for NVIDIA RTX 20/30/40/50 series (plus
PTX for future architectures) and picks the best available device at runtime:

- NVIDIA GPU + driver present → runs on GPU automatically.
- No NVIDIA GPU → runs on CPU.
- NVIDIA GPU present but no driver → `GpuSupport.EnsureNvidiaDriver()` prompts
  the user to open the NVIDIA driver download page, and the app continues on CPU.
- `LA_DEVICE=cpu` environment variable forces CPU.

## Third-party components

This repository's C# code is Apache 2.0. It links against and redistributes
components under their own licenses, which are NOT changed by this project:

- **locate-anything.cpp** (native engine): MIT license, © its respective authors.
- **NVIDIA CUDA runtime DLLs** (cudart/cublas): NVIDIA redistributable license.
- **Model weights** (`nvidia/LocateAnything-3B` GGUF conversions): NVIDIA's
  model license — **non-commercial use only** at the time of writing. Review it
  before shipping anything commercial.
