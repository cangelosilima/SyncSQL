#Requires -Version 7.0
<#
.SYNOPSIS
    Prepares everything Export-DatabaseObjects.ps1 needs: the SqlServer
    PowerShell module and the Oracle.ManagedDataAccess.Core managed ADO.NET
    driver (no native Oracle client required). Config parsing uses JSON
    (built into PowerShell 7+), so no separate module is needed for that.

.DESCRIPTION
    Everything is saved under -ModulesCacheDir rather than "installed", so
    the whole directory can be cached as-is between GitLab CI pipeline runs
    (see .gitlab-ci.yml) without needing elevated permissions.

    Safe to re-run: already-present modules/drivers are left alone unless
    -Force is passed.
#>
[CmdletBinding()]
param(
    [string]$ModulesCacheDir = (Join-Path (Split-Path $PSScriptRoot -Parent) '.pwsh-modules'),
    [string]$OracleDriverVersion,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$modulesDir = Join-Path $ModulesCacheDir 'Modules'
$oracleDir = Join-Path $ModulesCacheDir 'oracle'
New-Item -ItemType Directory -Path $modulesDir -Force | Out-Null
New-Item -ItemType Directory -Path $oracleDir -Force | Out-Null

function Install-SyncSqlPSGalleryModule {
    param([Parameter(Mandatory)][string]$Name)

    $alreadyPresent = Test-Path -LiteralPath (Join-Path $modulesDir $Name)
    if ($alreadyPresent -and -not $Force) {
        Write-Host "[bootstrap] $Name already cached, skipping."
        return
    }

    if (-not (Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue)) {
        Register-PSRepository -Default
        if (-not (Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue)) {
            throw "Could not register the PSGallery repository. Check that this runner can reach https://www.powershellgallery.com."
        }
    }
    Set-PSRepository -Name PSGallery -InstallationPolicy Trusted

    Write-Host "[bootstrap] Saving module $Name to $modulesDir"
    Save-Module -Name $Name -Path $modulesDir -Repository PSGallery -Force
}

function Get-SyncSqlLatestNuGetVersion {
    param([Parameter(Mandatory)][string]$PackageId)

    $indexUrl = "https://api.nuget.org/v3-flatcontainer/$($PackageId.ToLowerInvariant())/index.json"
    $index = Invoke-RestMethod -Uri $indexUrl
    return ($index.versions | Select-Object -Last 1)
}

function Install-SyncSqlOracleDriver {
    param([string]$Version)

    $sentinel = Join-Path $oracleDir '.driver-version'
    if ((Test-Path -LiteralPath $sentinel) -and -not $Force) {
        Write-Host "[bootstrap] Oracle managed driver already cached ($(Get-Content $sentinel)), skipping."
        return
    }

    $packageId = 'Oracle.ManagedDataAccess.Core'
    if ([string]::IsNullOrWhiteSpace($Version)) {
        Write-Host "[bootstrap] Resolving latest $packageId version from NuGet"
        $Version = Get-SyncSqlLatestNuGetVersion -PackageId $packageId
    }
    Write-Host "[bootstrap] Downloading $packageId $Version"

    $idLower = $packageId.ToLowerInvariant()
    $verLower = $Version.ToLowerInvariant()
    $downloadUrl = "https://api.nuget.org/v3-flatcontainer/$idLower/$verLower/$idLower.$verLower.nupkg"

    $tempZip = Join-Path ([IO.Path]::GetTempPath()) "$idLower.$verLower.zip"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip

    if (Test-Path -LiteralPath $oracleDir) { Remove-Item -LiteralPath $oracleDir -Recurse -Force }
    New-Item -ItemType Directory -Path $oracleDir -Force | Out-Null
    Expand-Archive -LiteralPath $tempZip -DestinationPath $oracleDir -Force
    Remove-Item -LiteralPath $tempZip -Force

    Set-Content -LiteralPath (Join-Path $oracleDir '.driver-version') -Value $Version
    Write-Host "[bootstrap] Oracle managed driver $Version ready under $oracleDir"
}

Install-SyncSqlPSGalleryModule -Name 'SqlServer'
Install-SyncSqlOracleDriver -Version $OracleDriverVersion

Write-Host "[bootstrap] Dependencies ready under $ModulesCacheDir"
