# ---------------------------------------------------------------------------
#  Packs everything the Linux server needs into one folder: images as a .tar
#  (the server has no internet), the database, the volumes that hold state,
#  and the compose files. ops/server-install.sh unpacks it on the server.
#
#    # rehearsal: old stack keeps running, bundle is a snapshot to test with
#    powershell -ExecutionPolicy Bypass -File ops\server-export.ps1 -ServerHost 10.204.x.x
#
#    # the real move: stops the api here first, so no data is written after
#    # the dump and the two stacks never run against RIOT3/OMS at once
#    powershell -ExecutionPolicy Bypass -File ops\server-export.ps1 -ServerHost 10.204.x.x -Cutover
#
#  -ServerHost is the address browsers will use to reach the server. It is
#  baked into the frontend image (the SignalR URL is compiled into the browser
#  bundle), so the frontend is rebuilt here for that host.
#
#  What travels: the whole amr_delivery_planning database (pg_dump), dp-keys
#  (without it every stored callback token is unreadable), miniodata (POD
#  photos, attachments), redis-data (token revocations), grafana-data.
#  What does not: rabbitmqdata (keyed to this machine's hostname, so the
#  server's RabbitMQ could not read it; -Cutover drains the queues instead) and
#  prometheus-data (15 days of metrics, rebuilt on its own).
# ---------------------------------------------------------------------------

param(
    [Parameter(Mandatory = $true)]
    [string]$ServerHost,
    [switch]$Cutover,
    [string]$OutDir = ("D:\dtms-server-bundle-{0:yyyyMMdd-HHmm}" -f (Get-Date)),
    # Reuse tms-frontend:server from an earlier run instead of rebuilding it.
    # Only safe when that run used the same -ServerHost.
    [switch]$SkipFrontendBuild,
    [int]$Retries = 3
)

$ErrorActionPreference = 'Stop'

$Repo      = Split-Path -Parent $PSScriptRoot
$Podman    = "$env:LOCALAPPDATA\Programs\Podman\podman.exe"
$PauseFile = "$env:LOCALAPPDATA\dtms-podman-autostart.pause"
$TaskName  = 'dtms-podman-autostart'
$env:DOCKER_HOST = 'npipe:////./pipe/docker_engine'

$HubsUrl = "http://${ServerHost}:5219"

# Everything the server's compose run needs, under the names podman stores
# them as. The installer re-tags the two localhost/ ones.
$Images = @(
    'localhost/dtms-api:latest',
    'localhost/tms-frontend:server',
    'docker.io/library/postgres:16-alpine',
    'docker.io/edoburu/pgbouncer:latest',
    'docker.io/prodrigestivill/postgres-backup-local:16',
    'docker.io/masstransit/rabbitmq:3.13',
    'docker.io/library/redis:7-alpine',
    'docker.io/jaegertracing/all-in-one:1.57',
    'docker.io/prom/prometheus:v2.55.1',
    'docker.io/grafana/grafana:11.3.0',
    'quay.io/minio/minio:RELEASE.2025-01-20T14-49-07Z',
    'docker.io/library/alpine:3.20'   # the installer unpacks volumes with it
)
$Volumes = @('dp-keys', 'miniodata', 'redis-data', 'grafana-data')

function Step([string]$msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

function Invoke-Native([scriptblock]$cmd) {
    # Native stderr is progress noise for podman; judge by exit code.
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { & $cmd 2>&1 | ForEach-Object { "$_" } | Write-Host; return $LASTEXITCODE }
    finally { $ErrorActionPreference = $prev }
}

function Invoke-Checked([string]$what, [scriptblock]$cmd) {
    $code = Invoke-Native $cmd
    if ($code -ne 0) { throw "$what failed (exit $code)." }
}

# ASCII only in strings below: PowerShell 5.1 reads this BOM-less file as ANSI.

# Messages waiting in RabbitMQ, not counting MassTransit's _error/_skipped
# queues, which only ever grow and would never reach zero.
function Get-PendingMessages {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try {
        $rows = & $Podman exec dtms-rabbitmq rabbitmqctl list_queues name messages --no-table-headers -q 2>$null
    } finally { $ErrorActionPreference = $prev }
    if ($LASTEXITCODE -ne 0) { return -1 }
    $sum = 0
    foreach ($row in $rows) {
        $parts = "$row" -split '\s+'
        if ($parts.Count -lt 2 -or $parts[0] -match '_(error|skipped)$') { continue }
        $n = 0
        if ([int]::TryParse($parts[1], [ref]$n)) { $sum += $n }
    }
    return $sum
}

function Write-Utf8NoBom([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
}

if (Test-Path $OutDir) { throw "$OutDir already exists - pick another -OutDir or remove it." }

Push-Location $Repo
try {
    Step 'checking the podman engine'
    if ((Invoke-Native { & $Podman version --format '{{.Server.Version}}' }) -ne 0) {
        throw 'podman engine is not reachable - run ops\podman-autostart.ps1 or podman machine start.'
    }

    # Built before anything is stopped: a failed build must not cost downtime.
    if (-not $SkipFrontendBuild) {
        for ($i = 1; $i -le $Retries; $i++) {
            Step "building the frontend for $HubsUrl (attempt $i/$Retries)"
            # --format docker: podman's default OCI format drops HEALTHCHECK.
            $code = Invoke-Native { & $Podman build --network=host --format docker -t tms-frontend:server `
                --build-arg "NEXT_PUBLIC_DTMS_HUBS_URL=$HubsUrl" -f frontend/Dockerfile frontend }
            if ($code -eq 0) { break }
            if ($i -eq $Retries) { throw "frontend build failed $Retries times." }
        }
    }

    New-Item -ItemType Directory -Force "$OutDir\images", "$OutDir\db", "$OutDir\volumes", "$OutDir\backups" | Out-Null

    if ($Cutover) {
        # The supervisor restarts anything that is not running, and ignores the
        # pause file after 30 minutes, so it has to be switched off for good -
        # otherwise the old api comes back and both stacks drive RIOT3 and OMS.
        Step "disabling the $TaskName scheduled task"
        Disable-ScheduledTask -TaskName $TaskName | Out-Null
        New-Item -ItemType File -Force $PauseFile | Out-Null

        Step 'stopping frontend + api (no new work comes in from here on)'
        Invoke-Checked 'podman stop' { & $Podman stop -t 90 tms-frontend dtms-api }

        # The outbox-worker keeps consuming, so whatever the api left queued is
        # handled here rather than lost with this machine's RabbitMQ.
        Step 'letting the outbox-worker drain RabbitMQ (up to 2 min)'
        $deadline = (Get-Date).AddMinutes(2)
        do {
            $pending = Get-PendingMessages
            Write-Host "  pending messages: $pending"
            if ($pending -eq 0) { break }
            Start-Sleep -Seconds 5
        } while ((Get-Date) -lt $deadline)
        if ($pending -ne 0) {
            Write-Host "  WARNING: $pending message(s) still queued; they stay on this machine." -ForegroundColor Yellow
        }

        Step 'stopping outbox-worker'
        Invoke-Checked 'podman stop' { & $Podman stop -t 90 dtms-outbox-worker }
    }

    Step 'dumping the database'
    # Dumped inside the container and copied out as a file: piping pg_dump's
    # binary output through PowerShell would corrupt it.
    Invoke-Checked 'pg_dump' { & $Podman exec dtms-postgres pg_dump -U postgres -Fc -f /tmp/dtms.dump amr_delivery_planning }
    Invoke-Checked 'podman cp' { & $Podman cp dtms-postgres:/tmp/dtms.dump "$OutDir\db\dtms.dump" }
    Invoke-Native { & $Podman exec dtms-postgres rm -f /tmp/dtms.dump } | Out-Null

    foreach ($v in $Volumes) {
        Step "exporting volume $v"
        Invoke-Checked "volume export $v" { & $Podman volume export "dtms_$v" --output "$OutDir\volumes\$v.tar" }
    }

    Step 'saving images (about 3 GB)'
    Invoke-Checked 'podman save' { & $Podman save -m --format docker-archive -o "$OutDir\images\dtms-images.tar" @Images }

    Step 'copying compose files and config'
    Copy-Item docker-compose.yml, docker-compose.server.yml, .env $OutDir
    New-Item -ItemType Directory -Force "$OutDir\ops", "$OutDir\docs" | Out-Null
    Copy-Item -Recurse ops\prometheus, ops\grafana "$OutDir\ops"
    Copy-Item -Recurse docs\grafana "$OutDir\docs"
    # bash chokes on CRLF, and git on Windows may have checked this out with it.
    $sh = [IO.File]::ReadAllText("$Repo\ops\server-install.sh") -replace "`r`n", "`n"
    Write-Utf8NoBom "$OutDir\server-install.sh" $sh
    Write-Utf8NoBom "$OutDir\bundle.env" ("SERVER_HOST={0}`nCUTOVER={1}`nCREATED={2:yyyy-MM-ddTHH:mm:sszzz}`n" -f $ServerHost, [int][bool]$Cutover, (Get-Date))

    Step 'writing SHA256SUMS'
    $root = (Resolve-Path $OutDir).Path.TrimEnd('\') + '\'
    $lines = Get-ChildItem $OutDir -Recurse -File | Sort-Object FullName | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length).Replace('\', '/')
        '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $rel
    }
    Write-Utf8NoBom "$OutDir\SHA256SUMS" (($lines -join "`n") + "`n")

    $size = (Get-ChildItem $OutDir -Recurse -File | Measure-Object Length -Sum).Sum / 1GB
    Step ("bundle ready: {0} ({1:N1} GB)" -f $OutDir, $size)
    Write-Host "Copy the whole folder to the server (scp -r / WinSCP / USB), then on the server:"
    Write-Host "  cd <bundle folder> && sudo bash server-install.sh"
    if ($Cutover) {
        Write-Host ''
        Write-Host 'This machine: frontend, api and outbox-worker are STOPPED and autostart is disabled.' -ForegroundColor Yellow
        Write-Host 'To roll back instead:' -ForegroundColor Yellow
        Write-Host "  Enable-ScheduledTask -TaskName $TaskName; Remove-Item $PauseFile" -ForegroundColor Yellow
        Write-Host '  podman start dtms-outbox-worker dtms-api tms-frontend' -ForegroundColor Yellow
    } else {
        Write-Host ''
        Write-Host 'Rehearsal bundle: this machine is still live. Install it with --rehearsal so the' -ForegroundColor Yellow
        Write-Host 'server copy does not call RIOT3, OMS or WMS at the same time.' -ForegroundColor Yellow
    }
} catch {
    if ($Cutover) {
        Write-Host "`nExport FAILED. If this machine should keep serving, bring it back with:" -ForegroundColor Red
        Write-Host "  Enable-ScheduledTask -TaskName $TaskName; Remove-Item $PauseFile" -ForegroundColor Red
        Write-Host '  podman start dtms-outbox-worker dtms-api tms-frontend' -ForegroundColor Red
    }
    throw
} finally {
    Pop-Location
}
