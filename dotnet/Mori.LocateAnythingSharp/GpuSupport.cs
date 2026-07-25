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

using System.Diagnostics;
using Microsoft.Win32;

namespace Mori.LocateAnything;

/// <summary>
/// First-run environment checks for GPU acceleration on Windows.
/// The native engine automatically uses CUDA when an NVIDIA GPU + driver are
/// present and falls back to CPU otherwise; these helpers explain *why* the
/// CPU path was taken and can prompt the user to install the NVIDIA driver.
/// </summary>
public static class GpuSupport
{
    public const string NvidiaDriverDownloadUrl = "https://www.nvidia.com/en-us/drivers/";

    /// <summary>
    /// True if an NVIDIA PCI device is present. Reads the PCI device enumeration
    /// from the registry (vendor id 0x10DE = NVIDIA), so it works even when no
    /// driver is installed - which is exactly the case we want to detect.
    /// </summary>
    public static bool HasNvidiaGpu()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var pci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
        if (pci is null) return false;
        foreach (string name in pci.GetSubKeyNames())
            if (name.StartsWith("VEN_10DE", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// True if the NVIDIA display driver is installed. nvcuda.dll (the CUDA
    /// driver interface) ships with the display driver, not with any toolkit,
    /// so its presence in System32 is a reliable indicator.
    /// </summary>
    public static bool HasNvidiaDriver() =>
        OperatingSystem.IsWindows()
        && File.Exists(Path.Combine(Environment.SystemDirectory, "nvcuda.dll"));

    /// <summary>
    /// Call once at startup. If an NVIDIA GPU is present but its driver is not
    /// installed, prompts on the console (when interactive) and optionally opens
    /// the NVIDIA driver download page. Returns true when GPU acceleration is
    /// expected to work.
    /// </summary>
    public static bool EnsureNvidiaDriver(TextWriter? log = null)
    {
        log ??= Console.Out;
        if (!HasNvidiaGpu())
        {
            log.WriteLine("No NVIDIA GPU detected - running on CPU.");
            return false;
        }
        if (HasNvidiaDriver())
            return true;

        log.WriteLine("An NVIDIA GPU was detected, but the NVIDIA driver is not installed.");
        log.WriteLine("Detection will run on CPU (much slower) until the driver is installed.");

        // Only ask when a human is attached to stdin; in services/CI just log.
        if (!Console.IsInputRedirected)
        {
            Console.Write("Open the NVIDIA driver download page now? [Y/n] ");
            string? answer = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(answer) || answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo(NvidiaDriverDownloadUrl) { UseShellExecute = true });
                log.WriteLine("After installing the driver, restart this application to enable GPU acceleration.");
            }
        }
        else
        {
            log.WriteLine($"Install it from {NvidiaDriverDownloadUrl} and restart to enable GPU acceleration.");
        }
        return false;
    }
}
