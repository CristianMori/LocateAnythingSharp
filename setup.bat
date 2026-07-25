@echo off
rem Copyright 2026 Cristian Mori
rem Licensed under the Apache License, Version 2.0. See dotnet\LICENSE.
rem
rem One-shot setup for LocateAnythingSharp: clones and builds the native
rem engine, downloads the model weights, builds the .NET solution, and
rem packages everything next to the demo executables.
rem The heavy lifting lives in scripts\setup.ps1; this wrapper just makes
rem the whole procedure double-clickable.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup.ps1" %*
if errorlevel 1 (
    echo.
    echo Setup failed - see messages above.
    pause
    exit /b 1
)
echo.
echo Setup complete.
pause
