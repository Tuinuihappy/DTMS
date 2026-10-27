# ---------------------------------------------------------------------------
#  Rebuilds the DTMS images on podman and restarts what uses them.
#
#    powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1              # api + frontend
#    powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 -Only api
#    powershell -ExecutionPolicy Bypass -File ops\podman-rebuild.ps1 -Only frontend
#
#  Why not `docker-compose build`: there is no buildx here, so compose falls
#  back to the classic builder, and podman's compat API drops that upload
#  mid-stream ("file has already been closed") — it has also killed the
#  rootful podman.socket doing it. `podman build` works, with two conditions:
#
#    * --network=host — RUN steps otherwise hit the same netavark/nftables bug
#      that stops podman publishing ports on this machine;
#    * retries — Alpine's CDN intermittently resets HTTPS from this network
#      (~1 in 8 connections), which fails the frontend's `apk add`. Layers are
#      cached, so a retry resumes where it failed.
#
#  Order for the api: build → run the migrator to completion → recreate api +
#  outbox-worker only if migrations succeeded. The autostart supervisor is
#  paused meanwhile (it would otherwise see the api missing mid-recreate and
#  race this with its own `compose up`).
# ---------------------------------------------------------------------------

param(
    [ValidateSet('all', 'api', 'frontend')]
    [string]$Only = 'all',
    [int]$Retries = 3,
    # Free commit (GB) required before a build starts. See Assert-Headroom.
    [double]$MinFreeCommitGB = 6,
    [switch]$IgnoreMemory
)

$ErrorActionPreference = 'Stop'

$Repo      = Split-Path -Parent $PSScriptRoot
$Podman    = "$env:LOCALAPPDATA\Programs\Podman\podman.exe"
$Compose   = (Get-Command docker-compose -ErrorAction SilentlyContinue).Source
if (-not $Compose) { $Compose = "$env:USERPROFILE\.local\bin\docker-compose.exe" }
$PauseFile = "$env:LOCALAPPDATA\dtms-podman-autostart.pause"
$env:DOCKER_HOST = 'npipe:////./pipe/docker_engine'   # compose otherwise targets the removed Docker Desktop pipe

function Step([string]$msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

function Invoke-Native([scriptblock]$cmd) {
    # Native stderr is progress noise for podman/compose; judge by exit code.
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & $cmd 2>&1 | ForEach-Object { "$_" } | Write-Host; return $LASTEXITCODE }
    finally { $ErrorActionPreference = $prev }
}

function Invoke-Compose([string[]]$composeArgs) {
    Invoke-Native { & $Compose -f docker-compose.yml -f docker-compose.podman.yml --profile prod @composeArgs }
}

# A build runs inside the podman WSL VM, which grows (npm ci, dotnet
# publish) and does not hand memory back to Windows. 2026-10-27: with the
# host at ~42/45.5 GB commit, a frontend build tipped it over, Windows
# stopped the VM, and postgres/api/frontend all went down for ~15 min -
# not just the image being built. So refuse to start (or retry) a build
# without headroom; -IgnoreMemory overrides. ASCII only in strings: this
# BOM-less file is read as ANSI by PowerShell 5.1.
function Assert-Headroom {
    if ($IgnoreMemory) { return }
    $os = Get-CimInstance Win32_OperatingSystem
    $freeGB = [math]::Round($os.FreeVirtualMemory / 1MB, 1)
    $limitGB = [math]::Round($os.TotalVirtualMemorySize / 1MB, 1)
    if ($freeGB -lt $MinFreeCommitGB) {
        throw ("only $freeGB GB of $limitGB GB commit free (need $MinFreeCommitGB). " +
            'A build now can crash the podman VM and stop the WHOLE stack. ' +
            'Free memory or reboot the PC off-hours, or rerun with -IgnoreMemory.')
    }
    Write-Host "memory: $freeGB GB commit free of $limitGB GB"
}

function Build-Image([string]$tag, [string]$dockerfile, [string]$context, [string[]]$extra = @()) {
    for ($i = 1; $i -le $Retries; $i++) {
        Assert-Headroom
        Step "podman build $tag (attempt $i/$Retries)"
        $code = Invoke-Native { & $Podman build --network=host -t $tag -f $dockerfile @extra $context }
        if ($code -eq 0) { return }
        Write-Host "build failed (exit $code)" -ForegroundColor Yellow
    }
    throw "podman build $tag failed $Retries times."
}

function Read-DotEnv([string]$key) {
    $line = Get-Content (Join-Path $Repo '.env') -ErrorAction SilentlyContinue |
        Where-Object { $_ -match "^\s*$key\s*=" } | Select-Object -Last 1
    if ($line) { return ($line -split '=', 2)[1].Trim().Trim('"') }
    return [Environment]::GetEnvironmentVariable($key)
}

function Wait-Http([string]$url, [int]$seconds = 180) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            if ((Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200) { return $true }
        } catch { }
        Start-Sleep -Seconds 3
    }
    return $false
}

Push-Location $Repo
try {
    Step 'checking the podman engine'
    if ((Invoke-Native { & $Podman version --format '{{.Server.Version}}' }) -ne 0) {
        Invoke-Native { & $Podman machine start } | Out-Null
        Invoke-Native { & $Podman machine ssh 'sudo systemctl start podman.socket' } | Out-Null
        if ((Invoke-Native { & $Podman version --format '{{.Server.Version}}' }) -ne 0) {
            throw 'podman engine is not reachable.'
        }
    }

    # Build everything first: a failed build must not leave a half-restarted stack.
    if ($Only -in 'all', 'api') {
        Build-Image 'dtms-api' 'Dockerfile' '.'
    }
    if ($Only -in 'all', 'frontend') {
        $buildArgs = @()
        foreach ($k in 'NEXT_PUBLIC_OPERATOR_MOCK_GPS_LAT', 'NEXT_PUBLIC_OPERATOR_MOCK_GPS_LNG', 'NEXT_PUBLIC_DTMS_HUBS_URL') {
            $v = Read-DotEnv $k
            if ($v) { $buildArgs += '--build-arg', "$k=$v" }
        }
        Build-Image 'tms-frontend:latest' 'frontend/Dockerfile' 'frontend' $buildArgs
    }

    New-Item -ItemType File -Force $PauseFile | Out-Null
    try {
        if ($Only -in 'all', 'api') {
            Step 'running migrations'
            if ((Invoke-Compose @('up', '-d', '--no-build', '--force-recreate', 'migrator')) -ne 0) {
                throw 'could not start the migrator.'
            }
            $exit = (& $Podman wait dtms-migrator 2>$null) -join ''
            Invoke-Native { & $Podman logs --tail 3 dtms-migrator } | Out-Null
            if ($exit -ne '0') {
                # ASCII only inside strings: PowerShell 5.1 reads this BOM-less
                # file as ANSI, where an em dash decodes to a closing quote.
                throw "migrator exited $exit - api NOT restarted. Read: podman logs dtms-migrator"
            }

            Step 'recreating api + outbox-worker'
            if ((Invoke-Compose @('up', '-d', '--no-build', '--force-recreate', '--no-deps', 'api', 'outbox-worker')) -ne 0) {
                throw 'compose up for api/outbox-worker failed.'
            }
        }
        if ($Only -in 'all', 'frontend') {
            Step 'recreating frontend'
            if ((Invoke-Compose @('up', '-d', '--no-build', '--force-recreate', '--no-deps', 'frontend')) -ne 0) {
                throw 'compose up for frontend failed.'
            }
        }
    } finally {
        Remove-Item $PauseFile -ErrorAction SilentlyContinue
    }

    Step 'waiting for health'
    $ok = $true
    if ($Only -in 'all', 'api') {
        $r = Wait-Http 'http://localhost:5219/health/ready'
        Write-Host ("api       {0}" -f $(if ($r) { 'healthy' } else { 'NOT healthy after 3 min' }))
        $ok = $ok -and $r
    }
    if ($Only -in 'all', 'frontend') {
        $r = Wait-Http 'http://localhost:3000/'
        Write-Host ("frontend  {0}" -f $(if ($r) { 'healthy' } else { 'NOT healthy after 3 min' }))
        $ok = $ok -and $r
    }

    # Each rebuild leaves the previous image untagged; only those are removed.
    Invoke-Native { & $Podman image prune -f } | Out-Null

    if (-not $ok) { throw 'rebuilt, but not everything came up healthy.' }
    Step 'done'
} finally {
    Pop-Location
}
