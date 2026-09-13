[CmdletBinding()]
param()

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
    throw 'Run this script from inside the repository.'
}

& git -C $repoRoot config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to configure Git core.hooksPath.'
}

Write-Host "Git hooks enabled from $repoRoot/.githooks"
