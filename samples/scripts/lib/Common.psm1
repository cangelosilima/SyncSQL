<#
.SYNOPSIS
    Shared plumbing for samples/scripts/*.ps1 - the Windows/PowerShell twin of lib/common.sh.
.NOTES
    Works on Windows PowerShell 5.1 and PowerShell 7+. Unlike the bash side it needs
    no jq: ConvertFrom-Json is built in.
#>

$script:SamplesDir = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$script:RepoDir    = (Resolve-Path (Join-Path $script:SamplesDir '..')).Path
$script:CacheDir   = Join-Path $script:SamplesDir '.cache'
$script:SourcesDir = Join-Path $script:CacheDir 'sources'
$script:BackupsDir = Join-Path $script:CacheDir 'backups'
$script:WorkDir    = Join-Path $script:CacheDir 'work'
$script:Manifest   = Join-Path $script:SamplesDir 'samples.json'
$script:ComposeFile= Join-Path $script:SamplesDir 'docker/docker-compose.yml'
$script:EnvFile    = Join-Path $script:SamplesDir '.env'

function Get-SamplesPath { [CmdletBinding()] param() $script:SamplesDir }
function Get-RepoPath    { [CmdletBinding()] param() $script:RepoDir }
function Get-CachePath   { [CmdletBinding()] param() $script:CacheDir }
function Get-SourcesPath { [CmdletBinding()] param() $script:SourcesDir }
function Get-BackupsPath { [CmdletBinding()] param() $script:BackupsDir }

function Write-Step { param([string]$Message) Write-Host "    - $Message" -ForegroundColor DarkGray }
function Write-Ok   { param([string]$Message) Write-Host "    OK $Message" -ForegroundColor Green }
function Write-Note { param([string]$Message) Write-Host "==> $Message" -ForegroundColor Blue }
function Write-Warn { param([string]$Message) Write-Warning $Message }

function Assert-Command {
    param([Parameter(Mandatory)][string]$Name, [string]$Hint)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "'$Name' is required but was not found on PATH.$(if ($Hint) { " $Hint" })"
    }
}

function Assert-Prerequisites {
    Assert-Command docker 'Install Docker Desktop: https://docs.docker.com/get-docker/'
    docker compose version *> $null
    if ($LASTEXITCODE -ne 0) { throw "The Docker Compose v2 plugin is required ('docker compose version' failed)." }
    docker info *> $null
    if ($LASTEXITCODE -ne 0) { throw 'The Docker daemon is not reachable. Start Docker and try again.' }
}

# samples/.env holds the container passwords and host ports; created from the
# checked-in example on first run. Returns them as a hashtable and also exports
# them into the process so `docker compose --env-file` and this script agree.
function Import-SampleEnvironment {
    if (-not (Test-Path $script:EnvFile)) {
        Copy-Item (Join-Path $script:SamplesDir 'docker/.env.example') $script:EnvFile
        Write-Warn "Created $script:EnvFile from docker/.env.example - edit it if you want different passwords or ports."
    }

    $settings = @{}
    foreach ($line in Get-Content $script:EnvFile) {
        if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
        $name, $value = $line -split '=', 2
        $settings[$name.Trim()] = $value.Trim()
        Set-Item -Path "Env:$($name.Trim())" -Value $value.Trim()
    }

    foreach ($required in 'MSSQL_SA_PASSWORD', 'ORACLE_PASSWORD') {
        if (-not $settings[$required]) { throw "$required is not set in samples/.env" }
    }
    if (-not $settings['ORACLE_SAMPLE_PASSWORD']) { $settings['ORACLE_SAMPLE_PASSWORD'] = $settings['ORACLE_PASSWORD'] }
    if (-not $settings['MSSQL_PORT'])  { $settings['MSSQL_PORT']  = '14330' }
    if (-not $settings['ORACLE_PORT']) { $settings['ORACLE_PORT'] = '15210' }
    $settings
}

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    & docker compose --project-directory (Join-Path $script:SamplesDir 'docker') -f $script:ComposeFile --env-file $script:EnvFile @Arguments
}

# --- manifest ---------------------------------------------------------------

function Get-SampleManifest {
    Get-Content -Raw -Encoding UTF8 $script:Manifest | ConvertFrom-Json
}

function Select-Samples {
    param(
        [Parameter(Mandatory)]$Manifest,
        [ValidateSet('mssql', 'oracle', 'all')][string]$Engine = 'all',
        [ValidateSet('standard', 'heavy', 'all')][string]$Tier = 'standard',
        [string[]]$Only = @(),
        [string[]]$Skip = @()
    )
    $tiers = if ($Tier -eq 'all') { @('standard', 'heavy') } else { @($Tier) }
    $Manifest.samples |
        Sort-Object order |
        Where-Object { $Engine -eq 'all' -or $_.engine -eq $Engine } |
        Where-Object { $_.provision.type -ne 'none' } |
        Where-Object { $Only.Count -eq 0 -or $Only -contains $_.id } |
        Where-Object { $Skip -notcontains $_.id } |
        Where-Object { $Only.Count -gt 0 -or $tiers -contains $_.tier }
}

# --- sources ----------------------------------------------------------------

function Sync-UpstreamSource {
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][string]$Name)

    $source = $Manifest.sources.$Name
    $dest = Join-Path $script:SourcesDir $Name
    $sparse = @($source.sparsePaths)
    $mode = if ($source.PSObject.Properties['sparseMode']) { $source.sparseMode } else { 'cone' }

    if (Test-Path (Join-Path $dest '.git')) {
        Write-Step "updating $Name"
        & git -C $dest fetch --quiet --depth 1 origin $source.ref
        if ($LASTEXITCODE -ne 0) { throw "git fetch failed for $Name" }
        & git -C $dest checkout --quiet --detach FETCH_HEAD
    }
    else {
        Write-Step "cloning $Name ($($source.repo))"
        New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
        # --filter=tree:0 plus a sparse checkout is what keeps sql-server-samples
        # from being a multi-gigabyte clone.
        $cloneArgs = @('clone', '--quiet', '--depth', '1', '--branch', $source.ref, '--filter=tree:0')
        if ($sparse.Count -gt 0) { $cloneArgs += '--sparse' }
        & git @cloneArgs $source.repo $dest
        if ($LASTEXITCODE -ne 0) { throw "git clone failed for $Name" }
    }

    if ($sparse.Count -gt 0) {
        $coneArg = if ($mode -eq 'no-cone') { '--no-cone' } else { '--cone' }
        & git -C $dest sparse-checkout set $coneArg @sparse | Out-Null
        & git -C $dest checkout --quiet
    }
    Write-Ok "$Name at $(& git -C $dest rev-parse --short HEAD)"
}

function Save-SampleBackup {
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][string]$Name)

    $download = $Manifest.downloads.$Name
    $dest = Join-Path $script:BackupsDir $Name

    if (Test-Path $dest) {
        if ((Get-Item $dest).Length -eq $download.sizeBytes) {
            Write-Ok "$Name already downloaded"
            return
        }
        Write-Warn "$Name is the wrong size - re-downloading"
    }

    New-Item -ItemType Directory -Force -Path $script:BackupsDir | Out-Null
    Write-Step "downloading $Name ($([math]::Round($download.sizeBytes / 1MB)) MB)"
    # The default progress bar makes Invoke-WebRequest an order of magnitude
    # slower on large files in Windows PowerShell.
    $previous = $ProgressPreference
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri $download.url -OutFile "$dest.partial" -UseBasicParsing
        Move-Item -Force "$dest.partial" $dest
    }
    finally { $ProgressPreference = $previous }
    Write-Ok $Name
}

# --- SQL Server -------------------------------------------------------------

$script:Sqlcmd = '/opt/mssql-tools/bin/sqlcmd'

# Runs a .sql file that lives under samples/ (mounted read-only at /samples in
# the tools container). -b fails on the first error, -I turns QUOTED_IDENTIFIER
# on (temporal tables and filtered indexes need it), -f 65001 reads the file as
# UTF-8. The password comes from SQLCMDPASSWORD in the container environment.
function Invoke-MssqlFile {
    param([Parameter(Mandatory)][string]$RelativePath, [string]$Database = 'master')
    Invoke-Compose run --rm -T mssql-tools -c "$script:Sqlcmd -S mssql,1433 -U sa -d '$Database' -b -I -f 65001 -i '/samples/$RelativePath'"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed for $RelativePath" }
}

function Invoke-MssqlScript {
    param([Parameter(Mandatory)][string]$Sql, [string]$Database = 'master', [string]$Name = 'generated')
    New-Item -ItemType Directory -Force -Path $script:WorkDir | Out-Null
    Set-Content -Path (Join-Path $script:WorkDir "$Name.sql") -Value $Sql -Encoding UTF8 -NoNewline
    Invoke-MssqlFile ".cache/work/$Name.sql" $Database
}

function Invoke-MssqlQuery {
    param([Parameter(Mandatory)][string]$Sql, [string]$Name = 'query')
    New-Item -ItemType Directory -Force -Path $script:WorkDir | Out-Null
    Set-Content -Path (Join-Path $script:WorkDir "$Name.sql") -Value "SET NOCOUNT ON;`n$Sql" -Encoding UTF8
    Invoke-Compose run --rm -T mssql-tools -c "$script:Sqlcmd -S mssql,1433 -U sa -b -I -h -1 -W -s '|' -i '/samples/.cache/work/$Name.sql'"
}

function Wait-Mssql {
    param([int]$Attempts = 60, [string]$Port = '14330')
    Write-Step "waiting for SQL Server on port $Port"
    for ($i = 0; $i -lt $Attempts; $i++) {
        Invoke-MssqlQuery 'SELECT 1;' 'readiness' *> $null
        if ($LASTEXITCODE -eq 0) { Write-Ok 'SQL Server is accepting queries'; return }
        Start-Sleep -Seconds 5
    }
    throw "SQL Server did not become ready after $($Attempts * 5)s. Try 'docker compose -f $script:ComposeFile logs mssql'."
}

function New-MssqlDatabase {
    param([Parameter(Mandatory)][string]$Database)
    Invoke-MssqlScript "IF DB_ID(N'$Database') IS NULL CREATE DATABASE [$Database];" 'master' 'create-db'
}

# Restores without assuming the paths the backup was taken from: FILELISTONLY
# reports the logical files and every one of them gets a MOVE into this
# container's data directory. Type D is data, L is log, S is a filestream /
# memory-optimized container (a directory, not a file).
function Restore-MssqlBackup {
    param([Parameter(Mandatory)][string]$Backup, [Parameter(Mandatory)][string]$Database)

    $disk = "/var/opt/mssql/backup/$Backup"
    Write-Step "reading file list from $Backup"
    $rows = Invoke-MssqlQuery "RESTORE FILELISTONLY FROM DISK = N'$disk';" 'filelistonly'

    $moves = New-Object System.Collections.Generic.List[string]
    foreach ($row in $rows) {
        if ($row -notmatch '\|') { continue }
        $fields = $row -split '\|'
        if ($fields.Count -lt 3) { continue }
        $logical = $fields[0].Trim()
        $type = $fields[2].Trim()
        if (-not $logical -or -not $type) { continue }

        $safe = ($logical -replace '[^A-Za-z0-9_.-]', '_')
        $target = switch ($type) {
            'D' { "/var/opt/mssql/data/${Database}__$safe.mdf" }
            'L' { "/var/opt/mssql/data/${Database}__$safe.ldf" }
            'S' { "/var/opt/mssql/data/${Database}__$safe" }
            default { $null }
        }
        if (-not $target) { Write-Warn "unknown backup file type '$type' for '$logical' - skipping its MOVE"; continue }
        $moves.Add(", MOVE N'$logical' TO N'$target'")
    }
    if ($moves.Count -eq 0) { throw "RESTORE FILELISTONLY returned no usable rows for $Backup." }

    Write-Step "restoring $Backup as $Database"
    $sql = @"
IF DB_ID(N'$Database') IS NOT NULL
BEGIN
    ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
END
RESTORE DATABASE [$Database] FROM DISK = N'$disk'
WITH REPLACE, RECOVERY, STATS = 10$($moves -join '');
ALTER DATABASE [$Database] SET MULTI_USER;
-- The backups predate this container's engine version; without this the restored
-- database keeps an old compatibility level and some demo syntax (and SyncSQL's
-- own catalog queries) behave differently than expected.
DECLARE @level tinyint = (SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int) * 10);
EXEC('ALTER DATABASE [$Database] SET COMPATIBILITY_LEVEL = ' + @level);
"@
    Invoke-MssqlScript $sql 'master' "restore-$Database"
}

# --- Oracle -----------------------------------------------------------------

function Wait-Oracle {
    param([int]$Attempts = 90, [string]$Port = '15210')
    Write-Step "waiting for Oracle on port $Port"
    for ($i = 0; $i -lt $Attempts; $i++) {
        $status = & docker inspect --format '{{.State.Health.Status}}' syncsql-samples-oracle 2>$null
        if ($status -eq 'healthy') { Write-Ok 'Oracle is healthy'; return }
        Start-Sleep -Seconds 5
    }
    throw "Oracle did not become healthy after $($Attempts * 5)s. Try 'docker compose -f $script:ComposeFile logs oracle'."
}

# Runs one of the upstream <schema>_install.sql drivers. Those are SQL*Plus
# programs - @@ includes, SPOOL, ACCEPT prompts - so they run through sqlplus
# inside the container.
#
# -L attempts the connection once and takes the password from stdin, keeping it
# out of the container's process list. The four answers are: SYSTEM's password,
# the new schema's password, the tablespace (blank accepts the PDB default) and
# whether to overwrite an existing schema. --workdir /tmp because the scripts
# SPOOL a log into the working directory and /samples is mounted read-only.
function Invoke-OracleInstallScript {
    param(
        [Parameter(Mandatory)][string]$WorkDir,
        [Parameter(Mandatory)][string]$Script,
        [Parameter(Mandatory)][string]$SystemPassword,
        [Parameter(Mandatory)][string]$SchemaPassword
    )
    $path = "/samples/.cache/sources/db-sample-schemas/$WorkDir/$Script"
    $answers = "$SystemPassword`n$SchemaPassword`n`nYES`n"
    $answers | Invoke-Compose exec -T --workdir /tmp oracle sqlplus -s -L 'system@localhost:1521/FREEPDB1' "@$path"
    if ($LASTEXITCODE -ne 0) { throw "sqlplus failed for $WorkDir/$Script" }
}

Export-ModuleMember -Function *
