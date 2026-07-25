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

using System.Management;
using OpenCvSharp;

namespace Mori.LocateAnything.Demo.Camera;

/// <summary>A selectable capture device: OpenCV index + display name.</summary>
public sealed record CameraInfo(int Index, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Discovers available cameras. OpenCV addresses cameras by integer index only,
/// so we probe indices for a device that actually opens and pair them with
/// device names from WMI. The index/name pairing is by enumeration order - a
/// best-effort match that is correct on typical single/dual-camera machines.
/// </summary>
public static class CameraEnumerator
{
    private const int MaxProbe = 5;

    public static List<CameraInfo> Discover()
    {
        List<string> names = QueryWmiCameraNames();
        var found = new List<CameraInfo>();
        for (int i = 0; i < Math.Max(MaxProbe, names.Count); i++)
        {
            // DSHOW opens faster and more reliably than MSMF for probing.
            using var probe = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
            if (!probe.IsOpened()) continue;
            string name = i < names.Count ? names[i] : $"Camera {i}";
            found.Add(new CameraInfo(i, name));
        }
        return found;
    }

    /// <summary>Human-readable camera names from WMI (empty list if the query fails).</summary>
    private static List<string> QueryWmiCameraNames()
    {
        var names = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'");
            foreach (var device in searcher.Get())
                if (device["Name"] is string s)
                    names.Add(s);
        }
        catch
        {
            // WMI unavailable (rare) - fall back to generic "Camera N" labels.
        }
        return names;
    }
}
