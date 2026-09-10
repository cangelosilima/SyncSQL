<#
.SYNOPSIS
    Brings up the sample database fleet - one SQL Server and one Oracle container,
    loaded with the samples described in samples/samples.json.
.DESCRIPTION
    The Windows/PowerShell twin of setup-databases.sh. Re-running is safe: sources
    are updated rather than re-cloned, backups already downloaded are kept, and
    every sample either restores over itself or drops what it is about to create.
.PARAMETER Engine
    Which engine to provision. Default: all.
.PARAMETER Tier
    Which tier to install. Default: standard. 'heavy' covers the samples that need
    a very large download or hours of row-by-row inserts.
.PARAMETER Only
    Provision these sample ids and their prerequisites (ignores -Tier).
.PARAMETER Skip
    Skip these sample ids.
.PARAMETER SkipFetch
    Reuse samples/.cache as-is; do not fetch or download.
.PARAMETER Status
    Report container and sample state, then exit.
.PARAMETER Down
    Stop the containers and delete their volumes, then exit.
.EXAMPLE
    ./setup-databases.ps1
.EXAMPLE
    ./setup-databases.ps1 -Tier all
.EXAMPLE
    ./setup-databases.ps1 -Only human-resources,sql-graph
#>
[CmdletBinding()]
param(
    [ValidateSet('mssql', 'oracle', 'all')][string]$Engine = 'all',
    [ValidateSet('standard', 'heavy', 'all')][string]$Tier = 'standard',
    [string[]]$Only = @(),
    [string[]]$Skip = @(),
    [switch]$SkipFetch,
    [switch]$Status,
    [switch]$Down
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'lib/Common.psm1') -Force

Assert-Prerequisites
$settings = Import-SampleEnvironment
$manifest = Get-SampleManifest

if ($Down) {
    Write-Note 'Tearing down the sample fleet'
    Invoke-Compose down --volumes --remove-orphans
    Write-Ok 'Containers and volumes removed. samples/.cache is untouched - delete it by hand to reclaim the downloads.'
    return
}

if ($Status) {
    Write-Note 'Containers'
    Invoke-Compose ps
    Write-Note 'Samples'
    foreach ($sample in $manifest.samples | Sort-Object order) {
        '  {0,-7} {1,-12} {2}' -f $sample.engine, $sample.tier, $sample.id | Write-Host
    }
    return
}

$selected = @(Select-Samples -Manifest $manifest -Engine $Engine -Tier $Tier -Only $Only -Skip $Skip)
if ($selected.Count -eq 0) {
    throw "No samples matched -Engine $Engine -Tier $Tier."
}

Write-Note "Provisioning $($selected.Count) sample(s): $($selected.id -join ', ')"

# The selection above pulls in each sample's declared prerequisites, so this only
# fires when -Skip removed one on purpose - in which case the dependent sample is
# about to install against a base database nobody restored.
foreach ($unmet in Get-SkippedRequirement -Manifest $manifest -Engine $Engine -Tier $Tier -Only $Only -Skip $Skip) {
    Write-Warn "$unmet, which -Skip excluded - it will install against whatever is already there"
}

$needsMssql = [bool]($selected | Where-Object { $_.engine -eq 'mssql' })
$needsOracle = [bool]($selected | Where-Object { $_.engine -eq 'oracle' })

New-Item -ItemType Directory -Force -Path (Get-SourcesPath), (Get-BackupsPath) | Out-Null

if ($SkipFetch) {
    Write-Note 'Skipping fetch (-SkipFetch)'
}
else {
    Write-Note 'Fetching upstream sources'
    Assert-Command git
    if ($needsMssql)  { Sync-UpstreamSource -Manifest $manifest -Name 'sql-server-samples' }
    if ($needsOracle) { Sync-UpstreamSource -Manifest $manifest -Name 'db-sample-schemas' }

    if ($needsMssql) {
        Write-Note 'Downloading sample database backups'
        foreach ($sample in $selected | Where-Object { $_.provision.type -eq 'mssql-restore' }) {
            foreach ($restore in $sample.provision.restores) {
                Save-SampleBackup -Manifest $manifest -Name $restore.backup
            }
        }
    }
}

Write-Note 'Starting containers'
$services = @()
if ($needsMssql)  { $services += 'mssql' }
if ($needsOracle) { $services += 'oracle' }
Invoke-Compose up -d @services

if ($needsMssql)  { Wait-Mssql -Port $settings['MSSQL_PORT'] }
if ($needsOracle) { Wait-Oracle -Port $settings['ORACLE_PORT'] }

$installed = @()
$failed = @()

foreach ($sample in $selected) {
    Write-Note "$($sample.id) ($($sample.title))"
    try {
        switch ($sample.provision.type) {
            'mssql-restore' {
                foreach ($restore in $sample.provision.restores) {
                    Restore-MssqlBackup -Backup $restore.backup -Database $restore.database
                }
            }
            'mssql-scripts' {
                $database = $sample.provision.database
                New-MssqlDatabase -Database $database
                foreach ($script in $sample.provision.scripts) {
                    $relative = if ($script.from -eq 'local') {
                        "$($sample.engine)/$($sample.id)/$($script.path)"
                    }
                    else {
                        ".cache/sources/sql-server-samples/$($sample.upstream.path)/$($script.path)"
                    }
                    Write-Step $script.path
                    Invoke-MssqlFile -RelativePath $relative -Database $database
                }
            }
            'oracle-install-script' {
                Write-Step "$($sample.provision.workdir)/$($sample.provision.script)"
                Invoke-OracleInstallScript `
                    -WorkDir $sample.provision.workdir `
                    -Script $sample.provision.script `
                    -SystemPassword $settings['ORACLE_PASSWORD'] `
                    -SchemaPassword $settings['ORACLE_SAMPLE_PASSWORD']
            }
            default { throw "Sample '$($sample.id)' has unsupported provision type '$($sample.provision.type)'." }
        }
        Write-Ok "$($sample.id) installed"
        $installed += $sample.id
    }
    catch {
        Write-Warn "$($sample.id) failed - continuing with the rest: $_"
        $failed += $sample.id
    }
}

Write-Note 'Done'
Write-Host "    installed: $(if ($installed) { $installed -join ', ' } else { '(none)' })"
if ($failed) { Write-Host "    failed:    $($failed -join ', ')" -ForegroundColor Red }
Write-Host @"

    SQL Server  localhost,$($settings['MSSQL_PORT'])   user 'sa'      (password: samples/.env MSSQL_SA_PASSWORD)
    Oracle      localhost:$($settings['ORACLE_PORT'])/FREEPDB1  user 'SYSTEM'  (password: samples/.env ORACLE_PASSWORD)

    Next: samples/scripts/run-syncsql.ps1
"@

if ($failed) { exit 1 }
