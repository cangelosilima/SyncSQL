#Requires -Version 5.1
<#
    SyncSql.Common.psm1
    Shared helpers: config loading, regex include/exclude filtering,
    filesystem-safe naming, static (non-timestamped) file headers, logging.

    Targets Windows PowerShell 5.1 (.NET Framework) as the floor, so it
    deliberately avoids PowerShell 6/7-only surface: no ??/?:/??=
    operators, no ConvertFrom-Json -AsHashtable, no ConvertTo-Json
    -AsArray, no [IO.Path]::GetRelativePath (added in .NET Core, absent
    from .NET Framework), no Join-Path calls with more than one ChildPath
    segment (-AdditionalChildPath is PS6+). Everything still runs fine on
    PowerShell 7 too - this is a floor, not a ceiling.
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

function ConvertTo-SyncSqlOrderedHashtable {
    <#
        Recursively converts the PSCustomObject/array tree ConvertFrom-Json
        produces into ordered hashtables/arrays instead - the PS5.1-safe
        equivalent of ConvertFrom-Json -AsHashtable (a PowerShell 6+ only
        switch). Gives every level of the config the same
        .Contains()/indexer shape the rest of this codebase expects, on
        every supported PowerShell version.
    #>
    [CmdletBinding()]
    param([Parameter(ValueFromPipeline)]$InputObject)

    process {
        if ($null -eq $InputObject) { return $null }

        if ($InputObject -is [string]) { return $InputObject }

        if ($InputObject -is [System.Collections.IEnumerable]) {
            $list = [System.Collections.Generic.List[object]]::new()
            foreach ($item in $InputObject) { $list.Add((ConvertTo-SyncSqlOrderedHashtable -InputObject $item)) }
            return $list.ToArray()
        }

        if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
            $hash = [ordered]@{}
            foreach ($prop in $InputObject.PSObject.Properties) {
                $hash[$prop.Name] = ConvertTo-SyncSqlOrderedHashtable -InputObject $prop.Value
            }
            return $hash
        }

        return $InputObject
    }
}

function Import-SyncSqlConfig {
    <#
        Loads and lightly validates the JSON inventory/filter config.
        ConvertTo-SyncSqlOrderedHashtable gives every object/array in the
        file the same hashtable-with-.Contains()-and-indexer shape the
        rest of this codebase expects, no external module required and no
        PowerShell-6+-only -AsHashtable switch.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Config file not found: $Path"
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    $config = ConvertTo-SyncSqlOrderedHashtable -InputObject (ConvertFrom-Json -InputObject $raw)

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

function Get-SyncSqlRelativePath {
    <#
        PS5.1-safe replacement for [IO.Path]::GetRelativePath (added in
        .NET Core; not present in the .NET Framework Windows PowerShell
        5.1 runs on). $FullPath is expected to live under $Root; the
        result uses forward slashes regardless of platform, matching what
        the rest of this codebase (node ids, JSON paths) expects.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$FullPath
    )

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $targetFull = [IO.Path]::GetFullPath($FullPath)

    if ($targetFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        $relative = $targetFull.Substring($rootFull.Length).TrimStart('\', '/')
    }
    else {
        $relative = $targetFull
    }

    return ($relative -replace '\\', '/')
}

function Set-SyncSqlUtf8NoBomContent {
    <#
        Writes $Content to $Path as UTF-8 without a byte-order mark.
        Set-Content/Out-File's -Encoding utf8 always *adds* a BOM on
        Windows PowerShell 5.1 (.NET Framework) - PS6+'s "utf8NoBOM"
        encoding name doesn't exist there - so every extracted/generated
        file would gain a 3-byte BOM prefix the moment this project runs
        under 5.1, showing up as a spurious full-file diff on the very
        next run for every single object. Writing via .NET's UTF8Encoding
        with encoderShouldEmitUTF8Identifier=$false sidesteps that on
        every supported PowerShell version.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Content
    )

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
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
        # 'mssql' or 'oracle' - which extraction backend produced this object.
        # Mandatory (rather than inferred later from path/type) so every
        # newly-written object is unambiguously taggable for engine-specific
        # lineage parsing (Build-Catalog.ps1) without guessing from object
        # type names that overlap between engines (Tables, Views, ...).
        [Parameter(Mandatory)][ValidateSet('mssql', 'oracle')][string]$Engine,
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

    # [IO.Path]::Combine(string[]) rather than Join-Path @segments: PS5.1's
    # Join-Path only accepts a single -Path/-ChildPath pair (-AdditionalChildPath
    # was added in PS6+), so splatting more than two segments into it fails.
    $dir = [IO.Path]::Combine($segments)
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
        "-- Engine:   $Engine"
    ) -join "`n"
    $header += "`n`n"

    $body = if ($null -eq $Definition) { '' } else { $Definition.Trim() }
    $content = $header + $body + "`n"

    Set-SyncSqlUtf8NoBomContent -Path $filePath -Content $content
    return $filePath
}

function New-SyncSqlMetricsSnapshotFile {
    <#
        Writes one run's volatile operational snapshot (row counts, index
        fragmentation/usage, optimizer statistics properties - see
        SyncSql.MsSql.psm1's Get-SyncSqlMsSqlMetricsSnapshot) for a single
        object to $MetricsRoot, a staging area kept entirely separate from
        -StagingRoot/the object's own .sql file.

        Deliberately mirrors New-SyncSqlObjectFile's path scheme
        (server/database/type/[schema/]object) so a metrics snapshot's
        relative path (minus extension) is exactly the same id
        Build-Catalog.ps1 assigns the corresponding catalog node - trivial
        correlation, no separate id-mapping needed. Unlike the object's own
        file, this is never committed as-is: Update-MetricsHistory.ps1
        appends it into a growing history array kept outside the
        git.pathPrefix tree that gets wiped/replaced every run, so the
        object's own version history stays free of daily-changing noise
        while the metrics history accumulates on its own.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$MetricsRoot,
        [Parameter(Mandatory)][string]$ServerName,
        [Parameter(Mandatory)][string]$DatabaseName,
        [string]$SchemaName,
        [Parameter(Mandatory)][string]$ObjectType,
        [Parameter(Mandatory)][string]$ObjectName,
        [Parameter(Mandatory)]$Snapshot
    )

    $segments = @(
        $MetricsRoot,
        (ConvertTo-SyncSqlSafeFileName $ServerName),
        (ConvertTo-SyncSqlSafeFileName $DatabaseName),
        (ConvertTo-SyncSqlSafeFileName $ObjectType)
    )
    if (-not [string]::IsNullOrWhiteSpace($SchemaName)) {
        $segments += (ConvertTo-SyncSqlSafeFileName $SchemaName)
    }

    # See New-SyncSqlObjectFile's comment: PS5.1's Join-Path can't take more
    # than one ChildPath segment, so multi-segment paths go through
    # [IO.Path]::Combine instead.
    $dir = [IO.Path]::Combine($segments)
    New-Item -ItemType Directory -Path $dir -Force | Out-Null

    $filePath = Join-Path $dir ("{0}.json" -f (ConvertTo-SyncSqlSafeFileName $ObjectName))
    $json = $Snapshot | ConvertTo-Json -Depth 10 -Compress
    Set-SyncSqlUtf8NoBomContent -Path $filePath -Content $json
    return $filePath
}

function Add-SyncSqlSectionBlock {
    <#
        Appends a "-- === Title ===" marked section to an object's definition
        text, generic across every appended section (Foreign Keys, Check
        Constraints, Indexes, Statistics, Grants, Columns...).
        Build-Catalog.ps1's section parser splits on that marker, so any
        title works as long as it's unique within one object's file. Shared
        between the MSSQL and Oracle extraction backends.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Definition,
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)]$SectionIndex,
        [Parameter(Mandatory)][string]$Key
    )

    if (-not $SectionIndex.ContainsKey($Key)) { return $Definition }

    $block = @('', "-- === $Title ===") + $SectionIndex[$Key]
    return $Definition + "`n" + ($block -join "`n")
}

Export-ModuleMember -Function @(
    'Write-SyncSqlLog',
    'ConvertTo-SyncSqlOrderedHashtable',
    'Import-SyncSqlConfig',
    'Test-SyncSqlNameAllowed',
    'Get-SyncSqlEffectiveFilters',
    'Get-SyncSqlAllowedObjectTypes',
    'Get-SyncSqlRelativePath',
    'Set-SyncSqlUtf8NoBomContent',
    'ConvertTo-SyncSqlSafeFileName',
    'New-SyncSqlObjectFile',
    'Add-SyncSqlSectionBlock',
    'New-SyncSqlMetricsSnapshotFile'
)
