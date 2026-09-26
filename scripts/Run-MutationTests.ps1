[CmdletBinding()]
param(
    [ValidateSet('Core', 'Catalog', 'Cli', 'Extraction.MsSql', 'Extraction.Oracle', 'Lineage.MsSql', 'Lineage.Oracle')]
    [string[]] $Project = @('Core', 'Catalog', 'Cli', 'Extraction.MsSql', 'Extraction.Oracle', 'Lineage.MsSql', 'Lineage.Oracle'),
    [string[]] $Mutate,
    [ValidateRange(0, 100)]
    [int] $BreakAt = 0,
    [string] $ArtifactsPath = "$PSScriptRoot/../TestResults/mutation"
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath("$PSScriptRoot/..")
$artifacts = [System.IO.Path]::GetFullPath($ArtifactsPath)
Push-Location $repoRoot
try {
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "Tool restore failed with exit code $LASTEXITCODE." }

    foreach ($name in $Project) {
        Push-Location "$repoRoot/cli/tests/SyncSql.$name.Tests"
        try {
            $strykerArgs = @(
                'stryker', '--config-file', "$repoRoot/cli/stryker-config.json",
                '--project', "SyncSql.$name.csproj",
                '--output', "$artifacts/$name",
                '--threshold-high', "$([Math]::Max(80, $BreakAt))",
                '--threshold-low', "$([Math]::Max(60, $BreakAt))",
                '--break-at', "$BreakAt", '--skip-version-check'
            )
            foreach ($pattern in $Mutate) { $strykerArgs += @('--mutate', $pattern) }
            & dotnet @strykerArgs
            if ($LASTEXITCODE -ne 0) { throw "Mutation testing failed for $name with exit code $LASTEXITCODE." }
        }
        finally { Pop-Location }
    }
}
finally { Pop-Location }
