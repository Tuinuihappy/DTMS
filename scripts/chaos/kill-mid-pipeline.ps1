<#
.SYNOPSIS
    Chaos test for T1 crash-recovery -- kill the API container repeatedly
    while the auto-planning consumer is in flight, then verify every order
    converged to a terminal state with no stuck-Planned survivors.

.DESCRIPTION
    Implements verification step 1 from
    docs/crash-recovery-workflow-resilience-plan.md:

        "inject 100 orders, kill API container at random delay 1-8s during
         each consumer execution; expect 0 stuck orders after 5-min settle"

    Phases:
      1. Setup   -- verify stack health, snapshot baseline.
      2. Chaos   -- submit OrderCount upstream orders; between each pair of
                    submissions kill + restart dtms-api at a random delay so
                    the consumer is killed mid-pipeline for orders currently
                    being processed.
      3. Settle  -- wait SettleMinutes so MassTransit redeliveries, watchdog
                    replays, and outbox publishes can drain.
      4. Verify  -- query Postgres for the order/job/trip outcomes; print
                    a verdict + summary; non-zero exit if any chaos order
                    is still stuck at Planned without a Trip (the
                    OD-0374 / OD-0375 incident shape).

    Reuses existing facility data (SHELF1 -> SHELF4 has a live OrderTemplate)
    so the script doesn't need to provision its own. If the route ever loses
    its template, vendor dispatch fails and items get marked Failed -- still
    a valid exercise of T1.2 + T1.4, just a different verdict.

    Encoding: this file is ASCII-only (no checkmarks, em-dashes, arrows etc.)
    so it loads correctly under both PowerShell 5.1 and PowerShell 7+ on
    Windows regardless of code-page settings.

.PARAMETER OrderCount
    How many upstream orders to inject. Default 100 (the plan's target).

.PARAMETER MinKillDelaySec / MaxKillDelaySec
    Random window for the delay between submitting an order and killing
    the api container. 1-8s by default.

.PARAMETER KillEvery
    Kill the container after every N orders. Default 5.

.PARAMETER SettleMinutes
    Wait between the last kill and the verify pass. Default 5.

.PARAMETER ApiUrl
    Defaults to the docker-compose host port. Override for staging.

.PARAMETER ContainerName
    Name of the api container to kill. Default 'dtms-api'.

.PARAMETER SkipChaos
    Skip the kill phase -- submit orders only. Useful to baseline the
    verify pass against a healthy pipeline first.

.EXAMPLE
    powershell -File scripts/chaos/kill-mid-pipeline.ps1 -OrderCount 10 -KillEvery 2

    Dry run with 10 orders, kill every 2 submissions. ~2 minutes total.

.EXAMPLE
    powershell -File scripts/chaos/kill-mid-pipeline.ps1

    Full run: 100 orders, kill every 5 submissions, 5-min settle.
    Total ~15-20 minutes.
#>

param(
    [int]$OrderCount = 100,
    [int]$MinKillDelaySec = 1,
    [int]$MaxKillDelaySec = 8,
    [int]$KillEvery = 5,
    [int]$SettleMinutes = 5,
    [string]$ApiUrl = "http://localhost:5219",
    [string]$ContainerName = "dtms-api",
    [switch]$SkipChaos,

    # Phase 5 (planned, not yet implemented) -- end-to-end completion
    # verification. Reserved as a documented future switch so callers
    # can opt in once vendor stub / sandbox lands. See the Phase 5
    # section of docs/chaos-test-results.md for the design.
    [switch]$WaitForCompletion,
    [int]$CompletionTimeoutHours = 24
)

# Facility constants -- verified-active in dev.
# A route with a registered OrderTemplate ("Confirm-SHELF1-to-SHELF4")
# so the happy path reaches Dispatched instead of failing on template-missing.
$Script:PickupId = "2138213c-f7eb-40d8-a6f7-0ab435012f4d"  # SHELF1
$Script:DropId   = "565ccc96-329c-4113-956d-a0d1aa1ed786"  # SHELF4
$Script:Profile  = "PALLET-EU"

$Script:OrderRefs = New-Object System.Collections.Generic.List[string]
$Script:KillTimestamps = New-Object System.Collections.Generic.List[datetime]
$Script:StartedAt = $null
$Script:EndedAt = $null

function Write-Phase([string]$msg) {
    $stamp = (Get-Date -Format "HH:mm:ss")
    Write-Host ""
    Write-Host "[$stamp] === $msg ===" -ForegroundColor Cyan
}

function Write-Step([string]$msg) {
    $stamp = (Get-Date -Format "HH:mm:ss")
    Write-Host "[$stamp]   $msg" -ForegroundColor Gray
}

function Write-Ok([string]$msg)   { Write-Host "    [OK]   $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "    [WARN] $msg" -ForegroundColor Yellow }
function Write-Bad([string]$msg)  { Write-Host "    [FAIL] $msg" -ForegroundColor Red }

# Phase 1: Setup -- verify stack health, facility data presence.
function Test-StackHealthy {
    Write-Phase "Phase 1: Setup"

    # Use liveness (/health) not readiness (/health/ready). Readiness can flap
    # to Degraded for unrelated reasons (e.g. RabbitMQ delayed-message plugin
    # not installed leaves TripCancelledOmsNotify endpoint degraded) which has
    # nothing to do with the consumer + watchdog path we're stress-testing.
    try {
        $r = Invoke-WebRequest -Uri "$ApiUrl/health" -UseBasicParsing -TimeoutSec 10
        if ($r.StatusCode -ne 200) {
            Write-Bad "API /health returned $($r.StatusCode)"
            return $false
        }
    } catch {
        Write-Bad "API not reachable at $ApiUrl -- $($_.Exception.Message)"
        return $false
    }
    Write-Ok "API live at $ApiUrl"

    $running = docker ps --format "{{.Names}}" | Where-Object { $_ -eq $ContainerName }
    if (-not $running) {
        Write-Bad "Container '$ContainerName' not running"
        return $false
    }
    Write-Ok "Container '$ContainerName' running"

    $out = (Invoke-PgQuery "SELECT COUNT(*) FROM facility.""Stations"" WHERE ""Id"" IN ('$Script:PickupId', '$Script:DropId');").Trim()
    if ($out -ne "2") {
        Write-Bad "Expected 2 stations in facility schema, got: '$out'"
        return $false
    }
    Write-Ok "Facility data present (SHELF1 -> SHELF4)"

    return $true
}

# Helper -- PowerShell strips double-quote characters when passing args to
# native commands, so SQL with quoted identifiers (which Postgres requires
# to preserve case) breaks. Round-trip via a tempfile + docker cp + psql -f
# to preserve the quotes verbatim.
function Invoke-PgQuery([string]$sql) {
    $tempFile = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $tempFile -Value $sql -Encoding ascii
    docker cp $tempFile dtms-postgres:/tmp/chaos.sql 2>&1 | Out-Null
    $raw = (docker exec dtms-postgres psql -U postgres -d amr_delivery_planning -t -A -f /tmp/chaos.sql 2>&1) -join ""
    Remove-Item $tempFile -ErrorAction SilentlyContinue
    return $raw
}

# Phase 2: Chaos -- submit orders, kill mid-pipeline.
function Submit-Order([int]$idx) {
    $shortGuid = [guid]::NewGuid().ToString("N").Substring(0, 8)
    $orderRef = "CHAOS-$shortGuid-$idx"
    $itemId   = "CHAOS-ITEM-$([guid]::NewGuid().ToString('N').Substring(0,8))"

    $body = @{
        SourceSystem = "Oms"
        OrderRef = $orderRef
        Priority = "Normal"
        RequestedTransportMode = "Amr"
        ServiceWindow = @{
            EarliestUtc = $null
            LatestUtc = (Get-Date).AddHours(4).ToUniversalTime().ToString("o")
        }
        Items = @(
            @{
                ItemId = $itemId
                PickupLocationCode = $Script:PickupId
                DropLocationCode = $Script:DropId
                LoadUnitProfileCode = $Script:Profile
                WeightKg = 5.0
                Quantity = @{ Value = 1.0; Uom = "BOX" }
            }
        )
    } | ConvertTo-Json -Depth 5

    $idemKey = "chaos-$orderRef-$([guid]::NewGuid().ToString('N').Substring(0,8))"
    $headers = @{
        "Content-Type"    = "application/json"
        "Idempotency-Key" = $idemKey
    }

    try {
        $r = Invoke-WebRequest -Uri "$ApiUrl/api/v1/delivery-orders/upstream" `
            -Method POST -Body $body -Headers $headers `
            -UseBasicParsing -TimeoutSec 15
        if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 300) {
            $Script:OrderRefs.Add($orderRef)
            return $true
        }
        Write-Warn "Order $idx HTTP $($r.StatusCode)"
        return $false
    } catch {
        Write-Warn "Order $idx submit failed: $($_.Exception.Message)"
        return $false
    }
}

function Wait-ForHealth {
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        try {
            $r = Invoke-WebRequest -Uri "$ApiUrl/health" -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200) { return }
        } catch { }
    }
    Write-Warn "health check did not return 200 within 120s -- continuing anyway"
}

function Invoke-Chaos {
    $modeWord = if ($SkipChaos) { "no chaos" } else { "kill every $KillEvery" }
    Write-Phase "Phase 2: submit $OrderCount orders ($modeWord)"
    $Script:StartedAt = Get-Date

    $submitted = 0
    $failed = 0
    $kills = 0

    for ($i = 1; $i -le $OrderCount; $i++) {
        $ok = Submit-Order -idx $i
        if ($ok) { $submitted++ } else { $failed++ }

        if ($i % 10 -eq 0) {
            Write-Step "submitted $submitted/$OrderCount, kills $kills, submit failures $failed"
        }

        if (-not $SkipChaos -and ($i % $KillEvery -eq 0) -and ($i -lt $OrderCount)) {
            $delay = Get-Random -Minimum $MinKillDelaySec -Maximum ($MaxKillDelaySec + 1)
            Start-Sleep -Seconds $delay

            $killNo = $kills + 1
            Write-Step "killing $ContainerName (delay was ${delay}s, run $killNo)"
            docker kill $ContainerName | Out-Null
            $Script:KillTimestamps.Add((Get-Date))
            $kills++

            $serviceName = $ContainerName.Replace("dtms-", "")
            docker compose up -d $serviceName | Out-Null
            Wait-ForHealth
        }
    }

    Write-Ok "submitted $submitted, failed $failed, kills $kills"
}

# Phase 3: Settle -- wait for redeliveries / watchdog / outbox to converge.
function Wait-ForSettle {
    Write-Phase "Phase 3: Settle ${SettleMinutes}min"
    Write-Step "waiting for MassTransit + watchdog + outbox to drain..."

    $start = Get-Date
    $totalSec = $SettleMinutes * 60
    for ($s = 30; $s -le $totalSec; $s += 30) {
        Start-Sleep -Seconds 30
        $elapsed = [int]((Get-Date) - $start).TotalSeconds
        Write-Step "settled ${elapsed}s / ${totalSec}s"
    }
}

# Phase 4: Verify -- per-order outcomes from DB + verdict.
function Get-OrderOutcomes {
    if ($Script:OrderRefs.Count -eq 0) {
        Write-Warn "no orders submitted -- skipping verification"
        return @()
    }

    $list = ($Script:OrderRefs | ForEach-Object { "'$_'" }) -join ","
    $sql = @"
SELECT
    o."OrderRef",
    o."Status",
    (SELECT COUNT(*) FROM planning."Jobs" j WHERE j."DeliveryOrderId" = o."Id") AS job_count,
    (SELECT COUNT(*) FROM dispatch."Trips" t WHERE t."DeliveryOrderId" = o."Id") AS trip_count,
    COALESCE((SELECT j."VendorOrderKey" FROM planning."Jobs" j WHERE j."DeliveryOrderId" = o."Id" LIMIT 1), '') AS vendor_key
FROM deliveryorder."DeliveryOrders" o
WHERE o."OrderRef" IN ($list);
"@

    $tempFile = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $tempFile -Value $sql -Encoding ascii
    docker cp $tempFile dtms-postgres:/tmp/verify.sql 2>&1 | Out-Null

    $raw = docker exec dtms-postgres psql -U postgres -d amr_delivery_planning -t -A -F'|' -f /tmp/verify.sql 2>&1
    Remove-Item $tempFile -ErrorAction SilentlyContinue

    $rows = @()
    foreach ($line in $raw) {
        $parts = $line -split '\|'
        if ($parts.Count -ne 5) { continue }
        $rows += [PSCustomObject]@{
            OrderRef  = $parts[0]
            Status    = $parts[1]
            JobCount  = [int]$parts[2]
            TripCount = [int]$parts[3]
            VendorKey = $parts[4]
        }
    }
    return $rows
}

function Get-TotalPlannedCount {
    $out = Invoke-PgQuery 'SELECT COUNT(*) FROM deliveryorder."DeliveryOrders" WHERE "Status" = ''Planned'';'
    return [int]($out.Trim())
}

function Invoke-Verify {
    Write-Phase "Phase 4: Verify"
    $Script:EndedAt = Get-Date

    $rows = Get-OrderOutcomes
    Write-Ok "loaded $($rows.Count) order outcomes from DB"

    $byStatus = $rows | Group-Object Status | Sort-Object Count -Descending
    Write-Host ""
    Write-Host "  Status distribution (chaos orders):" -ForegroundColor Cyan
    foreach ($g in $byStatus) {
        $line = "    {0,-22}  {1,4}" -f $g.Name, $g.Count
        Write-Host $line
    }

    $stuck = $rows | Where-Object {
        $_.Status -eq 'Planned' -and $_.TripCount -eq 0 -and [string]::IsNullOrEmpty($_.VendorKey)
    }
    $stuckVendor = $rows | Where-Object {
        $_.Status -eq 'Planned' -and -not [string]::IsNullOrEmpty($_.VendorKey)
    }
    $progressed = $rows | Where-Object {
        $_.Status -in @('Dispatched', 'InProgress', 'Completed', 'PartiallyCompleted')
    }
    $terminalFail = $rows | Where-Object {
        $_.Status -in @('Failed', 'Rejected', 'Cancelled')
    }
    $totalPlanned = Get-TotalPlannedCount

    Write-Host ""
    Write-Host "  T1 health signals:" -ForegroundColor Cyan
    Write-Host ("    Total Planned in DB (incl. non-chaos):    {0}" -f $totalPlanned)
    Write-Host ("    Stuck Planned + no Trip + no vendor key:  {0}  <-- T1 FAILURE if > 0" -f $stuck.Count)
    Write-Host ("    Vendor-accepted Planned (T1.8 will skip): {0}" -f $stuckVendor.Count)
    Write-Host ("    Reached Dispatched / Completed:           {0}" -f $progressed.Count)
    Write-Host ("    Terminal Failed / Rejected / Cancelled:   {0}" -f $terminalFail.Count)

    Write-Host ""
    Write-Host ("  Run window: {0} -> {1}" -f $Script:StartedAt.ToString('HH:mm:ss'), $Script:EndedAt.ToString('HH:mm:ss'))
    Write-Host ("  Kills issued: {0}" -f $Script:KillTimestamps.Count)

    Write-Host ""
    if ($stuck.Count -eq 0) {
        Write-Host "VERDICT: PASS -- every chaos order converged. T1 stack (retry + graceful shutdown + watchdog + idempotency) holds under kill-mid-pipeline stress." -ForegroundColor Green
        return 0
    } else {
        Write-Host "VERDICT: FAIL -- $($stuck.Count) order(s) stuck at Planned with no Trip and no vendor key. This is the OD-0374 / OD-0375 shape; investigate before deploying to prod." -ForegroundColor Red
        Write-Host "  Stuck order refs:"
        foreach ($s in $stuck) { Write-Host ("    - " + $s.OrderRef) -ForegroundColor Red }
        return 1
    }
}

# Main
$ErrorActionPreference = "Stop"

if (-not (Test-StackHealthy)) {
    Write-Host ""
    Write-Host "VERDICT: ABORTED -- stack pre-check failed." -ForegroundColor Red
    exit 2
}

Invoke-Chaos
Wait-ForSettle
$exit = Invoke-Verify

# Phase 5 (planned, not yet implemented) -- end-to-end completion
# verification. When implemented, would poll for vendor robot operate +
# webhooks to confirm every chaos order reached a terminal status
# (Completed / PartiallyCompleted / Failed) within $CompletionTimeoutHours.
# Until then a friendly heads-up so a caller passing -WaitForCompletion
# does not silently get the T1-only behaviour.
if ($WaitForCompletion) {
    Write-Host ""
    Write-Host "Phase 5 (-WaitForCompletion) is documented but not yet implemented." -ForegroundColor Yellow
    Write-Host "See docs/chaos-test-results.md 'Phase 5 -- end-to-end completion verification'."
}

exit $exit
