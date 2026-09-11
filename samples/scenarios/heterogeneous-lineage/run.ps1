[CmdletBinding()]
param(
    [switch]$Offline,
    [switch]$Down,
    [switch]$Gateway
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$project = Join-Path $repoRoot 'cli/tests/SyncSql.Samples.Benchmark.Tests'
$envFile = Join-Path $PSScriptRoot '.env'
if (-not (Test-Path -LiteralPath $envFile)) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.env.example') -Destination $envFile
}
$composeArgs = @('compose', '--env-file', $envFile, '-f', (Join-Path $PSScriptRoot 'compose.yml'))
if ($Gateway -or $Down) {
    $composeArgs += @('-f', (Join-Path $PSScriptRoot 'compose.gateway.yml'))
}
if ($Down) {
    # Fixed Compose project; deliberately retains the benchmark data volumes.
    & docker @composeArgs down
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose down failed.' }
    return
}
if ($Offline) {
    & dotnet test $project --filter 'FullyQualifiedName~HeterogeneousLineageTests'
    if ($LASTEXITCODE -ne 0) { throw 'Offline contract tests failed.' }
    return
}
$previousPassword = $env:BENCH_PASSWORD
$previousEnabled = $env:SYNCSQL_HETEROGENEOUS
$previousCli = $env:SYNCSQL_CLI
$previousGateway = $env:SYNCSQL_HETEROGENEOUS_GATEWAY
try {
    $passwordLine = Get-Content -LiteralPath $envFile | Where-Object { $_ -match '^BENCH_PASSWORD=' } | Select-Object -First 1
    if (-not $passwordLine) { throw 'BENCH_PASSWORD is missing from the scenario .env.' }
    $env:BENCH_PASSWORD = $passwordLine.Substring('BENCH_PASSWORD='.Length)
    if ($Gateway) {
        if ($env:ORACLE_GATEWAY_IMAGE) {
            & docker image inspect $env:ORACLE_GATEWAY_IMAGE --format '{{.Id}}'
            if ($LASTEXITCODE -ne 0) { throw 'Pull or build ORACLE_GATEWAY_IMAGE before running this benchmark.' }
        }
        else {
            $media = Join-Path $PSScriptRoot 'gateway/.cache/LINUX.X64_193000_gateways.zip'
            if (-not (Test-Path -LiteralPath $media -PathType Leaf)) {
                throw "Oracle gateway installer missing: $media. Download Gateways 19.3 from Oracle, or set ORACLE_GATEWAY_IMAGE to an installed dg4msql image. See gateway/README.md."
            }
            & docker @composeArgs build gateway
            if ($LASTEXITCODE -ne 0) { throw 'Oracle gateway image build failed.' }
        }
    }
    & docker @composeArgs up -d --no-build --wait --wait-timeout 900
    if ($LASTEXITCODE -ne 0) { throw 'Database containers did not become healthy.' }
    if ($Gateway) {
        & docker @composeArgs exec -T helios bash /opt/syncsql/gateway/configure.sh
        if ($LASTEXITCODE -ne 0) { throw 'Oracle gateway TNS configuration failed.' }
    }
    $cliDir = Join-Path $PSScriptRoot '.cache/cli'
    & dotnet publish (Join-Path $repoRoot 'cli/src/SyncSql.Cli') -c Release -o $cliDir --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
    $env:SYNCSQL_CLI = Join-Path $cliDir 'SyncSql.Cli.dll'
    $env:SYNCSQL_HETEROGENEOUS = '1'
    $env:SYNCSQL_HETEROGENEOUS_GATEWAY = if ($Gateway) { '1' } else { '0' }
    & dotnet test $project -c Release --filter 'FullyQualifiedName~Heterogeneous' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=heterogeneous.trx'
    if ($LASTEXITCODE -ne 0) { throw 'Heterogeneous benchmark failed. See .cache/runs for diagnostics.' }
}
finally {
    $env:BENCH_PASSWORD = $previousPassword
    $env:SYNCSQL_HETEROGENEOUS = $previousEnabled
    $env:SYNCSQL_CLI = $previousCli
    $env:SYNCSQL_HETEROGENEOUS_GATEWAY = $previousGateway
}
