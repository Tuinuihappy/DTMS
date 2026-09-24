@echo off
REM Launcher for podman-lan-forward.ps1 — see that file for what this is and why.
REM Used by the Startup shortcut; run it by hand to start the forwarder now.
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0podman-lan-forward.ps1"
