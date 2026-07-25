# Copyright 2026 Cristian Mori
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# Captures the main window of a process to a PNG (used for README screenshots).
# Usage: powershell -File capture-window.ps1 -ProcessId <pid> -OutFile shot.png
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$OutFile
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Capture {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@

# Without DPI awareness, window coordinates come back scaled and the capture
# grabs the wrong screen region on high-DPI displays.
[Win32Capture]::SetProcessDPIAware() | Out-Null

$proc = Get-Process -Id $ProcessId
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { throw "Process $ProcessId has no main window." }

# Bring the window to the front so the screen copy is unobstructed.
[Win32Capture]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 500

$rect = New-Object Win32Capture+RECT
[Win32Capture]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
if ($w -le 0 -or $h -le 0) { throw "Window has no size." }

$bmp = New-Object System.Drawing.Bitmap $w, $h
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$gfx.Dispose(); $bmp.Dispose()
Write-Host "Saved $OutFile ($w x $h)"
