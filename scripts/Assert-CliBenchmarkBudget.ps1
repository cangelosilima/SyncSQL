[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactsPath,
    [string] $BudgetPath = "$PSScriptRoot/../cli/benchmarks/budgets.json"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Number($Value, [string] $Name, [double] $Minimum) {
    if (($Value -isnot [int] -and $Value -isnot [long] -and $Value -isnot [double] -and $Value -isnot [decimal]) -or
        [double]::IsNaN($Value) -or [double]::IsInfinity($Value) -or $Value -lt $Minimum) {
        throw "Invalid or missing numeric metric: $Name."
    }
}

$budget = Get-Content -LiteralPath $BudgetPath -Raw | ConvertFrom-Json
if ($budget.Version -ne 1 -or $budget.Benchmarks.Count -eq 0 -or $budget.Retention.Count -eq 0) {
    throw 'Benchmark budgets are empty or have an unsupported version.'
}

$reports = @(Get-ChildItem -LiteralPath (Join-Path $ArtifactsPath 'results') -Filter '*-report-full.json' -File)
if ($reports.Count -eq 0) { throw 'No BenchmarkDotNet full JSON reports were produced.' }
$actual = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
foreach ($file in $reports) {
    $report = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($report.HostEnvironmentInfo.Configuration -ne 'Release' -or $report.Benchmarks.Count -eq 0) {
        throw "Empty or non-Release benchmark report: $($file.Name)."
    }
    foreach ($result in $report.Benchmarks) {
        if (-not $actual.TryAdd($result.FullName, $result)) { throw "Duplicate benchmark result: $($result.FullName)." }
    }
}

$expected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$failures = [System.Collections.Generic.List[string]]::new()
$summary = [System.Collections.Generic.List[string]]::new()
$summary.Add('# CLI performance budgets')
$summary.Add('')
$summary.Add('| Benchmark | Mean / limit (ms) | Allocated / limit (MiB) |')
$summary.Add('| --- | ---: | ---: |')
foreach ($limit in $budget.Benchmarks) {
    if (-not $expected.Add($limit.FullName)) { throw "Duplicate benchmark budget: $($limit.FullName)." }
    Assert-Number $limit.MaxMeanMilliseconds 'MaxMeanMilliseconds' 0.001
    Assert-Number $limit.MaxAllocatedBytes 'MaxAllocatedBytes' 1
    if (-not $actual.ContainsKey($limit.FullName)) { throw "Missing benchmark result: $($limit.FullName)." }
    $result = $actual[$limit.FullName]
    Assert-Number $result.Statistics.N "$($limit.FullName) sample count" 3
    Assert-Number $result.Statistics.Mean "$($limit.FullName) mean" 0.001
    Assert-Number $result.Memory.BytesAllocatedPerOperation "$($limit.FullName) allocated bytes" 1
    $milliseconds = $result.Statistics.Mean / 1000000
    $allocated = $result.Memory.BytesAllocatedPerOperation
    if ($milliseconds -gt $limit.MaxMeanMilliseconds) { $failures.Add("$($limit.FullName): runtime $milliseconds ms > $($limit.MaxMeanMilliseconds) ms") }
    if ($allocated -gt $limit.MaxAllocatedBytes) { $failures.Add("$($limit.FullName): allocated $allocated bytes > $($limit.MaxAllocatedBytes) bytes") }
    $summary.Add(('| {0} | {1:F2} / {2:F2} | {3:F2} / {4:F2} |' -f $limit.FullName, $milliseconds, $limit.MaxMeanMilliseconds, ($allocated / 1MB), ($limit.MaxAllocatedBytes / 1MB)))
}
foreach ($name in $actual.Keys) {
    if (-not $expected.Contains($name)) { throw "Benchmark has no budget: $name." }
}

$summary.Add('')
$summary.Add('Sampled retained analysis growth after full collections; these values are not peak process RAM.')
$summary.Add('')
$retentionFiles = @(Get-ChildItem -LiteralPath (Join-Path $ArtifactsPath 'retention') -Filter '*.json' -File)
$retention = [System.Collections.Generic.Dictionary[int, object]]::new()
foreach ($file in $retentionFiles) {
    $sample = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    Assert-Number $sample.NodeCount 'retention NodeCount' 1
    if (-not $retention.TryAdd($sample.NodeCount, $sample)) { throw "Duplicate retention result for $($sample.NodeCount) nodes." }
}
$expectedSizes = [System.Collections.Generic.HashSet[int]]::new()
foreach ($limit in $budget.Retention) {
    Assert-Number $limit.NodeCount 'retention budget NodeCount' 1
    Assert-Number $limit.MaxRetainedAnalysisBytes 'MaxRetainedAnalysisBytes' 1
    if (-not $expectedSizes.Add($limit.NodeCount)) { throw 'Duplicate retention budget.' }
    if (-not $retention.ContainsKey($limit.NodeCount)) { throw "Missing retention result for $($limit.NodeCount) nodes." }
    $sample = $retention[$limit.NodeCount]
    Assert-Number $sample.CompletedBuilds 'CompletedBuilds' 3
    Assert-Number $sample.PeakRetainedAnalysisBytes 'PeakRetainedAnalysisBytes' 0
    if ($sample.ColumnReferencesPerObject -ne 8193) { throw 'The synthetic retention workload changed; review its budgets.' }
    if ($sample.PeakRetainedAnalysisBytes -gt $limit.MaxRetainedAnalysisBytes) {
        $failures.Add("Synthetic retention ($($limit.NodeCount) nodes): $($sample.PeakRetainedAnalysisBytes) bytes > $($limit.MaxRetainedAnalysisBytes) bytes")
    }
    $summary.Add(('- {0} objects: retained {1:F2} MiB / {2:F2} MiB limit.' -f $limit.NodeCount, ($sample.PeakRetainedAnalysisBytes / 1MB), ($limit.MaxRetainedAnalysisBytes / 1MB)))
}
if ($retention.Count -ne $expectedSizes.Count) { throw 'A retention result has no budget.' }

$summary.Add('')
$summary.Add($(if ($failures.Count -eq 0) { 'Result: PASS.' } else { 'Result: FAIL.' }))
$summary.AddRange([string[]]$failures)
$summary | Set-Content -LiteralPath (Join-Path $ArtifactsPath 'budget-summary.md') -Encoding utf8
if ($failures.Count -gt 0) { throw "CLI performance gate failed:`n$($failures -join "`n")" }
Write-Host "CLI performance gate passed: $($actual.Count) benchmarks and $($retention.Count) retention budgets."
