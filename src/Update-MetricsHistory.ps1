#Requires -Version 5.1
<#
.SYNOPSIS
    Appends this run's freshly-captured metrics snapshots (row counts,
    index fragmentation/usage, optimizer statistics - see
    Get-SyncSqlMsSqlMetricsSnapshot / Get-SyncSqlOracleMetricsSnapshot)
    into a growing per-object history array.

.DESCRIPTION
    This is the piece that keeps volatile, daily-changing operational data
    out of an object's own version-controlled DDL file: -SnapshotRoot holds
    only *this run's* snapshots (one JSON object per table, same relative
    path/id as the object's own .sql file), while -HistoryRoot is a
    separate tree - kept outside config.git.pathPrefix, so
    Publish-SyncSqlToGit's wipe-and-replace of the object tree never
    touches it - holding one growing JSON *array* file per table. Each run
    reads the existing array (if any), appends the new snapshot, trims to
    -HistoryLimit entries (oldest first), and writes it back. The object's
    own file never changes because of this; the history file is *expected*
    to change every run - that's the point.

    Objects dropped from the source database keep their existing history
    file (not cleaned up) - a minor storage cost, not a correctness issue,
    and avoids needing to cross-reference the full current object list here.

.PARAMETER SnapshotRoot
    Root of this run's freshly captured snapshot tree (Export-DatabaseObjects.ps1's
    -MetricsRoot passed through to the extraction backends).

.PARAMETER HistoryRoot
    Root of the accumulating history tree, e.g. <target-repo-checkout>/metrics.

.PARAMETER HistoryLimit
    Maximum snapshots retained per object; oldest are trimmed first.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SnapshotRoot,
    [Parameter(Mandatory)][string]$HistoryRoot,
    [int]$HistoryLimit = 90
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Get-Module -Name SyncSql.Common)) {
    Import-Module (Join-Path $PSScriptRoot 'modules/SyncSql.Common.psm1')
}

if (-not (Test-Path -LiteralPath $SnapshotRoot)) {
    Write-SyncSqlLog "Snapshot root '$SnapshotRoot' not found; nothing to update." -Level WARN
    return
}

$files = @(Get-ChildItem -LiteralPath $SnapshotRoot -Recurse -File -Filter '*.json')
Write-SyncSqlLog "Updating metrics history for $($files.Count) object(s) under $HistoryRoot (retaining up to $HistoryLimit snapshot(s) each)"

New-Item -ItemType Directory -Path $HistoryRoot -Force | Out-Null

$updated = 0
foreach ($file in $files) {
    $relative = Get-SyncSqlRelativePath -Root $SnapshotRoot -FullPath $file.FullName
    $historyPath = Join-Path $HistoryRoot $relative

    $existing = @()
    if (Test-Path -LiteralPath $historyPath) {
        try {
            $raw = Get-Content -LiteralPath $historyPath -Raw
            if (-not [string]::IsNullOrWhiteSpace($raw)) {
                $existing = @(ConvertFrom-Json -InputObject $raw)
            }
        }
        catch {
            Write-SyncSqlLog "Existing metrics history at '$historyPath' could not be parsed (starting fresh): $($_.Exception.Message)" -Level WARN
            $existing = @()
        }
    }

    try {
        $newSnapshot = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $file.FullName -Raw)
    }
    catch {
        Write-SyncSqlLog "Snapshot file '$($file.FullName)' could not be parsed (skipping): $($_.Exception.Message)" -Level WARN
        continue
    }

    $combined = @($existing) + @($newSnapshot)
    if ($combined.Count -gt $HistoryLimit) {
        $combined = $combined[($combined.Count - $HistoryLimit)..($combined.Count - 1)]
    }

    $historyDir = Split-Path -Parent $historyPath
    New-Item -ItemType Directory -Path $historyDir -Force | Out-Null

    # ConvertTo-Json collapses a 0- or 1-element array back to a scalar/empty
    # object unless told otherwise, and -AsArray (the fix) is PS6.2+ only -
    # so build the JSON array manually, which is array-shaped regardless of
    # $combined.Count on every PowerShell version.
    $elements = @($combined | ForEach-Object { $_ | ConvertTo-Json -Depth 10 -Compress })
    $json = '[' + ($elements -join ',') + ']'
    Set-SyncSqlUtf8NoBomContent -Path $historyPath -Content $json
    $updated++
}

Write-SyncSqlLog "Metrics history updated for $updated object(s) under $HistoryRoot"
