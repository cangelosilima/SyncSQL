[CmdletBinding()]
param([string] $ArtifactsPath = "$PSScriptRoot/../TestResults/performance")

$ErrorActionPreference = 'Stop'
$artifacts = [System.IO.Path]::GetFullPath($ArtifactsPath)
# Never let reports from a previous run satisfy a failed or filtered benchmark run.
if ((Test-Path -LiteralPath $artifacts) -and @(Get-ChildItem -LiteralPath $artifacts -Force).Count -gt 0) {
    throw "Benchmark artifacts directory must be empty. Choose a new -ArtifactsPath: $artifacts"
}
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$previousArtifacts = $env:SYNCSQL_BENCHMARK_ARTIFACTS
try {
    $env:SYNCSQL_BENCHMARK_ARTIFACTS = $artifacts
    & dotnet run --project "$PSScriptRoot/../cli/benchmarks/SyncSql.Benchmarks/SyncSql.Benchmarks.csproj" --configuration Release -- --filter '*'
    if ($LASTEXITCODE -ne 0) { throw "BenchmarkDotNet failed with exit code $LASTEXITCODE. See $artifacts." }
    & "$PSScriptRoot/Assert-CliBenchmarkBudget.ps1" -ArtifactsPath $artifacts
}
finally {
    $env:SYNCSQL_BENCHMARK_ARTIFACTS = $previousArtifacts
}
