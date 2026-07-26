# Mori.LocateAnythingSharp.Runtime.Cuda

CUDA native engine for
[Mori.LocateAnythingSharp](https://www.nuget.org/packages/Mori.LocateAnythingSharp)
(Windows x64). One DLL embeds GPU kernels for NVIDIA RTX 20/30/40/50 series
(plus PTX for newer architectures); at runtime it selects the GPU when a
usable one is present and falls back to CPU otherwise.

This package contains no managed code: it ships `locate_anything.dll` under
`runtimes/win-x64/native`, and the .NET SDK copies it next to your application
automatically.

## Usage

Install together with the bindings package:

```
dotnet add package Mori.LocateAnythingSharp
dotnet add package Mori.LocateAnythingSharp.Runtime.Cuda
```

```csharp
using Mori.LocateAnything;

using var engine = LocateEngine.Load("locate-anything-q8_0.gguf");
Console.WriteLine(engine.DeviceName);   // "CUDA0" on GPU, "cpu" on fallback
```

## Requirements

- **NVIDIA CUDA runtime DLLs** (`cudart64_*`, `cublas64_*`, `cublasLt64_*`)
  must be loadable: either next to your application or via an installed CUDA
  Toolkit on PATH. They are NVIDIA redistributables, far too large to include
  in a NuGet package. Without them the engine DLL fails to load with
  `DllNotFoundException`.
- **NVIDIA display driver** on the target machine for GPU execution. Without
  it (or without any NVIDIA GPU), the engine still works and reports `cpu` -
  the `GpuSupport` helpers in the bindings package can prompt users to install
  the driver.
- Set the `LA_DEVICE=cpu` environment variable to force CPU execution.

You also need the GGUF model weights (~6.3 GB, downloaded separately) - see
the bindings package README or
https://github.com/CristianMori/LocateAnythingSharp for details.
