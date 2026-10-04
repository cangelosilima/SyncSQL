$ErrorActionPreference = 'Stop'
$env:PATH = (Join-Path $env:LOCALAPPDATA 'Microsoft/WindowsApps') + [IO.Path]::PathSeparator + $env:PATH
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Install-Module -Name Microsoft.WinGet.Client -Repository PSGallery -Scope CurrentUser -Force
    Import-Module Microsoft.WinGet.Client
    Repair-WinGetPackageManager -AllUsers
}
winget --version
if ($LASTEXITCODE) { throw 'WinGet bootstrap failed.' }
Join-Path $env:LOCALAPPDATA 'Microsoft/WindowsApps' >> $env:GITHUB_PATH
# Portable commands are added to the next workflow step's PATH.
Join-Path $env:LOCALAPPDATA 'Microsoft/WinGet/Links' >> $env:GITHUB_PATH
