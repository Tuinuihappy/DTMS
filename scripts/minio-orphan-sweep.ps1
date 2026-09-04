<#
.SYNOPSIS
    Report (and optionally delete) MinIO objects that no database row points at.

.DESCRIPTION
    Two buckets hold image bytes whose only reference is a column in Postgres:

      dtms-pod          pod/{tripId}/{pickup|drop}/{guid}.jpg
                        referenced by transportmanual."ManualTripExtensions"
                        ("PickupPodKey", "DropPodKey")

      dtms-attachments  {carrier|carrier-type|maintenance}/{ownerId}/{guid}[.thumb].jpg
                        referenced by fleet."Attachments"
                        ("ObjectKey", "ThumbnailKey")

    Bytes and rows are written in separate systems and there is no transaction
    across them, so they drift: a delete that removed the row but failed on the
    object, an upload confirmed into storage whose row never committed, a
    restored database snapshot that is older than the bucket. This finds the
    residue.

    DRY RUN BY DEFAULT. Nothing is removed unless you pass -Delete, and -Delete
    prompts before each object because the action is unrecoverable -- there is
    no versioning on these buckets and no backup of the volume.

    Four things are deliberately never deleted, and each is reported under its
    own heading so a skipped object is visible rather than silently absent:

      incoming/     the attachment staging prefix. A MinIO lifecycle rule
                    ('dtms-incoming-expiry', installed by the API on boot)
                    owns it. Two things expiring the same prefix on different
                    clocks is how you delete an upload mid-confirm.

      too new       anything younger than -MinAgeHours. Confirm copies the
                    object to its final key and THEN writes the row, so an
                    in-flight upload looks exactly like an orphan for the
                    width of that gap.

      unknown       any key outside the prefixes listed above. A future
                    feature writing to these buckets must be taught to this
                    script before the script is allowed an opinion on it.

      referenced    the point of the exercise.

.PARAMETER Delete
    Actually remove the orphans. Without it the script only reports.
    Honours -WhatIf and -Confirm.

.PARAMETER MinAgeHours
    Objects younger than this are left alone. Default 24. The floor is the
    upload-confirm window, which is seconds; 24h is slack for a human who is
    mid-investigation.

.PARAMETER Bucket
    'all' (default), 'pod', or 'attachments'.

.EXAMPLE
    .\scripts\minio-orphan-sweep.ps1
    Report only. Safe to run any time.

.EXAMPLE
    .\scripts\minio-orphan-sweep.ps1 -Delete -WhatIf
    Show exactly which objects -Delete would remove, without removing them.

.EXAMPLE
    .\scripts\minio-orphan-sweep.ps1 -Bucket attachments -Delete
    Sweep one bucket, prompting per object.

.NOTES
    Run from PowerShell, not Git Bash -- MSYS rewrites the alias/bucket/key
    argument into a Windows path and mc then reports the bucket as missing.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$Delete,
    [ValidateRange(0, 100000)]
    [int]$MinAgeHours = 24,
    [ValidateSet('all', 'pod', 'attachments')]
    [string]$Bucket = 'all',
    [string]$MinioContainer = 'dtms-minio',
    [string]$MinioAlias = 'local',
    [string]$PodBucketName = 'dtms-pod',
    [string]$AttachmentBucketName = 'dtms-attachments',
    [string]$PostgresContainer = 'dtms-postgres',
    [string]$PostgresUser = 'postgres',
    [string]$Database = 'amr_delivery_planning'
)

$ErrorActionPreference = 'Stop'

# Prefixes this script understands. Anything else in the bucket is reported
# and left alone -- see the 'unknown' note in the help above.
$KnownPrefixes = @{
    $PodBucketName        = @('pod/')
    $AttachmentBucketName = @('carrier/', 'carrier-type/', 'maintenance/')
}

# The staging prefix is the lifecycle rule's territory, not ours.
$StagingPrefix = 'incoming/'

function Invoke-Sql {
    <#
        Piped through stdin rather than -c: the identifiers are quoted
        PascalCase ("ObjectKey"), and those quotes do not survive the trip
        through PowerShell -> docker -> sh intact. Postgres then lowercases
        them and the query fails on a column that "does not exist".
    #>
    param([Parameter(Mandatory)][string]$Sql)

    $out = $Sql | docker exec -i $PostgresContainer psql -U $PostgresUser -d $Database -A -t 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed (exit $LASTEXITCODE): $($out -join [Environment]::NewLine)"
    }
    # An empty result is legitimate (no rows reference anything yet), so it
    # must not be confused with the failure above -- which is exactly why the
    # exit code is checked before the output is looked at.
    @($out | Where-Object { $_ -ne $null -and $_.ToString().Trim() -ne '' } | ForEach-Object { $_.ToString().Trim() })
}

function Get-MinioObject {
    param([Parameter(Mandatory)][string]$BucketName)

    $lines = docker exec $MinioContainer mc ls --recursive --json "$MinioAlias/$BucketName" 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "mc ls failed on '$BucketName' (exit $LASTEXITCODE): $($lines -join [Environment]::NewLine)"
    }

    $results = @()
    foreach ($line in $lines) {
        $text = "$line".Trim()
        if ($text -eq '') { continue }

        $row = $null
        try { $row = $text | ConvertFrom-Json } catch {
            throw "mc emitted a line that is not JSON, refusing to guess what is in the bucket: $text"
        }

        # mc reports per-entry failures inline with status 'error'. Treating
        # one as "no such object" would let a listing that only half worked
        # look like a bucket full of orphans.
        if ($row.status -ne 'success') {
            throw "mc reported an error listing '$BucketName': $text"
        }
        if ($row.type -ne 'file') { continue }

        $results += [pscustomobject]@{
            Key          = $row.key
            Size         = [long]$row.size
            LastModified = [datetime]$row.lastModified
        }
    }
    , $results
}

function Get-ReferencedKey {
    param([Parameter(Mandatory)][string]$BucketName)

    if ($BucketName -eq $PodBucketName) {
        $sql = @'
select "PickupPodKey" from transportmanual."ManualTripExtensions" where "PickupPodKey" is not null
union
select "DropPodKey"   from transportmanual."ManualTripExtensions" where "DropPodKey"   is not null;
'@
    }
    elseif ($BucketName -eq $AttachmentBucketName) {
        # ThumbnailKey is nullable; ObjectKey is not. Both are swept, so both
        # have to be collected or every thumbnail reads as an orphan.
        $sql = @'
select "ObjectKey"    from fleet."Attachments" where "ObjectKey"    is not null
union
select "ThumbnailKey" from fleet."Attachments" where "ThumbnailKey" is not null;
'@
    }
    else {
        throw "No reference query is defined for bucket '$BucketName'."
    }

    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($key in (Invoke-Sql -Sql $sql)) { [void]$set.Add($key) }
    , $set
}

function Invoke-BucketSweep {
    param([Parameter(Mandatory)][string]$BucketName)

    Write-Host ''
    Write-Host "=== $BucketName ===" -ForegroundColor Cyan

    $objects = Get-MinioObject -BucketName $BucketName
    $referenced = Get-ReferencedKey -BucketName $BucketName
    $prefixes = $KnownPrefixes[$BucketName]
    $cutoff = (Get-Date).ToUniversalTime().AddHours(-$MinAgeHours)

    $orphans = @()
    $buckets = @{ referenced = @(); staging = @(); unknown = @(); tooNew = @() }

    foreach ($obj in $objects) {
        if ($obj.Key.StartsWith($StagingPrefix, [StringComparison]::Ordinal)) {
            $buckets.staging += $obj; continue
        }
        $known = $false
        foreach ($p in $prefixes) {
            if ($obj.Key.StartsWith($p, [StringComparison]::Ordinal)) { $known = $true; break }
        }
        if (-not $known) { $buckets.unknown += $obj; continue }
        if ($referenced.Contains($obj.Key)) { $buckets.referenced += $obj; continue }
        if ($obj.LastModified.ToUniversalTime() -gt $cutoff) { $buckets.tooNew += $obj; continue }

        $orphans += $obj
    }

    Write-Host ("  objects in bucket : {0}" -f $objects.Count)
    Write-Host ("  referenced by a row: {0}" -f $buckets.referenced.Count)
    Write-Host ("  staging ({0}), lifecycle-owned: {1}" -f $StagingPrefix, $buckets.staging.Count)
    Write-Host ("  younger than {0}h  : {1}" -f $MinAgeHours, $buckets.tooNew.Count)

    if ($buckets.unknown.Count -gt 0) {
        Write-Host ("  unknown prefix    : {0}  (NOT swept -- teach this script about them first)" -f $buckets.unknown.Count) -ForegroundColor Yellow
        foreach ($o in $buckets.unknown) { Write-Host "      ? $($o.Key)" -ForegroundColor Yellow }
    }

    if ($orphans.Count -eq 0) {
        Write-Host "  orphans           : 0" -ForegroundColor Green
        return [pscustomobject]@{ Bucket = $BucketName; Orphans = 0; Bytes = 0; Deleted = 0 }
    }

    $bytes = ($orphans | Measure-Object -Property Size -Sum).Sum
    Write-Host ("  orphans           : {0}  ({1:N1} KiB)" -f $orphans.Count, ($bytes / 1KB)) -ForegroundColor Yellow

    # A sweep that would take the whole bucket is either a genuine backlog or a
    # database pointed at the wrong environment. The script cannot tell which,
    # so it says so loudly and still leaves the decision with the operator.
    $sweepable = $orphans.Count + $buckets.referenced.Count
    if ($sweepable -gt 0 -and $orphans.Count -eq $sweepable) {
        Write-Host "  NOTE: every referenceable object in this bucket is unreferenced." -ForegroundColor Red
        Write-Host "        Confirm '$Database' on '$PostgresContainer' is the right database before deleting." -ForegroundColor Red
    }

    foreach ($o in $orphans | Sort-Object Key) {
        Write-Host ("      - {0}  {1,8:N0} B  {2:yyyy-MM-dd HH:mm} UTC" -f $o.Key, $o.Size, $o.LastModified.ToUniversalTime())
    }

    if (-not $Delete) {
        Write-Host "  (dry run -- pass -Delete to remove them)" -ForegroundColor DarkGray
        return [pscustomobject]@{ Bucket = $BucketName; Orphans = $orphans.Count; Bytes = $bytes; Deleted = 0 }
    }

    $deleted = 0
    foreach ($o in $orphans) {
        $target = "$MinioAlias/$BucketName/$($o.Key)"
        if ($PSCmdlet.ShouldProcess($target, 'mc rm (permanent, no versioning, no backup)')) {
            $out = docker exec $MinioContainer mc rm $target 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "failed to remove $($o.Key): $($out -join ' ')"
            }
            else {
                $deleted++
            }
        }
    }
    Write-Host ("  deleted           : {0}" -f $deleted) -ForegroundColor Green

    [pscustomobject]@{ Bucket = $BucketName; Orphans = $orphans.Count; Bytes = $bytes; Deleted = $deleted }
}

$targets = switch ($Bucket) {
    'pod' { @($PodBucketName) }
    'attachments' { @($AttachmentBucketName) }
    default { @($PodBucketName, $AttachmentBucketName) }
}

Write-Host "MinIO orphan sweep" -ForegroundColor Cyan
Write-Host ("  mode      : {0}" -f $(if ($Delete) { 'DELETE' } else { 'dry run' }))
Write-Host ("  min age   : {0}h" -f $MinAgeHours)
Write-Host ("  storage   : {0} ({1})" -f $MinioContainer, $MinioAlias)
Write-Host ("  database  : {0} on {1}" -f $Database, $PostgresContainer)

$summary = foreach ($b in $targets) { Invoke-BucketSweep -BucketName $b }

Write-Host ''
$summary | Format-Table -AutoSize
