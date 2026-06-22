<#
.SYNOPSIS
    G1 Phase 4.3 -- measure container exit time under SIGTERM, with and
    without /admin/drain-start being called first. Reproducible scorecard
    for the SignalR drain protocol shipped in G1 Phase 1 (backend) and
    Phase 3 (frontend).

.DESCRIPTION
    Two back-to-back runs:

      A. Baseline: SIGTERM the api container, time the exit.
      B. With drain: POST /api/v1/admin/drain-start (loopback via
         docker exec), sleep the settle window, then SIGTERM, time the exit.

    Caveat -- the drain only helps if there are active SignalR clients to
    receive the "__drain" broadcast and reconnect early. Run A == Run B in
    a cold dev environment is EXPECTED, because broadcast is a no-op when
    no clients are connected. The original M2 baseline (2026-06-19, 149s)
    was measured with active hub connections; without those the residual
    49-ish-second shutdown is inherent ASP.NET + MassTransit + outbox
    teardown that G1 does not address.

    To exercise the full drain delta in dev, open a browser tab on
    http://localhost:3000 (any page that loads a hub subscription) BEFORE
    running this script. The Phase 3 frontend handler (commit 82184b6) is
    what cycles the connection in response to __drain.

.PARAMETER ContainerName
    Container to SIGTERM. Default 'dtms-api'.

.PARAMETER ServiceName
    docker-compose service name (for `docker compose up -d` restart).
    Default 'api'.

.PARAMETER SettleSeconds
    Drain settle window passed to /admin/drain-start in run B. Plus a
    2s buffer is slept on the host side before SIGTERM. Default 10.

.PARAMETER SkipBaseline
    Skip run A. Useful when you have load already wired up and want to
    measure only the drain path.

.PARAMETER SkipDrain
    Skip run B. Useful for re-measuring baseline only after a code change.

.EXAMPLE
    powershell -File scripts/chaos/m2-drain-exit-time.ps1

    Both runs, default 10s settle window. ~3 minutes total including
    container reboots.
#>

param(
    [string]$ContainerName = "dtms-api",
    [string]$ServiceName   = "api",
    [int]$SettleSeconds    = 10,
    [switch]$SkipBaseline,
    [switch]$SkipDrain
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$msg) {
    $stamp = (Get-Date -Format "HH:mm:ss")
    Write-Host "[$stamp] $msg" -ForegroundColor Cyan
}

function Wait-ForHealthy {
    Write-Step "waiting for $ContainerName healthy..."
    $deadline = (Get-Date).AddSeconds(180)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $status = docker inspect --format='{{.State.Health.Status}}' $ContainerName 2>$null
        if ($status -eq 'healthy') {
            Write-Step "healthy"
            return
        }
    }
    throw "$ContainerName did not reach healthy within 180s"
}

function Measure-SigtermExit {
    $start = Get-Date
    docker kill --signal=SIGTERM $ContainerName | Out-Null
    while ((docker ps --format '{{.Names}}' | Where-Object { $_ -eq $ContainerName })) {
        Start-Sleep -Seconds 1
    }
    $end = Get-Date
    return [int]($end - $start).TotalSeconds
}

function Restart-Container {
    docker compose up -d $ServiceName | Out-Null
    Wait-ForHealthy
}

Wait-ForHealthy
$results = @()

if (-not $SkipBaseline) {
    Write-Step "=== Run A: baseline SIGTERM (no drain) ==="
    $baselineSec = Measure-SigtermExit
    Write-Host "    Pod exit took ${baselineSec}s" -ForegroundColor Yellow
    $results += [PSCustomObject]@{ Scenario = "Baseline (no drain)"; ExitSeconds = $baselineSec }
    Restart-Container
}

if (-not $SkipDrain) {
    Write-Step "=== Run B: with /admin/drain-start (settle ${SettleSeconds}s) ==="
    $drainResp = docker exec $ContainerName curl -s -X POST `
        -H "Content-Type: application/json" `
        -d "{`"settleSeconds`":${SettleSeconds}}" `
        http://localhost:8080/api/v1/admin/drain-start
    Write-Host "    drain-start: $drainResp"
    $hostSettle = $SettleSeconds + 2
    Write-Step "sleeping ${hostSettle}s for settle window to elapse"
    Start-Sleep -Seconds $hostSettle
    $drainSec = Measure-SigtermExit
    Write-Host "    Pod exit took ${drainSec}s" -ForegroundColor Yellow
    $results += [PSCustomObject]@{ Scenario = "With drain (${SettleSeconds}s settle)"; ExitSeconds = $drainSec }
    Restart-Container
}

Write-Host ""
Write-Host "=== Results ===" -ForegroundColor Green
$results | Format-Table -AutoSize
Write-Host ""
Write-Host "G1 acceptance target: <=30s with drain under active SignalR client load." -ForegroundColor Gray
Write-Host "Cold-environment numbers reflect ONLY inherent ASP.NET + MassTransit"  -ForegroundColor Gray
Write-Host "teardown -- drain delta materialises when there are clients to broadcast to." -ForegroundColor Gray
