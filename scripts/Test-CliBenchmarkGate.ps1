[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('syncsql-benchmark-gate-' + [guid]::NewGuid().ToString('N'))
$artifacts = Join-Path $fixtureRoot 'artifacts'
$budgetPath = Join-Path $fixtureRoot 'budgets.json'
$reportPath = Join-Path $artifacts 'results/fixture-report-full.json'
$retentionPath = Join-Path $artifacts 'retention/synthetic-64.json'
New-Item -ItemType Directory -Path (Split-Path $reportPath), (Split-Path $retentionPath) -Force | Out-Null

function Write-ValidFixture {
    @{
        Version = 1
        Benchmarks = @(@{ FullName = 'Fixture.Build'; MaxMeanMilliseconds = 100; MaxAllocatedBytes = 1000 })
        Retention = @(@{ NodeCount = 64; MaxRetainedAnalysisBytes = 1000 })
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $budgetPath
    @{
        HostEnvironmentInfo = @{ Configuration = 'Release' }
        Benchmarks = @(@{
            FullName = 'Fixture.Build'
            Statistics = @{ N = 3; Mean = 100000000 }
            Memory = @{ BytesAllocatedPerOperation = 1000 }
        })
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath
    @{ NodeCount = 64; ColumnReferencesPerObject = 8193; PeakRetainedAnalysisBytes = 1000; CompletedBuilds = 3 } |
        ConvertTo-Json | Set-Content -LiteralPath $retentionPath
}

function Assert-Rejected([string] $Label, [scriptblock] $Change) {
    Write-ValidFixture
    & $Change
    $rejected = $false
    try { & "$PSScriptRoot/Assert-CliBenchmarkBudget.ps1" -ArtifactsPath $artifacts -BudgetPath $budgetPath *> $null }
    catch { $rejected = $true }
    if (-not $rejected) { throw "Performance gate accepted $Label." }
}

try {
    Write-ValidFixture
    & "$PSScriptRoot/Assert-CliBenchmarkBudget.ps1" -ArtifactsPath $artifacts -BudgetPath $budgetPath
    if (-not (Test-Path -LiteralPath (Join-Path $artifacts 'budget-summary.md'))) { throw 'Missing budget summary.' }

    foreach ($case in @('runtime regression', 'allocation regression', 'missing allocation', 'missing statistics', 'too few samples', 'string metric', 'negative metric', 'non-Release', 'missing case', 'unexpected case', 'duplicate case')) {
        Assert-Rejected $case {
            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
            switch ($case) {
                'runtime regression' { $report.Benchmarks[0].Statistics.Mean++ }
                'allocation regression' { $report.Benchmarks[0].Memory.BytesAllocatedPerOperation++ }
                'missing allocation' { $report.Benchmarks[0].PSObject.Properties.Remove('Memory') }
                'missing statistics' { $report.Benchmarks[0].Statistics = $null }
                'too few samples' { $report.Benchmarks[0].Statistics.N = 1 }
                'string metric' { $report.Benchmarks[0].Statistics.Mean = '100' }
                'negative metric' { $report.Benchmarks[0].Memory.BytesAllocatedPerOperation = -1 }
                'non-Release' { $report.HostEnvironmentInfo.Configuration = 'Debug' }
                'missing case' { $report.Benchmarks = @() }
                'unexpected case' { $report.Benchmarks += @{ FullName = 'Unexpected' } }
                'duplicate case' { $report.Benchmarks += $report.Benchmarks[0] }
            }
            $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath
        }
    }
    foreach ($case in @('retention regression', 'no builds', 'wrong workload', 'missing retention', 'wrong node count', 'string retention')) {
        Assert-Rejected $case {
            $sample = Get-Content -LiteralPath $retentionPath -Raw | ConvertFrom-Json
            switch ($case) {
                'retention regression' { $sample.PeakRetainedAnalysisBytes++ }
                'no builds' { $sample.CompletedBuilds = 0 }
                'wrong workload' { $sample.ColumnReferencesPerObject = 1 }
                'missing retention' { $sample.PSObject.Properties.Remove('PeakRetainedAnalysisBytes') }
                'wrong node count' { $sample.NodeCount = 256 }
                'string retention' { $sample.PeakRetainedAnalysisBytes = '0' }
            }
            $sample | ConvertTo-Json | Set-Content -LiteralPath $retentionPath
        }
    }
    Assert-Rejected 'absent reports' { Remove-Item -LiteralPath $reportPath }
    Assert-Rejected 'absent retention report' { Remove-Item -LiteralPath $retentionPath }
    Assert-Rejected 'malformed JSON' { '{broken' | Set-Content -LiteralPath $reportPath }
    Assert-Rejected 'duplicate budgets' {
        $budget = Get-Content -LiteralPath $budgetPath -Raw | ConvertFrom-Json
        $budget.Benchmarks += $budget.Benchmarks[0]
        $budget | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $budgetPath
    }
    Assert-Rejected 'empty budgets' { '{"Version":1,"Benchmarks":[],"Retention":[]}' | Set-Content -LiteralPath $budgetPath }
    Write-ValidFixture
    $rejected = $false
    try { & "$PSScriptRoot/Run-CliBenchmarks.ps1" -ArtifactsPath $artifacts *> $null }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Runner accepted a stale artifacts directory.' }
    Write-Host 'Performance gate regression checks passed.'
}
finally {
    # This directory was created by this test under the system temporary directory.
    $resolved = [System.IO.Path]::GetFullPath($fixtureRoot)
    $expectedRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($expectedRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        [System.IO.Path]::GetFileName($resolved) -notlike 'syncsql-benchmark-gate-*') { throw 'Unexpected test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
