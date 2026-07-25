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

# Copies the native engine DLL and the NVIDIA CUDA redistributable runtime DLLs
# next to every .NET demo app so each output folder is self-contained.
# Usage: powershell -File package-native.ps1 [-Configuration Debug|Release]
param([string]$Configuration = "Debug")

$root     = Split-Path $PSScriptRoot -Parent
# Prefer the CUDA build; fall back to the CPU-only build directory.
$buildDir = Join-Path $root "locate-anything.cpp\build-cuda"
if (-not (Test-Path (Join-Path $buildDir "locate_anything.dll"))) {
    $buildDir = Join-Path $root "locate-anything.cpp\build"
}
if (-not (Test-Path (Join-Path $buildDir "locate_anything.dll"))) {
    throw "Native engine not built - run setup.bat (or build locate-anything.cpp) first."
}

# CUDA redistributable runtime (needed by the CUDA-enabled engine DLL; loads and
# reports zero devices on machines without an NVIDIA GPU -> CPU fallback).
$cudaBin  = Join-Path ([Environment]::GetEnvironmentVariable("CUDA_PATH", "Machine")) "bin"
$cudaDlls = @()
if (Test-Path $cudaBin) {
    foreach ($p in @("cudart64_*.dll", "cublas64_*.dll", "cublasLt64_*.dll")) {
        $cudaDlls += Get-ChildItem -Path $cudaBin -Recurse -Filter $p
    }
} else {
    Write-Warning "CUDA_PATH not set - skipping CUDA runtime DLLs (CPU-only deployment)"
}

$demos = @("LocateAnything.Demo", "LocateAnything.Demo.Image", "LocateAnything.Demo.Camera")
foreach ($demo in $demos) {
    # GUI demos target net10.0-windows, the console demo plain net10.0.
    $outDirs = Get-ChildItem -Path (Join-Path $root "dotnet\$demo\bin\$Configuration") -Directory -ErrorAction SilentlyContinue
    if (-not $outDirs) { Write-Warning "$demo not built for $Configuration - skipping"; continue }
    foreach ($outDir in $outDirs) {
        # The engine DLL embeds GPU kernels for RTX 20/30/40/50 series plus PTX
        # for newer architectures, and selects GPU vs CPU automatically at runtime.
        Copy-Item (Join-Path $buildDir "locate_anything.dll") $outDir.FullName -Force
        foreach ($dll in $cudaDlls) { Copy-Item $dll.FullName $outDir.FullName -Force }
        Write-Host "Packaged native DLLs -> $($outDir.FullName)"
    }
}
