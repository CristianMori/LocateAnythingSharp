# LocateAnythingSharp

[![NuGet](https://img.shields.io/nuget/v/Mori.LocateAnythingSharp)](https://www.nuget.org/packages/Mori.LocateAnythingSharp)
[![Downloads](https://img.shields.io/nuget/dt/Mori.LocateAnythingSharp)](https://www.nuget.org/packages/Mori.LocateAnythingSharp)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

**.NET bindings for NVIDIA's LocateAnything-3B — text-prompted, open-vocabulary
object detection that runs fully local, from single images to live camera
streams.**

Describe what you want to find in plain language ("cat", "remote control",
"the red button in the corner") and get labeled bounding boxes back. No cloud,
no fixed class list: inference runs on your machine through a native engine,
on NVIDIA GPUs (RTX 20 series and newer) with automatic CPU fallback.

The package id is
[`Mori.LocateAnythingSharp`](https://www.nuget.org/packages/Mori.LocateAnythingSharp).
The C# namespace is `Mori.LocateAnything`.

| Image demo |
| --- |
| ![Image demo detecting cats and remote controls](docs/image-demo.png) |

## Highlights

- **Open vocabulary** - detect anything you can describe; categories are free
  text, not a trained class list.
- **Fully local** - a single native DLL runs the model; no Python runtime, no
  network calls, images never leave the machine.
- **GPU with graceful fallback** - the CUDA engine embeds kernels for RTX
  20/30/40/50 series (plus PTX for future GPUs) in one DLL. At runtime it uses
  the GPU when a usable NVIDIA GPU and driver are present, and CPU otherwise.
  `GpuSupport.EnsureNvidiaDriver()` notices a GPU with no driver and, on an
  interactive console, offers to open the NVIDIA driver download page. The
  console demo calls it at startup.
- **Simple C# API** - load once, detect per image, get typed results.
- **Live video pattern included** - the camera demo shows how to keep a stream
  smooth while a heavyweight detector evaluates the newest frame continuously.

## Install from NuGet

The bindings target **.NET 10**. Both native backends are **Windows x64**.
Pick one backend: each ships `locate_anything.dll` under
`runtimes/win-x64/native`, and the .NET SDK copies that DLL next to the app.
The NuGet packages are the bindings plus one native engine. Build the image
and live-camera demos from this repository
([Build from source](#build-from-source)).

Windows x64 engine for an NVIDIA GPU (RTX 20 series and newer). It uses the
GPU when a usable GPU and driver are present, and CPU otherwise. The CUDA
runtime DLLs are required in both cases:

```powershell
dotnet add package Mori.LocateAnythingSharp
dotnet add package Mori.LocateAnythingSharp.Runtime.Cuda
```

[`Mori.LocateAnythingSharp.Runtime.Cuda`](https://www.nuget.org/packages/Mori.LocateAnythingSharp.Runtime.Cuda)
(~110 MB) embeds the RTX 20/30/40/50 kernels plus PTX. The CUDA runtime DLLs
(`cudart64_*`, `cublas64_*`, `cublasLt64_*`) must be loadable — next to the
application, or from an installed CUDA Toolkit on `PATH`. Without them the
engine DLL fails to load with `DllNotFoundException`. GPU execution also needs
the NVIDIA display driver; with a GPU but no driver, the engine reports `cpu`.
Set `LA_DEVICE=cpu` to force CPU.

<!-- TODO: state the CUDA Toolkit version used to build the published Mori.LocateAnythingSharp.Runtime.Cuda 0.1.0 package (which cudart64/cublas64/cublasLt64 build to install). -->

Windows x64 CPU engine (~0.7 MB). It has no GPU kernels and no CUDA runtime
dependency:

```powershell
dotnet add package Mori.LocateAnythingSharp
dotnet add package Mori.LocateAnythingSharp.Runtime.Cpu
```

[`Mori.LocateAnythingSharp.Runtime.Cpu`](https://www.nuget.org/packages/Mori.LocateAnythingSharp.Runtime.Cpu)
reports `cpu` as its device. Expect several seconds per image (about 7 s on the
CPU in the [performance](#performance) table). For an NVIDIA RTX 20-series GPU
or newer, [`Runtime.Cuda`](https://www.nuget.org/packages/Mori.LocateAnythingSharp.Runtime.Cuda)
is about an order of magnitude faster on that same measurement and falls back
to CPU when the GPU cannot be used.

### Model weights

The GGUF weights are left out of the NuGet packages (size and the NVIDIA model
license). Download `locate-anything-q8_0.gguf` (~6.3 GB) from
[mudler/locate-anything.cpp-gguf](https://huggingface.co/mudler/locate-anything.cpp-gguf).
That is the file [`scripts/setup.ps1`](scripts/setup.ps1) saves under `models/`.
Pass the path to `LocateEngine.Load`, set `LOCATEANYTHING_MODEL` to the file,
or place it in a `models` folder next to (or above) the executable and call
`ModelLocator.FindDefault()`. If `LOCATEANYTHING_MODEL` names an existing file,
that path is used. Otherwise the locator walks parent directories for a
`models` folder and picks the first `.gguf` whose name contains `q8`, or the
first `.gguf` if none do.

The weights are derived from
[nvidia/LocateAnything-3B](https://huggingface.co/nvidia/LocateAnything-3B)
and stay under NVIDIA's model license: **non-commercial use only** at the time
of writing. See [License](#license) before shipping anything commercial.

## Using the library

```csharp
using Mori.LocateAnything;

using var engine = LocateEngine.Load("locate-anything-q8_0.gguf");
Console.WriteLine($"device: {engine.DeviceName}");   // "CUDA0" or "cpu"

var boxes = engine.Locate("street.jpg", LocateEngine.PromptFor("person", "car"));
foreach (var d in boxes)
    Console.WriteLine(d);                            // person [x1,y1,x2,y2]
```

`LocateEngine` wraps the native context: `Load` reads the GGUF model and
uploads it to the compute device (a few seconds, do it once), `Locate` accepts
a file path or encoded image bytes (PNG/JPEG) and returns `Detection` records
with label and pixel coordinates (`X1`, `Y1`, `X2`, `Y2`). The default decode
mode is `DecodeMode.Hybrid`; `Slow` is pure autoregressive and `Fast` is
multi-token prediction only. Serialize `Locate` calls on a single engine
instance. See [dotnet/README.md](dotnet/README.md) for the project map, GPU
behavior, and the model-resolution rules shared by all demos.

## Build from source

Prerequisites: git, the .NET 10 SDK, and Visual Studio with the C++ CMake
tools workload. The NVIDIA CUDA Toolkit is optional - with it the engine is
built with GPU kernels, without it the build is CPU-only.

```bat
setup.bat
```

One command builds everything: it clones and patches the native engine, builds
it (CUDA multi-arch when the toolkit is present), downloads the model weights
(~6.3 GB, skipped when already there), builds the .NET solution, and copies
the native DLLs next to the demos. Re-running is safe - each step skips work
already done.

Then try a demo:

```powershell
# GUI: open an image, type categories, see boxes
dotnet\LocateAnything.Demo.Image\bin\Debug\net10.0-windows\LocateAnything.Demo.Image.exe

# GUI: live camera with continuous detection
dotnet\LocateAnything.Demo.Camera\bin\Debug\net10.0-windows\LocateAnything.Demo.Camera.exe

# Console: one image, timed runs
dotnet run --project dotnet\LocateAnything.Demo --no-build -- photo.jpg "person,car" 3
```

## Performance

Measured on an RTX 5090 laptop GPU with the recommended q8_0 weights, hybrid
decode mode, a 448 px image with 4 detections:

| | Model load (once) | Per image |
| --- | --- | --- |
| GPU (CUDA) | ~3.5 s | **~1.0 s** |
| CPU (Ryzen-class, 16 threads) | ~2 s | ~7 s |

Decoding cost scales with the number of objects found: sparse scenes are
faster, dense scenes slower. For live video, run detection on the newest frame
in the background (as the camera demo does) rather than per-frame.

## Repository layout

| Path | Contents |
| --- | --- |
| `dotnet/` | C# solution: `Mori.LocateAnythingSharp` bindings, the CPU and CUDA runtime packages, and console, image, and live-camera demos. |
| `scripts/` | `setup.ps1` (full environment setup), `pack.ps1` (NuGet packages), `package-native.ps1` (native DLL packaging), `capture-window.ps1` (README screenshots). |
| `patches/` | Windows-port patch for the third-party C++ engine (symbol exports, device-name API). |
| `setup.bat` | One-shot setup - runs `scripts/setup.ps1`. |
| `docs/` | Screenshots and example output. |
| `locate-anything.cpp/` | *(not in git)* Clone of the native engine, patched and built locally. |
| `models/` | *(not in git)* GGUF model weights. |

## How it works

The heavy lifting is NVIDIA's LocateAnything-3B, a 3-billion-parameter
vision-language model that emits bounding boxes as coordinate tokens. This
repository builds it into a self-contained Windows DLL via the
[locate-anything.cpp](https://github.com/mudler/locate-anything.cpp) inference
engine (C++/ggml - validated box-identical to the official implementation),
and layers a small, safe C# API on top through P/Invoke. The `patches/` folder
carries the Windows-specific fixes until they land upstream.

## License

Code in this repository: Copyright © 2026 Cristian Mori, licensed under the
[Apache License 2.0](LICENSE).

Third-party components keep their own licenses:

- [locate-anything.cpp](https://github.com/mudler/locate-anything.cpp) - MIT license.
- NVIDIA CUDA runtime DLLs - NVIDIA redistributable license.
- Model weights derived from
  [nvidia/LocateAnything-3B](https://huggingface.co/nvidia/LocateAnything-3B) -
  NVIDIA model license, **non-commercial use only** at the time of writing.
  Review it before shipping anything commercial.

## Related projects

Part of a family of open-source robotics / industrial-automation libraries by @CristianMori:

- **EtherNet/IP + CIP Safety:** [Python](https://github.com/CristianMori/EthernetIpPython) · [C#](https://github.com/CristianMori/EthernetIpSharp) · [Rust](https://github.com/CristianMori/EthernetIpRust) · [C++](https://github.com/CristianMori/EthernetIpCpp)
- **PLC tooling:** [MoriFixPlc](https://github.com/CristianMori/MoriFixPlc) (portable PLC HAL) · [EscaFlow](https://github.com/CristianMori/Mori.EscaFlow) (PLC transpiler)
- **ROS in pure C#:** [Mori.RosSharp](https://github.com/CristianMori/Mori.RosSharp) (ROS 1) · [Mori.Ros2Sharp](https://github.com/CristianMori/Mori.Ros2Sharp) (ROS 2)
- **Simulation & vision:** [GemelliStudio](https://github.com/CristianMori/GemelliStudio) · [LocateAnythingSharp](https://github.com/CristianMori/LocateAnythingSharp) · [Mori.SkyScope](https://github.com/CristianMori/Mori.SkyScope)
- **AI agents:** [Deckhand](https://github.com/CristianMori/DeckHand)
