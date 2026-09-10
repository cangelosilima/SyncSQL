<#
.SYNOPSIS
    Runs the whole syncsql pipeline against the sample fleet, writing everything
    under samples/output/.
.DESCRIPTION
    The Windows/PowerShell twin of run-syncsql.sh. The four steps are the ones a
    real pipeline runs, in the same order:

        validate-config -> sync -> metrics update -> catalog build -> lint

    No git is involved: `sync` only writes local files, and `catalog build` runs
    without -RepoRoot, so the history and heatmap parts of catalog.json stay empty.
.PARAMETER Config
    SyncSQL config. Default: samples/config/servers.samples.json
.PARAMETER OutputRoot
    Where the object tree, metrics and catalog.json go. Default: samples/output
.PARAMETER ServerInclude
    Only extract servers matching these regexes.
.PARAMETER ServerExclude
    Skip servers matching these regexes.
.PARAMETER Clean
    Delete the output root before extracting.
.PARAMETER SkipLint
    Do not run the T-SQL lint pass at the end.
.PARAMETER NoBuild
    Assume the CLI is already built.
.EXAMPLE
    ./run-syncsql.ps1
.EXAMPLE
    ./run-syncsql.ps1 -ServerInclude '^SAMPLES-ORACLE$'
#>
[CmdletBinding()]
param(
    [string]$Config,
    [string]$OutputRoot,
    [string[]]$ServerInclude = @(),
    [string[]]$ServerExclude = @(),
    [switch]$Clean,
    [switch]$SkipLint,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'lib/Common.psm1') -Force

Assert-Command dotnet 'Install the .NET SDK pinned by global.json: https://dotnet.microsoft.com/download'
$settings = Import-SampleEnvironment

$samplesDir = Get-SamplesPath
$repoDir = Get-RepoPath
if (-not $Config)     { $Config = Join-Path $samplesDir 'config/servers.samples.json' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $samplesDir 'output' }

if (-not (Test-Path $Config)) { throw "Config not found: $Config" }

# Credentials never go on the command line (other processes can read argv) and
# never into the config; this file lives under the git-ignored cache.
$credentials = Join-Path (Get-CachePath) 'credentials.json'
New-Item -ItemType Directory -Force -Path (Get-CachePath) | Out-Null
@{
    SAMPLES_MSSQL  = @{ user = 'sa';     password = $settings['MSSQL_SA_PASSWORD'] }
    SAMPLES_ORACLE = @{ user = 'SYSTEM'; password = $settings['ORACLE_PASSWORD'] }
} | ConvertTo-Json | Set-Content -Path $credentials -Encoding UTF8

if ($Clean -and (Test-Path $OutputRoot)) {
    Write-Note "Clearing $OutputRoot"
    Remove-Item -Recurse -Force $OutputRoot
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$cliProject = Join-Path $repoDir 'cli/src/SyncSql.Cli'
if (-not $NoBuild) {
    Write-Note 'Building the CLI'
    & dotnet build $cliProject -c Release --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
}

function Invoke-SyncSql {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    & dotnet run --project $cliProject -c Release --no-build --nologo -- @Arguments
    if ($LASTEXITCODE -ne 0) { throw "syncsql $($Arguments[0]) exited with $LASTEXITCODE." }
}

Write-Note 'validate-config'
Invoke-SyncSql validate-config --config $Config

$syncArgs = @('sync', '--config', $Config, '--credentials-file', $credentials, '--output-root', $OutputRoot)
foreach ($pattern in $ServerInclude) { $syncArgs += @('--server-include', $pattern) }
foreach ($pattern in $ServerExclude) { $syncArgs += @('--server-exclude', $pattern) }

Write-Note 'sync'
Invoke-SyncSql @syncArgs

Write-Note 'metrics update'
Invoke-SyncSql metrics update --output-root $OutputRoot

Write-Note 'catalog build'
Invoke-SyncSql catalog build --output-root $OutputRoot --metrics-root (Join-Path $OutputRoot 'metrics')

if ($SkipLint) {
    Write-Step 'lint skipped (-SkipLint)'
}
else {
    # Findings are informational here: these are third-party sample scripts, and
    # SELECT */NOLOCK/cursor hits in them are exactly what the demo is meant to
    # show. Only a parse error should be loud.
    Write-Note 'lint'
    Invoke-SyncSql lint --output-root $OutputRoot --fail-on error
}

Write-Note 'Done'
$objects = (Get-ChildItem -Path $OutputRoot -Filter *.sql -Recurse -File).Count
Write-Host @"
    objects extracted : $objects
    catalog           : $(Join-Path $OutputRoot 'catalog.json')
    metrics history   : $(Join-Path $OutputRoot 'metrics')

    To browse it: copy the catalog into site/public/ and run 'npm run dev' in site/.
"@
