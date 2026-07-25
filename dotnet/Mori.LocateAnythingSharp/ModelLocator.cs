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

namespace Mori.LocateAnything;

/// <summary>
/// Finds a GGUF model file without hardcoding paths in every app:
/// 1. the LOCATEANYTHING_MODEL environment variable, if it points to a file;
/// 2. a "models" folder next to the executable or in any parent directory
///    (first *.gguf found wins, preferring names containing "q8").
/// </summary>
public static class ModelLocator
{
    public const string EnvVar = "LOCATEANYTHING_MODEL";

    /// <summary>Returns a model path, or null when nothing was found.</summary>
    public static string? FindDefault()
    {
        string? fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        // Walk from the app folder upward looking for a "models" directory.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string models = Path.Combine(dir.FullName, "models");
            if (!Directory.Exists(models)) continue;
            string[] ggufs = Directory.GetFiles(models, "*.gguf");
            if (ggufs.Length == 0) continue;
            // Prefer the near-lossless q8 quantization when several are present.
            return ggufs.FirstOrDefault(p => Path.GetFileName(p).Contains("q8", StringComparison.OrdinalIgnoreCase))
                   ?? ggufs[0];
        }
        return null;
    }
}
