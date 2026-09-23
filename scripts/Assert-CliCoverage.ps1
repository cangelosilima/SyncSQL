[CmdletBinding()]
param([Parameter(Mandatory)] [string] $SummaryPath)

$ErrorActionPreference = 'Stop'
$report = Get-Content -LiteralPath $SummaryPath -Raw | ConvertFrom-Json
$summary = $report.summary
if ($null -eq $summary) { throw 'CLI coverage report is missing its summary.' }

$metrics = @(
    @('lines', 'coveredlines', 'coverablelines'),
    @('branches', 'coveredbranches', 'totalbranches'),
    @('methods', 'coveredmethods', 'totalmethods'),
    @('full methods', 'fullcoveredmethods', 'totalmethods')
)
$failures = @()
foreach ($metric in $metrics) {
    $covered = $summary.($metric[1])
    $total = $summary.($metric[2])
    # Missing PRO method metrics must fail closed, just as they do in CI.
    if ($null -eq $covered -or $null -eq $total -or
        $covered -is [string] -or $total -is [string] -or
        $covered -is [bool] -or $total -is [bool] -or
        $total -le 0 -or $covered -ne $total -or $total -ne [math]::Truncate($total)) {
        $failures += "$($metric[0]) $covered/$total"
    }
}
if ($failures.Count -gt 0) {
    throw "CLI coverage gate failed: $($failures -join ', '). See $SummaryPath. Missing method metrics require a ReportGenerator PRO license (REPORTGENERATOR_LICENSE or a locally registered license)."
}
Write-Host 'CLI coverage: 100% of lines, branches, methods, and full methods.'
