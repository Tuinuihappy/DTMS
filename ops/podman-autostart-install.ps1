# ---------------------------------------------------------------------------
#  Registers ops/podman-autostart.ps1 as the scheduled task
#  "dtms-podman-autostart". Per-user task, no admin rights needed (same as
#  dtms-windows-exporter). Safe to re-run: it replaces the existing task.
#
#    powershell -ExecutionPolicy Bypass -File ops\podman-autostart-install.ps1
#
#  Triggers: at logon, plus every 5 minutes — if the supervisor is killed the
#  next firing brings it back. MultipleInstances=IgnoreNew and the script's own
#  mutex keep it to one copy.
#
#  Also takes over two things started elsewhere until now:
#    * the "DTMS LAN forward" Startup shortcut is removed — two forwarders
#      fight over the same ports (each clears the other's reverse tunnel);
#    * the "dtms-windows-exporter" task is disabled — it ran the exporter in a
#      visible console window, and closing that window killed it. The
#      supervisor starts it hidden, reading the command line from that task.
#
#  Uninstall: Unregister-ScheduledTask -TaskName dtms-podman-autostart -Confirm:$false
# ---------------------------------------------------------------------------

$ErrorActionPreference = 'Stop'

$TaskName = 'dtms-podman-autostart'
$Script   = Join-Path $PSScriptRoot 'podman-autostart.ps1'

$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument (
    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$Script`"")

$atLogon = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$every5  = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
    -RepetitionInterval (New-TimeSpan -Minutes 5)

$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable

$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Action $action `
    -Trigger @($atLogon, $every5) -Settings $settings -Principal $principal `
    -Description 'Keeps the DTMS podman stack, windows_exporter and the LAN forwarder running (ops/podman-autostart.ps1).' `
    -Force | Out-Null
Write-Host "Registered scheduled task '$TaskName'."

$shortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'DTMS LAN forward.lnk'
if (Test-Path $shortcut) {
    Remove-Item $shortcut
    Write-Host "Removed Startup shortcut '$shortcut' (the supervisor starts the forwarder now)."
}

if ((Get-ScheduledTask -TaskName 'dtms-windows-exporter' -ErrorAction SilentlyContinue).State -ne 'Disabled') {
    Disable-ScheduledTask -TaskName 'dtms-windows-exporter' | Out-Null
    Write-Host "Disabled task 'dtms-windows-exporter' (the supervisor starts the exporter hidden now)."
}

Start-ScheduledTask -TaskName $TaskName
Write-Host "Started. Log: $env:LOCALAPPDATA\dtms-podman-autostart.log"
