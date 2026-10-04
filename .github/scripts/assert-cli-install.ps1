[CmdletBinding()]
param([Parameter(Mandatory)][string] $Version)
$ErrorActionPreference = 'Stop'
$command = Get-Command syncsql -CommandType Application -ErrorAction Stop
Write-Host "Installed command: $($command.Source)"
$installedVersion = (& $command.Source --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $installedVersion -notmatch ('^' + [regex]::Escape($Version) + '(\+[^\s]+)?$')) {
    throw "Expected syncsql $Version, got '$installedVersion'."
}
$help = & $command.Source --help | Out-String
if ($LASTEXITCODE -ne 0 -or $help -notmatch 'validate-config' -or $help -notmatch 'catalog') {
    throw 'The installed CLI did not return its expected help.'
}
Write-Host $help
