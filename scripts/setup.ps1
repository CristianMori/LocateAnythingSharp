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

# End-to-end setup for LocateAnythingSharp on a fresh clone:
#   1. clone the native engine (locate-anything.cpp) and apply the Windows patch
#   2. build it with CMake/Ninja from Visual Studio (CUDA multi-arch if the
#      CUDA Toolkit is installed, CPU-only otherwise)
#   3. download the GGUF model weights (skipped if already present)
#   4. build the .NET solution and copy the native DLLs next to the demos
#
# Usage: powershell -File setup.ps1 [-Configuration Debug|Release] [-SkipModel]
param(
    [string]$Configuration = "Debug",
    [switch]$SkipModel
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# ---------------------------------------------------------------- prerequisites
Step "Checking prerequisites"
foreach ($tool in @("git", "dotnet")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is required but not on PATH. Install it and re-run."
    }
}

# Locate Visual Studio (C++ toolchain) plus its bundled CMake and Ninja.
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "Visual Studio with C++ tools is required (vswhere.exe not found)." }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "No Visual Studio installation with C++ tools found." }
$cmake  = Join-Path $vs "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$ninja  = Join-Path $vs "Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
foreach ($p in @($cmake, $ninja, $vcvars)) {
    if (-not (Test-Path $p)) { throw "Missing Visual Studio component: $p (install the 'C++ CMake tools' workload)." }
}
Write-Host "Visual Studio: $vs"

# CUDA Toolkit is optional: with it the engine gets GPU kernels for RTX 20-50
# series; without it the build is CPU-only (still fully functional).
$cudaPath = [Environment]::GetEnvironmentVariable("CUDA_PATH", "Machine")
$useCuda = $cudaPath -and (Test-Path (Join-Path $cudaPath "bin\nvcc.exe"))
Write-Host $(if ($useCuda) { "CUDA Toolkit: $cudaPath (GPU build)" }
             else { "CUDA Toolkit not found - building CPU-only. Install it and re-run for GPU support." })

# ------------------------------------------------------------- native engine
Step "Fetching native engine source"
$engineDir = Join-Path $root "locate-anything.cpp"
if (-not (Test-Path $engineDir)) {
    git clone --recursive https://github.com/mudler/locate-anything.cpp $engineDir
} else {
    Write-Host "Already cloned."
    # The ggml submodule is required; fetch it if the clone was made without --recursive.
    git -C $engineDir submodule update --init
}

# Apply the Windows-port patch once (reverse-check detects an applied patch).
# Run through cmd so git's stderr chatter doesn't trip $ErrorActionPreference.
$patch = Join-Path $root "patches\locate-anything-windows-port.patch"
cmd /c "git -C `"$engineDir`" apply --reverse --check `"$patch`" 2>nul"
if ($LASTEXITCODE -ne 0) {
    git -C $engineDir apply $patch
    if ($LASTEXITCODE -ne 0) { throw "Failed to apply $patch" }
    Write-Host "Windows-port patch applied."
} else {
    Write-Host "Windows-port patch already applied."
}

Step "Building native engine ($(if ($useCuda) { 'CUDA' } else { 'CPU' }))"
$buildDir = Join-Path $engineDir $(if ($useCuda) { "build-cuda" } else { "build" })
$cudaFlags = ""
if ($useCuda) {
    # Real kernels for RTX 20 (sm_75) through RTX 50 (sm_120a) plus PTX for
    # forward compatibility with future GPUs.
    $cudaFlags = "-DLA_GGML_CUDA=ON -DCMAKE_CUDA_ARCHITECTURES=`"75-real;80-real;86-real;89-real;120a-real;120-virtual`""
}
# Configure and build inside the VS developer environment so cl.exe is found.
cmd /c "`"$vcvars`" >nul 2>&1 && `"$cmake`" -B `"$buildDir`" -S `"$engineDir`" -G Ninja -DCMAKE_BUILD_TYPE=Release -DLA_BUILD_CLI=ON -DLA_SHARED=ON $cudaFlags -DCMAKE_MAKE_PROGRAM=`"$ninja`" && `"$cmake`" --build `"$buildDir`" -j"
if ($LASTEXITCODE -ne 0) { throw "Native build failed." }

# -------------------------------------------------------------------- model
if (-not $SkipModel) {
    Step "Model weights"
    $modelsDir = Join-Path $root "models"
    New-Item -ItemType Directory -Force $modelsDir | Out-Null
    $model = Join-Path $modelsDir "locate-anything-q8_0.gguf"
    if (Test-Path $model) {
        Write-Host "Already present: $model"
    } else {
        Write-Host "Downloading q8_0 weights (~6.3 GB) - NVIDIA model license, non-commercial use..."
        curl.exe -L --fail --retry 3 -o $model `
            https://huggingface.co/mudler/locate-anything.cpp-gguf/resolve/main/locate-anything-q8_0.gguf
        if ($LASTEXITCODE -ne 0) { Remove-Item $model -ErrorAction SilentlyContinue; throw "Model download failed." }
    }
}

# ---------------------------------------------------------------- .NET side
Step "Building .NET solution ($Configuration)"
dotnet build (Join-Path $root "dotnet") -c $Configuration -v q
if ($LASTEXITCODE -ne 0) { throw ".NET build failed." }

Step "Packaging native DLLs next to the demos"
& (Join-Path $PSScriptRoot "package-native.ps1") -Configuration $Configuration

Step "Done"
Write-Host "Try the demos, e.g.:"
Write-Host "  dotnet\LocateAnything.Demo.Camera\bin\$Configuration\net10.0-windows\LocateAnything.Demo.Camera.exe"
