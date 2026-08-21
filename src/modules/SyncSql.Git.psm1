#Requires -Version 5.1
<#
    SyncSql.Git.psm1
    Clones (or creates) the target repository, replaces the configured
    path prefix with the freshly staged extraction tree (so dropped
    objects show up as deletions), and commits/pushes the result.

    The push token is only ever handed to git via GIT_ASKPASS + process
    environment variables, never as a CLI argument or embedded in a
    remote URL, so it cannot leak through `ps`, `git remote -v`, or
    shell history.
#>

Set-StrictMode -Version Latest

function Get-SyncSqlTargetRepoUrl {
    [CmdletBinding()]
    param([string]$ConfigRemoteUrl)

    if (-not [string]::IsNullOrWhiteSpace($ConfigRemoteUrl)) {
        return $ConfigRemoteUrl
    }
    if ([string]::IsNullOrWhiteSpace($env:CI_SERVER_PROTOCOL) -or
        [string]::IsNullOrWhiteSpace($env:CI_SERVER_HOST) -or
        [string]::IsNullOrWhiteSpace($env:CI_PROJECT_PATH)) {
        throw "git.remoteUrl is not set in the config and CI_SERVER_PROTOCOL/CI_SERVER_HOST/CI_PROJECT_PATH are unavailable; cannot determine the target repository."
    }
    return "$($env:CI_SERVER_PROTOCOL)://$($env:CI_SERVER_HOST)/$($env:CI_PROJECT_PATH).git"
}

function Get-SyncSqlGitDefaults {
    <#
        Resolves the git.* config block's optional keys to their defaults
        in one place, so Publish-SyncSqlToGit and callers that need the
        same values ahead of time (e.g. Export-DatabaseObjects.ps1 writing
        them out for the pages CI job to pick up) can't drift.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)]$GitConfig)

    return [ordered]@{
        branch           = if ($GitConfig.Contains('branch') -and $GitConfig.branch) { $GitConfig.branch } else { 'main' }
        pathPrefix       = if ($GitConfig.Contains('pathPrefix') -and $GitConfig.pathPrefix) { $GitConfig.pathPrefix } else { 'objects' }
        commitUserName   = if ($GitConfig.Contains('commitUserName') -and $GitConfig.commitUserName) { $GitConfig.commitUserName } else { 'SyncSQL Bot' }
        commitUserEmail  = if ($GitConfig.Contains('commitUserEmail') -and $GitConfig.commitUserEmail) { $GitConfig.commitUserEmail } else { 'syncsql-bot@example.com' }
        commitMessage    = if ($GitConfig.Contains('commitMessage') -and $GitConfig.commitMessage) { $GitConfig.commitMessage } else { 'chore(sync): update database objects' }
    }
}

function New-SyncSqlGitAskPass {
    <#
        Writes a tiny askpass helper and returns its path plus the two env
        var names it reads from, so the caller can set them without ever
        putting the token on a command line.
    #>
    [CmdletBinding()]
    param()

    $scriptPath = Join-Path ([IO.Path]::GetTempPath()) "syncsql-askpass-$PID.sh"
    $content = @'
#!/bin/sh
case "$1" in
    Username*) printf '%s' "$SYNCSQL_GIT_USERNAME" ;;
    *)         printf '%s' "$SYNCSQL_GIT_PASSWORD" ;;
esac
'@
    Set-Content -LiteralPath $scriptPath -Value $content -NoNewline:$false -Encoding ascii
    # $IsWindows doesn't exist before PowerShell 6 - Windows PowerShell 5.1
    # only ever runs on Windows, so treat "PS5.x" as "definitely Windows"
    # rather than reading an automatic variable that isn't there yet.
    $onWindows = $PSVersionTable.PSVersion.Major -lt 6 -or $IsWindows
    if (-not $onWindows) {
        & chmod 700 $scriptPath
    }
    return $scriptPath
}

function Invoke-SyncSqlGit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $gitArgs = @()
    if ($WorkingDirectory) { $gitArgs += @('-C', $WorkingDirectory) }
    $gitArgs += $Arguments

    $output = & git @gitArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed (exit $LASTEXITCODE):`n$($output -join "`n")"
    }
    return $output
}

function Publish-SyncSqlToGit {
    <#
        Full clone -> sync -> commit -> push flow.
        Returns $true if a commit was pushed, $false if there was nothing
        to publish (extraction produced no diff since the last run).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$GitConfig,
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$WorkDir,
        [string]$Summary = '',
        # How many commits to clone. Keep this at the default (1) unless a
        # caller needs real git history in $WorkDir afterwards (e.g. to mine
        # it for a catalog before committing) - a deep clone is slower and
        # heavier for the common case, which never looks at history.
        [int]$CloneDepth = 1,
        # Invoked as (WorkDir, PathPrefix) after the staged tree has been
        # copied into $WorkDir/$PathPrefix but before `git add`/commit, so a
        # caller can add more files (e.g. a generated catalog.json) into the
        # same commit. Use .GetNewClosure() when building this scriptblock
        # so it doesn't depend on this function's local scope.
        [scriptblock]$PostSyncHook
    )

    $defaults = Get-SyncSqlGitDefaults -GitConfig $GitConfig
    $remoteUrl = Get-SyncSqlTargetRepoUrl -ConfigRemoteUrl $GitConfig.remoteUrl
    $branch = $defaults.branch
    $pathPrefix = $defaults.pathPrefix
    $commitUserName = $defaults.commitUserName
    $commitUserEmail = $defaults.commitUserEmail
    $commitMessage = $defaults.commitMessage

    if (Test-Path -LiteralPath $WorkDir) {
        Remove-Item -LiteralPath $WorkDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null

    $askPassPath = New-SyncSqlGitAskPass
    $previousAskPass = $env:GIT_ASKPASS
    $previousTerminalPrompt = $env:GIT_TERMINAL_PROMPT
    $env:GIT_ASKPASS = $askPassPath
    $env:GIT_TERMINAL_PROMPT = '0'
    $env:SYNCSQL_GIT_USERNAME = 'oauth2'
    $env:SYNCSQL_GIT_PASSWORD = $Token

    try {
        Write-SyncSqlLog "Cloning target repository (branch '$branch', depth $CloneDepth)"
        try {
            Invoke-SyncSqlGit -Arguments @('clone', '--branch', $branch, '--single-branch', '--depth', "$CloneDepth", $remoteUrl, $WorkDir) | Out-Null
        }
        catch {
            Write-SyncSqlLog "Branch '$branch' not found on remote yet; cloning default branch and creating it." -Level WARN
            Invoke-SyncSqlGit -Arguments @('clone', '--depth', "$CloneDepth", $remoteUrl, $WorkDir) | Out-Null
            Invoke-SyncSqlGit -Arguments @('checkout', '-B', $branch) -WorkingDirectory $WorkDir | Out-Null
        }

        Invoke-SyncSqlGit -Arguments @('config', 'user.name', $commitUserName) -WorkingDirectory $WorkDir | Out-Null
        Invoke-SyncSqlGit -Arguments @('config', 'user.email', $commitUserEmail) -WorkingDirectory $WorkDir | Out-Null

        $targetDir = Join-Path $WorkDir $pathPrefix
        # Wipe and repopulate so objects dropped from the source database
        # (or excluded by an updated regex) show up as deletions in git.
        if (Test-Path -LiteralPath $targetDir) {
            Remove-Item -LiteralPath $targetDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

        if (Test-Path -LiteralPath $StagingRoot) {
            Get-ChildItem -LiteralPath $StagingRoot -Force | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $targetDir -Recurse -Force
            }
        }

        if ($PostSyncHook) {
            Write-SyncSqlLog "Running post-sync hook before commit"
            & $PostSyncHook $WorkDir $pathPrefix
        }

        Invoke-SyncSqlGit -Arguments @('add', '-A') -WorkingDirectory $WorkDir | Out-Null
        $status = Invoke-SyncSqlGit -Arguments @('status', '--porcelain') -WorkingDirectory $WorkDir

        if ([string]::IsNullOrWhiteSpace(($status -join ''))) {
            Write-SyncSqlLog "No changes detected; nothing to publish."
            return $false
        }

        $fullMessage = if ($Summary) { "$commitMessage`n`n$Summary" } else { $commitMessage }
        Invoke-SyncSqlGit -Arguments @('commit', '-m', $fullMessage) -WorkingDirectory $WorkDir | Out-Null

        Write-SyncSqlLog "Pushing to '$branch'"
        Invoke-SyncSqlGit -Arguments @('push', 'origin', "HEAD:$branch") -WorkingDirectory $WorkDir | Out-Null

        return $true
    }
    finally {
        if ($null -ne $previousAskPass) { $env:GIT_ASKPASS = $previousAskPass } else { Remove-Item Env:GIT_ASKPASS -ErrorAction SilentlyContinue }
        if ($null -ne $previousTerminalPrompt) { $env:GIT_TERMINAL_PROMPT = $previousTerminalPrompt } else { Remove-Item Env:GIT_TERMINAL_PROMPT -ErrorAction SilentlyContinue }
        Remove-Item Env:SYNCSQL_GIT_USERNAME -ErrorAction SilentlyContinue
        Remove-Item Env:SYNCSQL_GIT_PASSWORD -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $askPassPath -Force -ErrorAction SilentlyContinue
    }
}

Export-ModuleMember -Function @(
    'Get-SyncSqlTargetRepoUrl',
    'Get-SyncSqlGitDefaults',
    'Invoke-SyncSqlGit',
    'Publish-SyncSqlToGit'
)
