@echo off
cd /d "%~dp0"
if exist "Portable\Gather.exe" (
  start "" "Portable\Gather.exe"
) else (
  echo Run build.ps1 first. .NET 10 Desktop Runtime is required.
  pause
)
