#Requires -Version 5.1
<#
.SYNOPSIS
    Extracts database objects (procedures, views, functions, triggers,
    tables, schemas, synonyms, linked servers / database links) from a
    fleet of MSSQL and Oracle servers and publishes them to a git
    repository, one file per object.

.DESCRIPTION
    Driven entirely by a JSON config file (see config/servers.example.json)
    so the same script works unmodified across environments: which
    servers/databases/schemas/objects to touch is all regex-filterable
    configuration, not code. Credentials are never stored in the config;
    they are read from CI/CD variables named
    "<credentialsVariablePrefix>_DB_USER" / "..._DB_PASSWORD".

.PARAMETER ConfigPath
    Path to the JSON config file. Defaults to config/servers.json next to
    the repository root.

.PARAMETER StagingRoot
    Local directory the extraction is written to before being synced into
    the git checkout. Defaults to a fresh temp directory.

.PARAMETER ModulesCacheDir
    Directory populated by Bootstrap-Dependencies.ps1 with the SqlServer
    PowerShell module, the Oracle managed driver, and ScriptDom (used for
    real T-SQL-parser-based MSSQL lineage inference in Build-Catalog.ps1;
    falls back to the regex-based scan if not found, never a hard
    failure).

.PARAMETER ServerNameInclude / ServerNameExclude
    Optional regex overrides for which configured servers actually run in
    this invocation. Takes precedence over config.serverSelection. Useful
    for ad-hoc / single-server pipeline runs.

.PARAMETER PushToken
    Token used to push to the target git repository. Defaults to the
    CI_JOB_Maintainer_Token environment variable (a Maintainer-role project
    access token with write_repository scope), falling back to
    GIT_PUSH_TOKEN if that isn't set.

.PARAMETER SkipGit
    Run the extraction and leave results under -StagingRoot without
    cloning/committing/pushing anything (and without building a catalog -
    see -HistoryLimit). Useful for local testing.

.PARAMETER DotenvPath
    Optional path to write a small KEY=VALUE file with the resolved
    PATH_PREFIX and GIT_BRANCH (config.git.pathPrefix / .branch, after
    defaulting). Meant to be picked up by GitLab CI as a `dotenv` artifact
    report so a downstream job can act on the same path/branch this run
    just pushed to, without hardcoding or duplicating those defaults in
    .gitlab-ci.yml.

.PARAMETER HistoryLimit
    When publishing to git (i.e. -SkipGit is not set), src/Build-Catalog.ps1
    is run against the target repo's own checkout before committing, so
    catalog.json is versioned in the same commit as the extracted objects
    right alongside them under config.git.pathPrefix - not just a
    CI artifact. This controls both how many commits are cloned to make
    that possible and how many of them Build-Catalog.ps1 mines for its
    change-history/heatmap/point-in-time features.

.PARAMETER MetricsHistoryLimit
    Maximum number of daily metrics snapshots (row counts, index
    fragmentation/usage, optimizer statistics - see
    src/Update-MetricsHistory.ps1) retained per table. This data changes
    every run by nature, so it is deliberately kept out of each object's
    own versioned file - see "Volatile metrics" in the README.
#>
[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'config/servers.json'),
    [string]$StagingRoot = (Join-Path ([IO.Path]::GetTempPath()) "syncsql-staging-$([Guid]::NewGuid())"),
    [string]$ModulesCacheDir = (Join-Path (Split-Path $PSScriptRoot -Parent) '.pwsh-modules'),
    [string[]]$ServerNameInclude,
    [string[]]$ServerNameExclude,
    [string]$PushToken = $(if ($env:CI_JOB_Maintainer_Token) { $env:CI_JOB_Maintainer_Token } else { $env:GIT_PUSH_TOKEN }),
    [switch]$SkipGit,
    [string]$DotenvPath,
    [int]$HistoryLimit = 250,
    [int]$MetricsHistoryLimit = 90
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Only auto-clean the staging directory when the caller didn't ask for a
# specific one - CI pipelines pass an explicit path here so the extracted
# tree survives as a job artifact for debugging this run.
$stagingRootExplicit = $PSBoundParameters.ContainsKey('StagingRoot')

# Modules saved by Bootstrap-Dependencies.ps1 live here; make them importable
# regardless of which process/job step is currently running.
$modulesPathEntry = Join-Path $ModulesCacheDir 'Modules'
if ((Test-Path -LiteralPath $modulesPathEntry) -and ($env:PSModulePath -notlike "*$modulesPathEntry*")) {
    $env:PSModulePath = "$modulesPathEntry$([IO.Path]::PathSeparator)$env:PSModulePath"
}

Import-Module (Join-Path $PSScriptRoot 'modules/SyncSql.Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'modules/SyncSql.MsSql.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'modules/SyncSql.Oracle.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'modules/SyncSql.Git.psm1') -Force
# Only Find-SyncSqlScriptDomDll is needed here (to locate the DLL for the
# -ScriptDomDllPath handed to Build-Catalog.ps1 below) - actually loading
# ScriptDom happens inside Build-Catalog.ps1 itself.
Import-Module (Join-Path $PSScriptRoot 'modules/SyncSql.MsSqlLineage.psm1') -Force

Write-SyncSqlLog "Loading config from $ConfigPath"
$config = Import-SyncSqlConfig -Path $ConfigPath

if ($DotenvPath) {
    $gitDefaults = Get-SyncSqlGitDefaults -GitConfig $config.git
    "PATH_PREFIX=$($gitDefaults.pathPrefix)`nGIT_BRANCH=$($gitDefaults.branch)`n" | Set-Content -LiteralPath $DotenvPath -NoNewline
    Write-SyncSqlLog "Wrote $DotenvPath"
}

$serverSelection = [ordered]@{ include = @('.*'); exclude = @() }
if ($config.Contains('serverSelection') -and $config.serverSelection) {
    if ($config.serverSelection.Contains('include') -and $config.serverSelection['include']) { $serverSelection['include'] = @($config.serverSelection['include']) }
    if ($config.serverSelection.Contains('exclude') -and $config.serverSelection['exclude']) { $serverSelection['exclude'] = @($config.serverSelection['exclude']) }
}
if ($ServerNameInclude) { $serverSelection['include'] = $ServerNameInclude }
if ($ServerNameExclude) { $serverSelection['exclude'] = $ServerNameExclude }

New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null
Write-SyncSqlLog "Staging extracted objects under $StagingRoot"

# Separate from $StagingRoot on purpose: this only ever holds *this run's*
# volatile metrics snapshots (row counts, index fragmentation/usage,
# optimizer statistics), never committed as-is. Update-MetricsHistory.ps1
# folds it into an accumulating history tree outside git.pathPrefix - see
# the -MetricsHistoryLimit hook below.
$metricsRoot = Join-Path ([IO.Path]::GetTempPath()) "syncsql-metrics-$([Guid]::NewGuid())"
New-Item -ItemType Directory -Path $metricsRoot -Force | Out-Null

$oracleDriverDll = Find-SyncSqlOracleDriverDll -CacheDir (Join-Path $ModulesCacheDir 'oracle')
$scriptDomDll = Find-SyncSqlScriptDomDll -CacheDir (Join-Path $ModulesCacheDir 'scriptdom')
if (-not $scriptDomDll) {
    Write-SyncSqlLog "ScriptDom not found under '$ModulesCacheDir/scriptdom' - MSSQL lineage will fall back to the regex-based text scan. Run Bootstrap-Dependencies.ps1 to fetch it." -Level WARN
}

$summaryLines = [System.Collections.Generic.List[string]]::new()
$failedServers = [System.Collections.Generic.List[string]]::new()
$totalFiles = 0

foreach ($server in $config.servers) {
    $serverName = $server.name

    if (-not (Test-SyncSqlNameAllowed -Name $serverName -Filter $serverSelection)) {
        Write-SyncSqlLog "Skipping '$serverName' (excluded by server selection filter)"
        continue
    }

    $prefix = $server.credentialsVariablePrefix
    $userVarName = "${prefix}_DB_USER"
    $passVarName = "${prefix}_DB_PASSWORD"
    $username = [Environment]::GetEnvironmentVariable($userVarName)
    $password = [Environment]::GetEnvironmentVariable($passVarName)

    if ([string]::IsNullOrEmpty($username) -or [string]::IsNullOrEmpty($password)) {
        Write-SyncSqlLog "Skipping '$serverName': CI/CD variables '$userVarName' / '$passVarName' are not set." -Level ERROR
        $failedServers.Add($serverName)
        continue
    }

    $filters = Get-SyncSqlEffectiveFilters -Defaults $config.defaults -Server $server
    $allowedObjectTypes = Get-SyncSqlAllowedObjectTypes -Filters $filters
    if ($allowedObjectTypes.Count -eq 0) {
        Write-SyncSqlLog "Skipping '$serverName': no objectTypes configured (defaults + server override both empty)." -Level WARN
        continue
    }

    try {
        $fileCount = 0
        switch ($server.type) {
            'mssql' {
                $fileCount = Export-SyncSqlMsSqlServer -Server $server -Username $username -Password $password `
                    -Filters $filters -AllowedObjectTypes $allowedObjectTypes -StagingRoot $StagingRoot -MetricsRoot $metricsRoot
            }
            'oracle' {
                if (-not $oracleDriverDll) {
                    throw "Oracle managed driver was not found under '$ModulesCacheDir/oracle'. Run Bootstrap-Dependencies.ps1 first."
                }
                $fileCount = Export-SyncSqlOracleServer -Server $server -Username $username -Password $password `
                    -Filters $filters -AllowedObjectTypes $allowedObjectTypes -StagingRoot $StagingRoot -DriverDllPath $oracleDriverDll `
                    -MetricsRoot $metricsRoot
            }
        }
        $totalFiles += $fileCount
        $summaryLines.Add("- $serverName ($($server.type)): $fileCount object file(s)")
    }
    catch {
        Write-SyncSqlLog "Extraction failed for '$serverName': $($_.Exception.Message)" -Level ERROR
        $failedServers.Add($serverName)
        $summaryLines.Add("- $serverName ($($server.type)): FAILED - $($_.Exception.Message)")
    }
}

Write-SyncSqlLog "Extraction complete: $totalFiles object file(s) across $($config.servers.Count - $failedServers.Count) server(s); $($failedServers.Count) failure(s)."

if ($SkipGit) {
    Write-SyncSqlLog "SkipGit set; leaving extracted files at $StagingRoot"
    Write-SyncSqlLog "SkipGit set; leaving this run's metrics snapshots at $metricsRoot (no history tree to accumulate them into without a git checkout)"
}
else {
    if ([string]::IsNullOrWhiteSpace($PushToken)) {
        throw "No push token supplied. Set the CI_JOB_Maintainer_Token CI/CD variable (a Maintainer-role token with write_repository scope) or pass -PushToken."
    }

    $gitWorkDir = Join-Path ([IO.Path]::GetTempPath()) "syncsql-repo-$([Guid]::NewGuid())"
    $buildCatalogScript = Join-Path $PSScriptRoot 'Build-Catalog.ps1'
    $updateMetricsScript = Join-Path $PSScriptRoot 'Update-MetricsHistory.ps1'
    $catalogHistoryLimit = $HistoryLimit
    $metricsHistoryLimitCopy = $MetricsHistoryLimit
    $metricsRootCopy = $metricsRoot
    $metricsDirName = 'metrics'
    $scriptDomDllCopy = $scriptDomDll
    $catalogHook = {
        param($WorkDir, $PathPrefix)

        # Kept outside $PathPrefix on purpose - Publish-SyncSqlToGit only
        # wipes/replaces $PathPrefix, so this tree accumulates across runs
        # instead of being reset to just this run's snapshot every time.
        $metricsHistoryRoot = Join-Path $WorkDir $metricsDirName
        Write-SyncSqlLog "Updating metrics history ($metricsHistoryLimitCopy-snapshot retention) -> $metricsHistoryRoot"
        & $updateMetricsScript -SnapshotRoot $metricsRootCopy -HistoryRoot $metricsHistoryRoot -HistoryLimit $metricsHistoryLimitCopy

        # [IO.Path]::Combine rather than Join-Path with 3 segments: PS5.1's
        # Join-Path only takes a single -Path/-ChildPath pair.
        $catalogOutputPath = [IO.Path]::Combine($WorkDir, $PathPrefix, 'catalog.json')
        Write-SyncSqlLog "Building catalog.json ($catalogHistoryLimit-commit history window) -> $catalogOutputPath"
        & $buildCatalogScript -ObjectsRoot (Join-Path $WorkDir $PathPrefix) -OutputPath $catalogOutputPath `
            -RepoRoot $WorkDir -PathPrefix $PathPrefix -HistoryLimit $catalogHistoryLimit -MetricsRoot $metricsHistoryRoot `
            -ScriptDomDllPath $scriptDomDllCopy
    }.GetNewClosure()

    try {
        $published = Publish-SyncSqlToGit -GitConfig $config.git -StagingRoot $StagingRoot -Token $PushToken `
            -WorkDir $gitWorkDir -Summary ($summaryLines -join "`n") -CloneDepth $HistoryLimit -PostSyncHook $catalogHook
        if ($published) {
            Write-SyncSqlLog "Published extracted objects, metrics history and catalog.json."
        }
    }
    finally {
        Remove-Item -LiteralPath $gitWorkDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $metricsRoot -Recurse -Force -ErrorAction SilentlyContinue
        if (-not $stagingRootExplicit) {
            Remove-Item -LiteralPath $StagingRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($failedServers.Count -gt 0) {
    Write-SyncSqlLog "Failed server(s): $($failedServers -join ', ')" -Level ERROR
    exit 1
}
