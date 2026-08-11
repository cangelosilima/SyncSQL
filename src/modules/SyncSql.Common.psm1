#Requires -Version 7.0
<#
    SyncSql.Common.psm1
    Shared helpers: config loading, regex include/exclude filtering,
    filesystem-safe naming, static (non-timestamped) file headers, logging.
#>

Set-StrictMode -Version Latest

function Write-SyncSqlLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('INFO', 'WARN', 'ERROR', 'DEBUG')][string]$Level = 'INFO'
    )

    $prefix = switch ($Level) {
        'ERROR' { "`e[31m[ERROR]`e[0m" }
        'WARN'  { "`e[33m[WARN] `e[0m" }
        'DEBUG' { "`e[90m[DEBUG]`e[0m" }
        default { "`e[36m[INFO] `e[0m" }
    }

    $line = "$prefix $Message"
    if ($Level -eq 'ERROR') {
        Write-Error $line -ErrorAction Continue
    }
    elseif ($Level -eq 'WARN') {
        Write-Warning $Message
    }
    else {
        Write-Host $line
    }
}

function Import-SyncSqlConfig {
    <#
        Loads and lightly validates the YAML inventory/filter config.
        Requires the `powershell-yaml` module to already be importable.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Config file not found: $Path"
    }

    Import-Module powershell-yaml -ErrorAction Stop

    $raw = Get-Content -LiteralPath $Path -Raw
    $config = ConvertFrom-Yaml -Yaml $raw -Ordered

    if (-not $config.servers -or $config.servers.Count -eq 0) {
        throw "Config file '$Path' does not define any servers."
    }

    foreach ($server in $config.servers) {
        foreach ($required in @('name', 'type', 'host', 'credentialsVariablePrefix')) {
            if (-not $server.Contains($required) -or [string]::IsNullOrWhiteSpace([string]$server[$required])) {
                throw "Server entry is missing required key '$required': $($server | ConvertTo-Json -Compress)"
            }
        }
        if ($server.type -notin @('mssql', 'oracle')) {
            throw "Server '$($server.name)' has unsupported type '$($server.type)'. Expected 'mssql' or 'oracle'."
        }
    }

    return $config
}

function Test-SyncSqlNameAllowed {
    <#
        Returns $true when $Name passes the include/exclude regex filter.
        $Filter is a hashtable/ordered-dict with optional 'include' and
        'exclude' arrays of regex strings. Exclude always wins.
        A missing or empty 'include' list means "include everything".
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Name,
        $Filter
    )

    $includePatterns = @()
    $excludePatterns = @()

    if ($null -ne $Filter) {
        if ($Filter.Contains('include') -and $Filter['include']) { $includePatterns = @($Filter['include']) }
        if ($Filter.Contains('exclude') -and $Filter['exclude']) { $excludePatterns = @($Filter['exclude']) }
    }

    foreach ($pattern in $excludePatterns) {
        if ($Name -match $pattern) { return $false }
    }

    if ($includePatterns.Count -eq 0) { return $true }

    foreach ($pattern in $includePatterns) {
        if ($Name -match $pattern) { return $true }
    }

    return $false
}

function Get-SyncSqlEffectiveFilters {
    <#
        Merges config.defaults with a per-server override block.
        Any key present on the server replaces (not merges) the default's
        value for that key, keeping the mental model simple: a server that
        specifies 'schemas' fully owns its schema filter.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Defaults,
        [Parameter(Mandatory)]$Server
    )

    $effective = [ordered]@{}
    foreach ($key in @('databases', 'schemas', 'objectNames', 'objectTypes')) {
        if ($Server.Contains($key) -and $null -ne $Server[$key]) {
            $effective[$key] = $Server[$key]
        }
        elseif ($Defaults -and $Defaults.Contains($key)) {
            $effective[$key] = $Defaults[$key]
        }
        else {
            $effective[$key] = $null
        }
    }
    return $effective
}

function Get-SyncSqlAllowedObjectTypes {
    <#
        objectTypes in config is a plain list (not include/exclude), since
        object type sets are small and enumerable. Returns it as a string
        array, defaulting to an empty list (nothing exported) if unset.
    #>
    [CmdletBinding()]
    param($Filters)

    if ($Filters.Contains('objectTypes') -and $Filters['objectTypes']) {
        return @($Filters['objectTypes'])
    }
    return @()
}

function ConvertTo-SyncSqlSafeFileName {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Name
    )

    $invalid = [IO.Path]::GetInvalidFileNameChars() -join ''
    $pattern = "[{0}]" -f [regex]::Escape($invalid)
    return ($Name -replace $pattern, '_')
}

function New-SyncSqlObjectFile {
    <#
        Writes a single extracted object to disk under a deterministic,
        diff-friendly path. Content is a static header (no timestamps, so
        re-running the export doesn't create noise when nothing changed)
        followed by the trimmed object definition.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][string]$ServerName,
        [Parameter(Mandatory)][string]$DatabaseName,
        [string]$SchemaName,
        [Parameter(Mandatory)][string]$ObjectType,
        [Parameter(Mandatory)][string]$ObjectName,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Definition,
        [string]$Extension = 'sql'
    )

    $segments = @(
        $StagingRoot,
        (ConvertTo-SyncSqlSafeFileName $ServerName),
        (ConvertTo-SyncSqlSafeFileName $DatabaseName),
        (ConvertTo-SyncSqlSafeFileName $ObjectType)
    )
    if (-not [string]::IsNullOrWhiteSpace($SchemaName)) {
        $segments += (ConvertTo-SyncSqlSafeFileName $SchemaName)
    }

    $dir = Join-Path @segments
    New-Item -ItemType Directory -Path $dir -Force | Out-Null

    $fileName = "{0}.{1}" -f (ConvertTo-SyncSqlSafeFileName $ObjectName), $Extension
    $filePath = Join-Path $dir $fileName

    $qualifiedName = if ($SchemaName) { "$SchemaName.$ObjectName" } else { $ObjectName }
    $header = @(
        "-- Auto-generated by SyncSQL. Do not edit manually; changes will be overwritten."
        "-- Server:   $ServerName"
        "-- Database: $DatabaseName"
        "-- Type:     $ObjectType"
        "-- Object:   $qualifiedName"
    ) -join "`n"
    $header += "`n`n"

    $body = ($Definition ?? '').Trim()
    $content = $header + $body + "`n"

    Set-Content -LiteralPath $filePath -Value $content -NoNewline -Encoding utf8
    return $filePath
}

Export-ModuleMember -Function @(
    'Write-SyncSqlLog',
    'Import-SyncSqlConfig',
    'Test-SyncSqlNameAllowed',
    'Get-SyncSqlEffectiveFilters',
    'Get-SyncSqlAllowedObjectTypes',
    'ConvertTo-SyncSqlSafeFileName',
    'New-SyncSqlObjectFile'
)
