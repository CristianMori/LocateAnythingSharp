# Mori.LocateAnythingSharp

.NET bindings for NVIDIA's LocateAnything-3B — text-prompted, open-vocabulary
object detection that runs fully local. Describe what you want to find in
plain language and get labeled bounding boxes back.

```csharp
using Mori.LocateAnything;

using var engine = LocateEngine.Load("locate-anything-q8_0.gguf");
var boxes = engine.Locate("street.jpg", LocateEngine.PromptFor("person", "car"));
foreach (var d in boxes)
    Console.WriteLine(d);   // person [x1,y1,x2,y2]
```

## Getting started

This package contains the managed bindings only. You also need:

1. **A native backend package** (pick one):
   - `Mori.LocateAnythingSharp.Runtime.Cpu` — runs everywhere, no GPU needed.
   - `Mori.LocateAnythingSharp.Runtime.Cuda` — NVIDIA RTX 20 series and newer,
     with automatic CPU fallback. Additionally requires the CUDA runtime DLLs
     (cudart64, cublas64, cublasLt64) next to your app or an installed CUDA
     Toolkit.

2. **Model weights** (GGUF, not redistributable in a package): download
   `locate-anything-q8_0.gguf` (~6.3 GB) from
   [mudler/locate-anything.cpp-gguf](https://huggingface.co/mudler/locate-anything.cpp-gguf).
   Point `LocateEngine.Load` at it, or place it in a `models` folder and use
   `ModelLocator.FindDefault()`. Note the weights are under NVIDIA's model
   license (non-commercial use only at the time of writing).

Full documentation, demos (image file and live camera), and source:
https://github.com/CristianMori/LocateAnythingSharp
