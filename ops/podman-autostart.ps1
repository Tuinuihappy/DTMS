# ---------------------------------------------------------------------------
#  Keeps the DTMS stack running on podman: machine, containers, windows_exporter
#  and the LAN forwarder. Registered by ops/podman-autostart-install.ps1 as the
#  scheduled task "dtms-podman-autostart" (at logon, re-fired every 5 minutes).
#
#  Why a supervisor and not a plain logon script: nothing started podman after
#  a reboot, so the stack stayed down until someone noticed; windows_exporter
#  ran in a visible console window and died whenever it was closed; and the
#  Startup-folder forwarder started ~6 minutes after logon, long enough to look
#  broken. This checks every minute and restarts whatever is missing, and the
#  task re-fires it every 5 minutes if it is killed too.
#
#  Cheap when healthy: one `podman ps` and two process lookups per minute.
#  `docker-compose up` runs only when a long-running container is not up, so the
#  one-shot migrator is not re-run every minute.
#
#  Log: %LOCALAPPDATA%\dtms-podman-autostart.log (trimmed to the last 2000 lines).
#  Delete this, its installer and the task once Docker Desktop is back.
# ---------------------------------------------------------------------------

$ErrorActionPreference = 'Continue'

# One supervisor at a time: the task re-fires every 5 minutes and a second copy
# would race the first over `podman machine start` and `compose up`.
$mutex = New-Object System.Threading.Mutex($false, 'Local\dtms-podman-autostart')
if (-not $mutex.WaitOne(0)) { exit 0 }

$Repo        = Split-Path -Parent $PSScriptRoot
$Podman      = "$env:LOCALAPPDATA\Programs\Podman\podman.exe"
$Compose     = (Get-Command docker-compose -ErrorAction SilentlyContinue).Source
if (-not $Compose) { $Compose = "$env:USERPROFILE\.local\bin\docker-compose.exe" }
$LanForward  = Join-Path $PSScriptRoot 'podman-lan-forward.ps1'
$Log         = "$env:LOCALAPPDATA\dtms-podman-autostart.log"

# Everything `up` should bring back. The migrator is left to compose's
# depends_on — it is one-shot and is never "running".
$Services = @(
    'postgres', 'redis', 'rabbitmq', 'minio', 'migrator',
    'api', 'outbox-worker', 'frontend', 'portproxy-api', 'portproxy-postgres',
    'postgres-backup', 'jaeger', 'prometheus', 'grafana'
)
$LongRunning = @(
    'dtms-postgres', 'dtms-redis', 'dtms-rabbitmq', 'dtms-minio',
    'dtms-api', 'dtms-outbox-worker', 'tms-frontend',
    'dtms-portproxy-api', 'dtms-portproxy-postgres', 'dtms-postgres-backup',
    'dtms-jaeger', 'dtms-prometheus', 'dtms-grafana'
)

function Write-Log([string]$msg) {
    $line = '{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $msg
    Add-Content -Path $Log -Value $line -Encoding utf8
}

# Runs a native command and logs its output. `*>> $Log` would append UTF-16
# (PowerShell 5.1's redirection default) into a UTF-8 file.
function Invoke-Logged([scriptblock]$cmd) {
    $out = & $cmd 2>&1 | ForEach-Object { "$_" } | Out-String
    if ($out.Trim()) { Write-Log $out.TrimEnd() }
}

function Limit-Log {
    try {
        $lines = Get-Content $Log -ErrorAction Stop
        if ($lines.Count -gt 2500) { $lines | Select-Object -Last 2000 | Set-Content $Log -Encoding utf8 }
    } catch { }
}

function Test-Engine {
    & $Podman version --format '{{.Server.Version}}' *> $null
    return $LASTEXITCODE -eq 0
}

function Ensure-Machine {
    $state = (& $Podman machine inspect --format '{{.State}}' 2>$null) -join ''
    if ($state -ne 'running') {
        Write-Log "machine is '$state' - starting"
        Invoke-Logged { & $Podman machine start }
    }
    if (-not (Test-Engine)) {
        # Seen 2026-09-24: the rootful API socket dies while the VM stays up,
        # and every podman/compose call then fails to connect.
        Write-Log 'engine not answering - restarting the rootful podman.socket'
        Invoke-Logged { & $Podman machine ssh 'sudo systemctl start podman.socket' }
    }
    return Test-Engine
}

function Ensure-Containers {
    $running = & $Podman ps --format '{{.Names}}' 2>$null
    $missing = $LongRunning | Where-Object { $running -notcontains $_ }
    if (-not $missing) { return }

    Write-Log ("not running: {0} - compose up" -f ($missing -join ', '))
    $env:DOCKER_HOST = 'npipe:////./pipe/docker_engine'   # compose otherwise targets the removed Docker Desktop pipe
    Push-Location $Repo
    try {
        # Only the summary lines: compose's full output is a pull/wait log per service.
        $out = & $Compose -f docker-compose.yml -f docker-compose.podman.yml --profile prod `
            up -d --no-build @Services 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
        $out | Where-Object { $_ -match 'Started|Error|error|unhealthy' } | ForEach-Object { Write-Log "  $_" }
        Write-Log "compose up exited $code"
    } finally { Pop-Location }
}

# Started hidden from here instead of through its own task: that task opened a
# visible console window, and closing it killed the exporter (0xC000013A) —
# twice on 2026-09-24. The task is left disabled; its action stays the single
# source of the command line.
function Ensure-Exporter {
    if (Get-Process windows_exporter -ErrorAction SilentlyContinue) { return }
    $task = Get-ScheduledTask -TaskName 'dtms-windows-exporter' -ErrorAction SilentlyContinue
    if (-not $task) { Write-Log 'windows_exporter task not found - skipping'; return }
    $a = $task.Actions[0]
    Write-Log 'windows_exporter not running - starting it hidden'
    Start-Process -FilePath $a.Execute -ArgumentList $a.Arguments -WindowStyle Hidden
}

function Ensure-LanForward {
    $alive = Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
        Where-Object { $_.CommandLine -like '*podman-lan-forward.ps1*' }
    if ($alive) { return }
    Write-Log 'LAN forwarder not running - starting it'
    Start-Process powershell -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden',
        '-File', "`"$LanForward`"")
}

Write-Log "supervisor started (pid $PID)"
while ($true) {
    try {
        if (Ensure-Machine) {
            Ensure-Containers
        } else {
            Write-Log 'engine still unreachable - retrying next round'
        }
        Ensure-Exporter
        Ensure-LanForward
        Limit-Log
    } catch {
        Write-Log "round failed: $($_.Exception.Message)"
    }
    Start-Sleep -Seconds 60
}
