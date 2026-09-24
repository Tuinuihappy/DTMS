@echo off
REM Launcher for podman-lan-forward.ps1 — see that file for what this is and why.
REM Normally started by ops\podman-autostart.ps1; run it by hand only if that
REM supervisor is not running (two forwarders fight over the same ports).
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0podman-lan-forward.ps1"
