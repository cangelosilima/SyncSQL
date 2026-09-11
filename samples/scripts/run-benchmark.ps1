<#
.SYNOPSIS
    Runs the sample-fleet benchmark: extracts every sample database, builds the
    catalog, and asserts the result against samples/expected/.
.DESCRIPTION
    The databases have to be up first - see setup-databases.ps1.
.PARAMETER Reuse
    Assert against the existing samples/output instead of re-extracting.
.PARAMETER UpdateBaseline
    Re-record samples/expected/baseline.json from this run.
.PARAMETER Filter
    Passed through to `dotnet test --filter`.
.EXAMPLE
    ./run-benchmark.ps1
.EXAMPLE
    ./run-benchmark.ps1 -Reuse
#>
[CmdletBinding()]
param(
    [switch]$Reuse,
    [switch]$UpdateBaseline,
    [string]$Filter,
    [switch]$Gateway,
    [ValidateSet('samples', 'heterogeneous-lineage')]
    [string]$Scenario = 'samples'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Scenario -eq 'heterogeneous-lineage') {
    if ($Reuse -or $UpdateBaseline -or $Filter) {
        throw 'The heterogeneous scenario always provisions and compares its independent contract; Reuse, UpdateBaseline and Filter are not supported.'
    }
    & (Join-Path $PSScriptRoot '../scenarios/heterogeneous-lineage/run.ps1') -Gateway:$Gateway
    return
}
if ($Gateway) { throw 'Gateway applies only to the heterogeneous-lineage scenario.' }

Import-Module (Join-Path $PSScriptRoot 'lib/Common.psm1') -Force

Assert-Command dotnet 'Install the .NET SDK pinned by global.json: https://dotnet.microsoft.com/download'
Import-SampleEnvironment | Out-Null

$repoDir = Get-RepoPath
$env:SYNCSQL_SAMPLES = '1'
if ($Reuse) { $env:SYNCSQL_SAMPLES_REUSE = '1' }
if ($UpdateBaseline) { $env:SYNCSQL_SAMPLES_UPDATE_BASELINE = '1' }

# Publishing the CLI once and pointing the benchmark at it keeps `dotnet run`
# (and its implicit restore) out of the middle of the test run.
$publishDir = Join-Path (Get-CachePath) 'cli'
Write-Note "Publishing the CLI to $publishDir"
& dotnet publish (Join-Path $repoDir 'cli/src/SyncSql.Cli') -c Release -o $publishDir --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
$env:SYNCSQL_CLI = Join-Path $publishDir 'SyncSql.Cli.dll'

Write-Note 'Running the benchmark'
$testArgs = @('test', (Join-Path $repoDir 'cli/tests/SyncSql.Samples.Benchmark.Tests'), '-c', 'Release', '--nologo')
if ($Filter) { $testArgs += @('--filter', $Filter) }
& dotnet @testArgs
if ($LASTEXITCODE -ne 0) { throw "The benchmark failed (dotnet test exited with $LASTEXITCODE)." }
