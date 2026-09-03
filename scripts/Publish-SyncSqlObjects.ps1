#Requires -Version 7.0

<#
.SYNOPSIS
    Publishes a syncsql extraction into a git repository: clone, replace the object tree, fold metrics
    history, rebuild catalog.json, commit, push.

.DESCRIPTION
    This is the sync stage of the pipeline - the only place git actually runs. It lives here as a real
    script rather than an inline CI shell block so it can be read, reviewed, and run by hand from a
    workstation exactly as CI runs it.

    Everything it needs is a parameter. A value that is not passed falls back to the `git` block of
    config/servers.json (when -ConfigPath is given), then to the documented default. The one deliberate
    exception is -PushToken: a secret is read from the environment (SYNCSQL_PUSH_TOKEN, GIT_PUSH_TOKEN,
    or CI_JOB_Maintainer_Token) when it is not passed, so it never has to appear in a command line, a
    process listing, or shell history. It is handed to git through a credential helper that reads it from
    this process's environment - never written to disk and never embedded in the remote URL.

.PARAMETER ExtractedObjectsDir
    Directory holding the merged `syncsql sync --staging-root` output to publish.

.PARAMETER MetricsSnapshotDir
    Directory holding this run's `syncsql sync --metrics-snapshot-root` output. Omit (or point at a
    directory that does not exist) to skip the metrics history update entirely.

.PARAMETER ConfigPath
    config/servers.json, read only to default the git settings below. Omit to rely purely on parameters.

.PARAMETER RemoteUrl
    Repository the extracted objects are published to. Defaults to config `git.remoteUrl`.

.PARAMETER SkipPush
    Do everything except the push - useful for a local dry run against a real clone.

.EXAMPLE
    ./scripts/Publish-SyncSqlObjects.ps1 -ExtractedObjectsDir ./syncsql-output/objects `
        -MetricsSnapshotDir ./syncsql-output/metrics-snapshot -ConfigPath ./config/servers.json -SkipPush

.LINK
    ../.gitlab/README.md
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ExtractedObjectsDir,

    [string] $MetricsSnapshotDir,

    [string] $ConfigPath,

    [string] $RemoteUrl,

    [string] $Branch,

    [string] $PathPrefix,

    [string] $CommitUserName,

    [string] $CommitUserEmail,

    [string] $CommitMessage,

    [string] $MetricsHistoryDirName = 'metrics',

    [string] $CatalogFileName = 'catalog.json',

    [int] $HistoryLimit = 250,

    [int] $MetricsHistoryLimit = 90,

    [int] $MaxVersionsPerObject = 15,

    [int] $MaxHistoryContentCalls = 1500,

    [int] $MaxCoChangeCommitSize = 40,

    [string] $PushToken,

    [string] $SyncSqlCommand = 'syncsql',

    [string] $CloneDirectory,

    [string] $DotEnvPath,

    [switch] $SkipPush,

    [switch] $KeepCloneDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([Parameter(Mandatory)][string] $Message)
    Write-Host "==> $Message"
}

function Invoke-Native {
    <#
        .SYNOPSIS
            Runs a native command, turning a non-zero exit code into a terminating error unless
            -AllowFailure is passed (in which case the exit code is returned for the caller to inspect).
    #>
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [string[]] $ArgumentList = @(),
        [switch] $AllowFailure
    )

    & $FilePath @ArgumentList
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "$FilePath $($ArgumentList -join ' ') failed with exit code $exitCode."
    }

    return $exitCode
}

function Get-JsonProperty {
    param($InputObject, [Parameter(Mandatory)][string] $Name)

    if ($null -eq $InputObject) { return $null }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Resolve-Setting {
    <#
        .SYNOPSIS
            Parameter wins, then the config file's value, then the documented default.
    #>
    param([string] $Value, $ConfigValue, [string] $Default)

    if (-not [string]::IsNullOrWhiteSpace($Value)) { return $Value }
    if ($ConfigValue -is [string] -and -not [string]::IsNullOrWhiteSpace($ConfigValue)) { return $ConfigValue }
    return $Default
}

function Resolve-PushToken {
    param([string] $Value)

    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        return [pscustomobject]@{ Token = $Value; Source = 'the -PushToken parameter' }
    }

    foreach ($name in @('SYNCSQL_PUSH_TOKEN', 'GIT_PUSH_TOKEN', 'CI_JOB_Maintainer_Token')) {
        $fromEnvironment = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($fromEnvironment)) {
            return [pscustomobject]@{ Token = $fromEnvironment; Source = "the $name environment variable" }
        }
    }

    return $null
}

$createdCloneDirectory = $false

try {
    # ---------------------------------------------------------------- settings
    $gitConfig = $null
    if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
        if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
            throw "Config file not found: $ConfigPath"
        }
        $gitConfig = Get-JsonProperty (Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json) 'git'
    }

    $Branch          = Resolve-Setting $Branch          (Get-JsonProperty $gitConfig 'branch')          'main'
    $PathPrefix      = Resolve-Setting $PathPrefix      (Get-JsonProperty $gitConfig 'pathPrefix')      'objects'
    $CommitUserName  = Resolve-Setting $CommitUserName  (Get-JsonProperty $gitConfig 'commitUserName')  'SyncSQL Bot'
    $CommitUserEmail = Resolve-Setting $CommitUserEmail (Get-JsonProperty $gitConfig 'commitUserEmail') 'syncsql-bot@example.com'
    $CommitMessage   = Resolve-Setting $CommitMessage   (Get-JsonProperty $gitConfig 'commitMessage')   'chore(sync): update database objects'
    $RemoteUrl       = Resolve-Setting $RemoteUrl       (Get-JsonProperty $gitConfig 'remoteUrl')       ''

    if ([string]::IsNullOrWhiteSpace($RemoteUrl)) {
        throw 'No repository to publish to. Pass -RemoteUrl, or set git.remoteUrl in the config file passed as -ConfigPath.'
    }

    if (-not (Test-Path -LiteralPath $ExtractedObjectsDir -PathType Container)) {
        throw "Extracted objects directory not found: $ExtractedObjectsDir"
    }
    $ExtractedObjectsDir = (Resolve-Path -LiteralPath $ExtractedObjectsDir).Path

    $pushCredentials = Resolve-PushToken $PushToken
    if ($null -eq $pushCredentials -and -not $SkipPush) {
        throw 'No push token supplied. Pass -PushToken, set SYNCSQL_PUSH_TOKEN (or GIT_PUSH_TOKEN / CI_JOB_Maintainer_Token), or pass -SkipPush to stop before pushing.'
    }

    # ------------------------------------------------------------ git identity
    # The token reaches git only through a credential helper reading this process's environment: never a
    # command-line argument, never part of the remote URL, never written to disk.
    $credentialHelper = '!f() { echo username=oauth2; echo "password=$SYNCSQL_GIT_PASSWORD"; }; f'
    $env:GIT_TERMINAL_PROMPT = '0'
    if ($null -ne $pushCredentials) {
        Write-Step "Authenticating to $RemoteUrl with $($pushCredentials.Source)"
        $env:SYNCSQL_GIT_PASSWORD = $pushCredentials.Token
    }

    # ------------------------------------------------------------------- clone
    if ([string]::IsNullOrWhiteSpace($CloneDirectory)) {
        $CloneDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "syncsql-publish-$([guid]::NewGuid())"
        $createdCloneDirectory = $true
    }
    if (Test-Path -LiteralPath $CloneDirectory) {
        Remove-Item -LiteralPath $CloneDirectory -Recurse -Force
    }

    Write-Step "Cloning $RemoteUrl (branch '$Branch', depth $HistoryLimit) -> $CloneDirectory"
    $cloneArguments = @(
        '-c', "credential.helper=$credentialHelper",
        'clone', '--branch', $Branch, '--single-branch', '--depth', "$HistoryLimit", $RemoteUrl, $CloneDirectory
    )
    if ((Invoke-Native git $cloneArguments -AllowFailure) -ne 0) {
        Write-Step "Branch '$Branch' not found on the remote yet; cloning the default branch and creating it."
        if (Test-Path -LiteralPath $CloneDirectory) {
            Remove-Item -LiteralPath $CloneDirectory -Recurse -Force
        }
        Invoke-Native git @('-c', "credential.helper=$credentialHelper", 'clone', '--depth', "$HistoryLimit", $RemoteUrl, $CloneDirectory) | Out-Null
        Invoke-Native git @('-C', $CloneDirectory, 'checkout', '-B', $Branch) | Out-Null
    }

    # Persist the helper (not the token) in the clone's own config so the later push authenticates too.
    Invoke-Native git @('-C', $CloneDirectory, 'config', 'credential.helper', $credentialHelper) | Out-Null
    Invoke-Native git @('-C', $CloneDirectory, 'config', 'user.name', $CommitUserName) | Out-Null
    Invoke-Native git @('-C', $CloneDirectory, 'config', 'user.email', $CommitUserEmail) | Out-Null

    # ------------------------------------------------- replace the object tree
    # Wiped and repopulated every run, so an object dropped from the source database (or excluded by an
    # updated filter) shows up as a deletion in git instead of lingering forever.
    $targetDir = Join-Path $CloneDirectory $PathPrefix
    Write-Step "Replacing '$PathPrefix' with the extracted tree from $ExtractedObjectsDir"
    if (Test-Path -LiteralPath $targetDir) {
        Remove-Item -LiteralPath $targetDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

    $extractedEntries = @(Get-ChildItem -LiteralPath $ExtractedObjectsDir -Force)
    if ($extractedEntries.Count -eq 0) {
        Write-Warning "No extracted objects found under $ExtractedObjectsDir - publishing an empty tree."
    }
    else {
        Copy-Item -Path (Join-Path $ExtractedObjectsDir '*') -Destination $targetDir -Recurse -Force
    }

    # ---------------------------------------------------------- metrics history
    $metricsHistoryDir = Join-Path $CloneDirectory $MetricsHistoryDirName
    if (-not [string]::IsNullOrWhiteSpace($MetricsSnapshotDir) -and (Test-Path -LiteralPath $MetricsSnapshotDir -PathType Container)) {
        Write-Step "Updating metrics history ($MetricsHistoryLimit-snapshot retention) -> $metricsHistoryDir"
        Invoke-Native $SyncSqlCommand @(
            'metrics', 'update',
            '--snapshot-root', (Resolve-Path -LiteralPath $MetricsSnapshotDir).Path,
            '--history-root', $metricsHistoryDir,
            '--history-limit', "$MetricsHistoryLimit"
        ) | Out-Null
    }
    else {
        Write-Step 'No metrics snapshot directory to fold in; skipping the metrics history update.'
    }

    # ------------------------------------------------------------ catalog.json
    $catalogPath = Join-Path $targetDir $CatalogFileName
    Write-Step "Building $CatalogFileName ($HistoryLimit-commit history window) -> $catalogPath"
    $catalogArguments = @(
        'catalog', 'build',
        '--objects-root', $targetDir,
        '--output', $catalogPath,
        '--repo-root', $CloneDirectory,
        '--path-prefix', $PathPrefix,
        '--history-limit', "$HistoryLimit",
        '--max-versions-per-object', "$MaxVersionsPerObject",
        '--max-history-content-calls', "$MaxHistoryContentCalls",
        '--max-co-change-commit-size', "$MaxCoChangeCommitSize"
    )
    if (Test-Path -LiteralPath $metricsHistoryDir -PathType Container) {
        $catalogArguments += @('--metrics-root', $metricsHistoryDir)
    }
    Invoke-Native $SyncSqlCommand $catalogArguments | Out-Null

    # ------------------------------------------------------------ commit/push
    Invoke-Native git @('-C', $CloneDirectory, 'add', '-A') | Out-Null
    $hasChanges = (Invoke-Native git @('-C', $CloneDirectory, 'diff', '--cached', '--quiet') -AllowFailure) -ne 0

    if (-not $hasChanges) {
        Write-Step 'No changes detected; nothing to publish.'
    }
    elseif ($SkipPush) {
        Write-Step "Changes staged in $CloneDirectory but -SkipPush was passed; not committing or pushing."
    }
    else {
        Invoke-Native git @('-C', $CloneDirectory, 'commit', '-m', $CommitMessage) | Out-Null
        Write-Step "Pushing to '$Branch'"
        Invoke-Native git @('-C', $CloneDirectory, 'push', 'origin', "HEAD:$Branch") | Out-Null
    }

    # --------------------------------------------------------- downstream hand-off
    if (-not [string]::IsNullOrWhiteSpace($DotEnvPath)) {
        Write-Step "Writing PATH_PREFIX/GIT_BRANCH -> $DotEnvPath"
        Set-Content -LiteralPath $DotEnvPath -Value @("PATH_PREFIX=$PathPrefix", "GIT_BRANCH=$Branch") -Encoding utf8NoBOM
    }
}
finally {
    $env:SYNCSQL_GIT_PASSWORD = $null
    if ($createdCloneDirectory -and -not $KeepCloneDirectory -and -not [string]::IsNullOrWhiteSpace($CloneDirectory) -and (Test-Path -LiteralPath $CloneDirectory)) {
        Remove-Item -LiteralPath $CloneDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
