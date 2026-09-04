#Requires -Version 5.1

<#
.SYNOPSIS
    Publishes a syncsql extraction into a git repository: clone, replace the object tree, fold metrics
    history, rebuild catalog.json, commit, push.

.DESCRIPTION
    This is the sync stage of the pipeline - the only place git actually runs. It lives here as a real
    script rather than an inline CI shell block so it can be read, reviewed, and run by hand from a
    workstation exactly as CI runs it.

    Targets Windows PowerShell 5.1, so it runs on a stock Windows runner or workstation with no
    PowerShell install at all - and unchanged on PowerShell 7+ (which is what the Linux CI image uses).
    Nothing here relies on a 7-only language feature or cmdlet parameter.

    Everything it needs is a parameter. A value that is not passed falls back to the `git` block of
    config/servers.json (when -ConfigPath is given), then to the documented default. The one deliberate
    exception is -PushToken: a secret is read from the environment (SYNCSQL_PUSH_TOKEN, GIT_PUSH_TOKEN,
    or CI_JOB_Maintainer_Token) when it is not passed, so it never has to appear in a command line, a
    process listing, or shell history. It is handed to git through a credential helper that reads it from
    this process's environment - never written to disk and never embedded in the remote URL.

.PARAMETER ExtractedObjectsDir
    Directory holding the merged `syncsql sync --staging-root` output to publish. Its immediate children
    are the server directories, which is exactly how they land in the target repository.

.PARAMETER PathPrefix
    Folder inside the clone the object tree replaces. Defaults to config `git.pathPrefix`, then to empty -
    the tree is published at the repository root, so the first path segment is the server name. With an
    empty prefix the script replaces only the server directories it owns rather than wiping the clone,
    which would take .git with it; see .gitlab/README.md's "Wipe and repopulate".

.PARAMETER MetricsSnapshotDir
    Directory holding this run's `syncsql sync --metrics-snapshot-root` output. Omit (or point at a
    directory that does not exist) to skip the metrics history update entirely.

.PARAMETER MetricsSnapshotDirName
    Name of the snapshot folder as it appears inside -ExtractedObjectsDir. Only used to recognize and
    skip it: `syncsql sync`'s defaults put the snapshot and metrics folders next to the server
    directories under one output root, so -ExtractedObjectsDir can be that root without those two (and
    a catalog.json left over from a local `catalog build`) being mistaken for servers and published.

.PARAMETER ConfigPath
    config/servers.json, read only to default the git settings below. Omit to rely purely on parameters.

.PARAMETER RemoteUrl
    Repository the extracted objects are published to. Defaults to config `git.remoteUrl`.

.PARAMETER SkipPush
    Do everything except the push - useful for a local dry run against a real clone.

.EXAMPLE
    ./scripts/Publish-SyncSqlObjects.ps1 -ExtractedObjectsDir ./syncsql-output `
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

    [string] $MetricsSnapshotDirName = 'metrics-snapshot',

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

# 2.0 rather than Latest: identical, defined semantics on Windows PowerShell 5.1 and PowerShell 7+.
Set-StrictMode -Version 2.0
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
        .NOTES
            The command's own output goes straight to the host, so the only thing this function returns
            is the exit code - callers can compare it without picking it out of the command's stdout.
    #>
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [string[]] $ArgumentList = @(),
        [switch] $AllowFailure
    )

    # Windows PowerShell 5.1 turns a native command's stderr into error records while
    # $ErrorActionPreference is 'Stop' - and git writes its progress there - so relax it for the call
    # and judge the command by its exit code instead.
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @ArgumentList | Out-Host
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

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

function Remove-SyncSqlServerDirectories {
    <#
        .SYNOPSIS
            Deletes the server directories a previous run published at the root of the clone, so objects
            dropped from the fleet show up as deletions instead of lingering forever.
        .DESCRIPTION
            Only reached when -PathPrefix is empty, i.e. the extracted tree starts at the repository root
            with the server name. There is no folder to wipe in that layout, and wiping the clone itself
            would take .git - and any other content the repository holds - with it, so the set of
            directories this tool owns is worked out explicitly instead:

              * the top-level directories this run is about to write, and
              * the servers the previous run recorded in catalog.json - which is what makes a server that
                stopped being extracted disappear from git rather than go stale.

            Anything else at the root (.git, the metrics history folder, a README, the catalog file) is
            left untouched. Returns the names it removed.
    #>
    param(
        [Parameter(Mandatory)][string] $CloneDirectory,
        [object[]] $ExtractedEntries = @(),
        [Parameter(Mandatory)][string] $CatalogFileName
    )

    $owned = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    foreach ($entry in $ExtractedEntries) {
        if ($entry.PSIsContainer) { [void] $owned.Add($entry.Name) }
    }

    $previousCatalog = Join-Path $CloneDirectory $CatalogFileName
    if (Test-Path -LiteralPath $previousCatalog -PathType Leaf) {
        try {
            $catalog = Get-Content -LiteralPath $previousCatalog -Raw | ConvertFrom-Json
            foreach ($server in @(Get-JsonProperty $catalog 'servers')) {
                if ($server -is [string] -and -not [string]::IsNullOrWhiteSpace($server)) { [void] $owned.Add($server) }
            }
        }
        catch {
            # A catalog.json that cannot be parsed (hand-edited, truncated, written by a much older
            # version) is not worth failing the publish over: this run still replaces everything it is
            # about to write, it just cannot also clean up a server it no longer extracts.
            Write-Warning "Could not read the previous $CatalogFileName to find stale servers: $($_.Exception.Message)"
        }
    }

    $removed = @()
    foreach ($name in $owned) {
        if ($name -eq '.git') { continue }
        $path = Join-Path $CloneDirectory $name
        if (Test-Path -LiteralPath $path -PathType Container) {
            Remove-Item -LiteralPath $path -Recurse -Force
            $removed += $name
        }
    }

    return $removed
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
    # Empty by default: the extracted tree starts at the repository root, so the first path segment of
    # every published file is the server it came from.
    $PathPrefix      = Resolve-Setting $PathPrefix      (Get-JsonProperty $gitConfig 'pathPrefix')      ''
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
    $targetDir = if ([string]::IsNullOrWhiteSpace($PathPrefix)) { $CloneDirectory } else { Join-Path $CloneDirectory $PathPrefix }
    $prefixLabel = if ([string]::IsNullOrWhiteSpace($PathPrefix)) { 'the repository root' } else { "'$PathPrefix'" }
    Write-Step "Replacing the extracted tree under $prefixLabel with $ExtractedObjectsDir"

    # The extracted tree is exactly the server directories; `syncsql sync`'s own defaults put the
    # metrics folders and a locally built catalog.json beside them under one output root, so those are
    # recognized and skipped rather than published as if they were servers.
    $skipNames = @(@($MetricsHistoryDirName, $MetricsSnapshotDirName, $CatalogFileName) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $serverEntries = @()
    foreach ($entry in @(Get-ChildItem -LiteralPath $ExtractedObjectsDir -Force)) {
        if (-not $entry.PSIsContainer) {
            Write-Warning "Skipping '$($entry.Name)': the extracted tree holds one directory per server, not loose files."
            continue
        }
        if ($skipNames -contains $entry.Name) {
            Write-Step "Skipping '$($entry.Name)' - not part of the extracted object tree."
            continue
        }
        $serverEntries += $entry
    }

    if ($serverEntries.Count -eq 0) {
        Write-Warning "No extracted objects found under $ExtractedObjectsDir - publishing an empty tree."
    }

    if ([string]::IsNullOrWhiteSpace($PathPrefix)) {
        # With no prefix the target IS the clone, so wiping it wholesale would take .git (and anything
        # else the repository holds) with it. Only the server directories this tool owns are removed:
        # the ones this run is about to write, plus the ones the previous run recorded in catalog.json -
        # which is how a server that stopped being extracted still shows up as a deletion.
        $ownedNames = @(Remove-SyncSqlServerDirectories -CloneDirectory $CloneDirectory `
            -ExtractedEntries $serverEntries -CatalogFileName $CatalogFileName)
        Write-Step "Removed $($ownedNames.Count) previously published server director(ies) from the repository root"
    }
    else {
        if (Test-Path -LiteralPath $targetDir) {
            Remove-Item -LiteralPath $targetDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    foreach ($entry in $serverEntries) {
        Copy-Item -LiteralPath $entry.FullName -Destination $targetDir -Recurse -Force
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
        # Written through .NET rather than Set-Content: 5.1's -Encoding utf8 always emits a BOM, which
        # GitLab's dotenv parser reads as part of the first variable's name. Line endings are forced to
        # LF for the same reason - a CRLF from a Windows runner would end up inside the value.
        $dotEnvFullPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DotEnvPath)
        $dotEnvContent = "PATH_PREFIX=$PathPrefix`nGIT_BRANCH=$Branch`n"
        [System.IO.File]::WriteAllText($dotEnvFullPath, $dotEnvContent, (New-Object System.Text.UTF8Encoding($false)))
    }
}
finally {
    Remove-Item Env:\SYNCSQL_GIT_PASSWORD -ErrorAction SilentlyContinue
    if ($createdCloneDirectory -and -not $KeepCloneDirectory -and -not [string]::IsNullOrWhiteSpace($CloneDirectory) -and (Test-Path -LiteralPath $CloneDirectory)) {
        Remove-Item -LiteralPath $CloneDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
