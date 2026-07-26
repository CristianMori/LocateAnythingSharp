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

# Builds the NuGet packages:
#   Mori.LocateAnythingSharp               (managed bindings)
#   Mori.LocateAnythingSharp.Runtime.Cpu   (CPU-only native engine)
#   Mori.LocateAnythingSharp.Runtime.Cuda  (CUDA native engine; skipped when
#                                           the CUDA Toolkit is not installed)
#
# Compiles both native engine variants first (the engine source must already
# be cloned and patched - run setup.bat once beforehand), then packs into
# artifacts/nuget. Publishing is a deliberate manual step; this script only
# prints the push command.
#
# Usage: powershell -File pack.ps1
param()

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$engineDir = Join-Path $root "locate-anything.cpp"
$outDir = Join-Path $root "artifacts\nuget"

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

if (-not (Test-Path $engineDir)) { throw "Engine source missing - run setup.bat first." }

# Visual Studio toolchain (same discovery as setup.ps1).
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "No Visual Studio installation with C++ tools found." }
$cmake  = Join-Path $vs "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$ninja  = Join-Path $vs "Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"

# Builds one engine variant into the given directory.
function Build-Engine([string]$buildDir, [string]$extraFlags) {
    cmd /c "`"$vcvars`" >nul 2>&1 && `"$cmake`" -B `"$buildDir`" -S `"$engineDir`" -G Ninja -DCMAKE_BUILD_TYPE=Release -DLA_BUILD_CLI=OFF -DLA_SHARED=ON $extraFlags -DCMAKE_MAKE_PROGRAM=`"$ninja`" && `"$cmake`" --build `"$buildDir`" -j"
    if ($LASTEXITCODE -ne 0) { throw "Native build failed in $buildDir." }
}

Step "Building CPU-only engine"
Build-Engine (Join-Path $engineDir "build-cpu") ""

$cudaPath = [Environment]::GetEnvironmentVariable("CUDA_PATH", "Machine")
$useCuda = $cudaPath -and (Test-Path (Join-Path $cudaPath "bin\nvcc.exe"))
if ($useCuda) {
    Step "Building CUDA engine (RTX 20-50 series + PTX)"
    Build-Engine (Join-Path $engineDir "build-cuda") `
        "-DLA_GGML_CUDA=ON -DCMAKE_CUDA_ARCHITECTURES=`"75-real;80-real;86-real;89-real;120a-real;120-virtual`""
} else {
    Write-Warning "CUDA Toolkit not found - skipping the CUDA runtime package."
}

Step "Packing NuGet packages"
New-Item -ItemType Directory -Force $outDir | Out-Null
$projects = @(
    "dotnet\Mori.LocateAnythingSharp\Mori.LocateAnythingSharp.csproj",
    "dotnet\Mori.LocateAnythingSharp.Runtime.Cpu\Mori.LocateAnythingSharp.Runtime.Cpu.csproj"
)
if ($useCuda) { $projects += "dotnet\Mori.LocateAnythingSharp.Runtime.Cuda\Mori.LocateAnythingSharp.Runtime.Cuda.csproj" }
foreach ($proj in $projects) {
    dotnet pack (Join-Path $root $proj) -c Release -o $outDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed for $proj" }
}

Step "Done"
Get-ChildItem $outDir -Filter *.nupkg | ForEach-Object {
    "{0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)
}
Write-Host "`nTo publish (deliberate manual step):"
Write-Host "  dotnet nuget push `"$outDir\*.nupkg`" --api-key <YOUR_KEY> --source https://api.nuget.org/v3/index.json"
