# Mori.LocateAnythingSharp.Runtime.Cpu

CPU-only native engine for
[Mori.LocateAnythingSharp](https://www.nuget.org/packages/Mori.LocateAnythingSharp)
(Windows x64). Runs on any modern x64 CPU - no GPU, no extra dependencies.

This package contains no managed code: it ships `locate_anything.dll` under
`runtimes/win-x64/native`, and the .NET SDK copies it next to your application
automatically.

## Usage

Install together with the bindings package:

```
dotnet add package Mori.LocateAnythingSharp
dotnet add package Mori.LocateAnythingSharp.Runtime.Cpu
```

Then use the API as usual - the engine reports `cpu` as its device:

```csharp
using Mori.LocateAnything;

using var engine = LocateEngine.Load("locate-anything-q8_0.gguf");
Console.WriteLine(engine.DeviceName);   // "cpu"
```

Expect several seconds per image on CPU. If the machine has an NVIDIA RTX GPU
(20 series or newer), use `Mori.LocateAnythingSharp.Runtime.Cuda` instead for
roughly an order of magnitude faster inference with automatic CPU fallback.

You also need the GGUF model weights (~6.3 GB, downloaded separately) - see
the bindings package README or
https://github.com/CristianMori/LocateAnythingSharp for details.
