[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$summaryPath = [System.IO.Path]::GetTempFileName()
try {
    $valid = @{
        coveredlines = 10000; coverablelines = 10000
        coveredbranches = 2000; totalbranches = 2000
        coveredmethods = 500; fullcoveredmethods = 500; totalmethods = 500
    }
    @{ summary = $valid } | ConvertTo-Json | Set-Content -LiteralPath $summaryPath
    & "$PSScriptRoot/Assert-CliCoverage.ps1" -SummaryPath $summaryPath

    foreach ($field in @('coveredlines', 'coveredbranches', 'coveredmethods', 'fullcoveredmethods')) {
        $invalid = $valid.Clone()
        $invalid[$field]--
        @{ summary = $invalid } | ConvertTo-Json | Set-Content -LiteralPath $summaryPath
        $rejected = $false
        try { & "$PSScriptRoot/Assert-CliCoverage.ps1" -SummaryPath $summaryPath }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Gate accepted an uncovered item in $field." }
    }

    foreach ($json in @(
        '{}',
        '{"summary":{}}',
        '{"summary":{"coveredlines":0,"coverablelines":0}}',
        '{"summary":{"coveredlines":10000,"coverablelines":10000,"coveredbranches":2000,"totalbranches":2000}}',
        '{broken'
    )) {
        Set-Content -LiteralPath $summaryPath -Value $json
        $rejected = $false
        try { & "$PSScriptRoot/Assert-CliCoverage.ps1" -SummaryPath $summaryPath }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Gate accepted missing or malformed metrics: $json" }
    }
    Write-Host 'Coverage gate regression checks passed.'
}
finally {
    Remove-Item -LiteralPath $summaryPath -Force
}
