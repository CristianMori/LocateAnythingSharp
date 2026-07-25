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

// Console demo: loads the model once, then runs open-vocabulary detection on an
// image several times to show steady-state per-image latency.
//
// Usage: LocateAnything.Demo <image> [category,category,...] [runs] [model.gguf]
// The model defaults to the LOCATEANYTHING_MODEL environment variable or the
// first *.gguf in a "models" folder next to (or above) the executable.

using System.Diagnostics;
using Mori.LocateAnything;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: LocateAnything.Demo <image> [category,category,...] [runs] [model.gguf]");
    return 1;
}
string image = args[0];
string[] cats = (args.Length > 1 ? args[1] : "person").Split(',', StringSplitOptions.TrimEntries);
int runs = args.Length > 2 ? int.Parse(args[2]) : 3;
string? model = args.Length > 3 ? args[3] : ModelLocator.FindDefault();
if (model is null)
{
    Console.Error.WriteLine($"No GGUF model found. Pass one as the 4th argument, set {ModelLocator.EnvVar}, " +
                            "or place a model in a 'models' folder next to the app.");
    return 1;
}

// First-run check: if there is an NVIDIA GPU without a driver, offer to install it.
GpuSupport.EnsureNvidiaDriver();

Console.WriteLine($"Loading {Path.GetFileName(model)} ...");
var swLoad = Stopwatch.StartNew();
using var engine = LocateEngine.Load(model);
swLoad.Stop();
Console.WriteLine($"Loaded in {swLoad.Elapsed.TotalSeconds:F1} s on device: {engine.DeviceName}");

// GPU + driver present but the engine still chose CPU: the usual cause is a
// missing CUDA runtime next to the executable, so point there explicitly.
if (!engine.IsGpu && GpuSupport.HasNvidiaGpu() && GpuSupport.HasNvidiaDriver())
    Console.WriteLine("Note: NVIDIA GPU + driver present but engine chose CPU - " +
                      "check that the CUDA runtime DLLs (cudart/cublas) are next to the app.");

string prompt = LocateEngine.PromptFor(cats);
Console.WriteLine($"Prompt: {prompt}\n");

// The first run includes one-time graph/buffer setup; later runs are steady state.
for (int r = 1; r <= runs; r++)
{
    var sw = Stopwatch.StartNew();
    var detections = engine.Locate(image, prompt);
    sw.Stop();
    Console.WriteLine($"Run {r}: {detections.Count} detections in {sw.Elapsed.TotalMilliseconds:F0} ms");
    if (r == 1)
        foreach (var d in detections)
            Console.WriteLine($"  {d}");
}
return 0;
