#Requires -Version 5.1
<#
    SyncSql.MsSql.psm1
    Microsoft SQL Server extraction backend. Relies on the `SqlServer`
    PowerShell module (Invoke-Sqlcmd) for connectivity so no native client
    libraries are required.
#>

Set-StrictMode -Version Latest

# Maps sys.objects.type codes to the config's objectTypes vocabulary.
$script:SyncSqlMsSqlTypeMap = @{
    'P'  = 'StoredProcedures'
    'V'  = 'Views'
    'TR' = 'Triggers'
    'FN' = 'Functions'
    'IF' = 'Functions'
    'TF' = 'Functions'
}

function Invoke-SyncSqlMsSqlQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ServerHost,
        [int]$Port = 1433,
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Username,
        [Parameter(Mandatory)][string]$Password,
        [bool]$Encrypt = $true,
        [bool]$TrustServerCertificate = $false,
        [Parameter(Mandatory)][string]$Query,
        [int]$QueryTimeoutSeconds = 120
    )

    $credential = [PSCredential]::new($Username, (ConvertTo-SecureString -String $Password -AsPlainText -Force))

    $params = @{
        ServerInstance          = "$ServerHost,$Port"
        Database                = $Database
        Credential              = $credential
        Query                   = $Query
        QueryTimeout            = $QueryTimeoutSeconds
        ConnectionTimeout       = 30
        TrustServerCertificate  = $TrustServerCertificate
        OutputSqlErrors         = $true
        ErrorAction             = 'Stop'
    }
    if ($Encrypt) { $params['Encrypt'] = 'Mandatory' } else { $params['Encrypt'] = 'Optional' }

    return Invoke-Sqlcmd @params
}

function Get-SyncSqlMsSqlDatabases {
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$ConnectionInfo)

    $query = "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name;"
    $rows = Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database 'master' -Query $query
    return @($rows | ForEach-Object { $_.name })
}

function Get-SyncSqlMsSqlSchemas {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT s.name AS SchemaName, dp.name AS OwnerName
FROM sys.schemas s
JOIN sys.database_principals dp ON dp.principal_id = s.principal_id
ORDER BY s.name;
"@
    return Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
}

function Get-SyncSqlMsSqlModuleObjects {
    <#
        Returns procedures, views, functions and DML triggers in a single
        pass, since they all live in sys.sql_modules.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    s.name  AS SchemaName,
    o.name  AS ObjectName,
    o.type  AS TypeCode,
    m.definition AS Definition
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
WHERE o.type IN ('P', 'V', 'TR', 'FN', 'IF', 'TF')
  AND o.is_ms_shipped = 0
ORDER BY s.name, o.name;
"@
    return Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
}

function Get-SyncSqlMsSqlTables {
    <#
        MSSQL does not store a reusable "CREATE TABLE" text the way it does
        for procedures/views, so this rebuilds an approximate DDL from
        catalog views: columns, data types, identity, defaults and the
        primary key. Indexes, foreign keys and check constraints are
        intentionally out of scope for v1 (see README).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    sch.name AS SchemaName,
    t.name   AS TableName,
    STUFF((
        SELECT ',' + CHAR(13) + CHAR(10) + '    ' + QUOTENAME(c.name) + ' ' +
            UPPER(ty.name) +
            CASE
                WHEN ty.name IN ('varchar','char','varbinary','binary')
                    THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length AS VARCHAR(10)) END + ')'
                WHEN ty.name IN ('nvarchar','nchar')
                    THEN '(' + CASE WHEN c.max_length = -1 THEN 'MAX' ELSE CAST(c.max_length / 2 AS VARCHAR(10)) END + ')'
                WHEN ty.name IN ('decimal','numeric')
                    THEN '(' + CAST(c.precision AS VARCHAR(10)) + ',' + CAST(c.scale AS VARCHAR(10)) + ')'
                ELSE ''
            END +
            CASE WHEN c.is_identity = 1
                THEN ' IDENTITY(' + CAST(ISNULL(ic.seed_value, 1) AS VARCHAR(20)) + ',' + CAST(ISNULL(ic.increment_value, 1) AS VARCHAR(20)) + ')'
                ELSE ''
            END +
            CASE WHEN c.is_nullable = 0 THEN ' NOT NULL' ELSE ' NULL' END +
            CASE WHEN dc.definition IS NOT NULL THEN ' DEFAULT ' + dc.definition ELSE '' END
        FROM sys.columns c
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
        WHERE c.object_id = t.object_id
        ORDER BY c.column_id
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 1, '') AS ColumnsDdl,
    (
        SELECT '  CONSTRAINT ' + QUOTENAME(kc.name) + ' PRIMARY KEY (' +
            STUFF((
                SELECT ', ' + QUOTENAME(c2.name) + CASE WHEN ic2.is_descending_key = 1 THEN ' DESC' ELSE ' ASC' END
                FROM sys.index_columns ic2
                JOIN sys.columns c2 ON c2.object_id = ic2.object_id AND c2.column_id = ic2.column_id
                WHERE ic2.object_id = kc.parent_object_id AND ic2.index_id = kc.unique_index_id
                ORDER BY ic2.key_ordinal
                FOR XML PATH(''), TYPE
            ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') + ')'
        FROM sys.key_constraints kc
        WHERE kc.parent_object_id = t.object_id AND kc.type = 'PK'
    ) AS PrimaryKeyDdl
FROM sys.tables t
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0
ORDER BY sch.name, t.name;
"@
    $rows = Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query

    foreach ($row in $rows) {
        $lines = @("CREATE TABLE [$($row.SchemaName)].[$($row.TableName)] (")
        $lines += $row.ColumnsDdl
        if (-not [string]::IsNullOrWhiteSpace([string]$row.PrimaryKeyDdl)) {
            $lines[-1] += ','
            $lines += $row.PrimaryKeyDdl
        }
        $lines += ');'

        [pscustomobject]@{
            SchemaName = $row.SchemaName
            TableName  = $row.TableName
            Definition = ($lines -join "`n")
        }
    }
}

function Get-SyncSqlMsSqlSynonyms {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT sch.name AS SchemaName, syn.name AS SynonymName, syn.base_object_name AS BaseObjectName
FROM sys.synonyms syn
JOIN sys.schemas sch ON sch.schema_id = syn.schema_id
ORDER BY sch.name, syn.name;
"@
    $rows = Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
    foreach ($row in $rows) {
        [pscustomobject]@{
            SchemaName  = $row.SchemaName
            SynonymName = $row.SynonymName
            Definition  = "CREATE SYNONYM [$($row.SchemaName)].[$($row.SynonymName)] FOR $($row.BaseObjectName);"
        }
    }
}

function Get-SyncSqlMsSqlExtendedProperties {
    <#
        Best-effort documentation extraction (MS_Description and friends)
        from sys.extended_properties at the object and column level
        (class = 1). Callers should treat failures as non-fatal: some
        environments restrict access to this catalog view.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    s.name AS SchemaName,
    o.name AS ObjectName,
    c.name AS ColumnName,
    ep.name AS PropertyName,
    CAST(ep.value AS NVARCHAR(MAX)) AS PropertyValue
FROM sys.extended_properties ep
JOIN sys.objects o ON o.object_id = ep.major_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id AND ep.minor_id <> 0
WHERE ep.class = 1
ORDER BY s.name, o.name, ep.minor_id, ep.name;
"@
    return Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
}

function Get-SyncSqlMsSqlExtendedPropertiesIndex {
    <#
        Wraps Get-SyncSqlMsSqlExtendedProperties in a try/catch and returns
        a lookup ("schema.object" -> formatted comment lines) instead of raw
        rows, so callers get an empty index rather than an error when the
        query isn't available (permissions, edition, etc).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$ServerName
    )

    $index = @{}
    try {
        foreach ($row in (Get-SyncSqlMsSqlExtendedProperties -ConnectionInfo $ConnectionInfo -Database $Database)) {
            $key = "$($row.SchemaName).$($row.ObjectName)"
            if (-not $index.ContainsKey($key)) { $index[$key] = [System.Collections.Generic.List[string]]::new() }
            $label = if ([string]::IsNullOrWhiteSpace([string]$row.ColumnName)) { '[object]' } else { "[column: $($row.ColumnName)]" }
            $index[$key].Add("-- $label $($row.PropertyName) = $($row.PropertyValue)")
        }
    }
    catch {
        Write-SyncSqlLog "[$ServerName/$Database] sys.extended_properties extraction failed (continuing without it): $($_.Exception.Message)" -Level WARN
        return @{}
    }
    return $index
}

function Add-SyncSqlExtendedPropertiesBlock {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Definition,
        [Parameter(Mandatory)]$ExtendedPropertiesIndex,
        [Parameter(Mandatory)][string]$SchemaName,
        [Parameter(Mandatory)][string]$ObjectName
    )

    $key = "$SchemaName.$ObjectName"
    if (-not $ExtendedPropertiesIndex.ContainsKey($key)) { return $Definition }

    $block = @('', '-- === Extended Properties ===') + $ExtendedPropertiesIndex[$key]
    return $Definition + "`n" + ($block -join "`n")
}

function Get-SyncSqlMsSqlGrants {
    <#
        Object- and column-level GRANT/DENY permissions (sys.database_permissions,
        class = 1: OBJECT_OR_COLUMN) so each object can carry its own access
        map - who can do what to it, down to the column when the grant was
        scoped that way. Server-level/database-level permissions and role
        membership are out of scope; this is "who can touch this object", not
        a full permissions audit.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    s.name  AS SchemaName,
    o.name  AS ObjectName,
    dp.name AS GranteeName,
    dp.type_desc AS GranteeType,
    perm.permission_name AS PermissionName,
    perm.state_desc AS StateDesc,
    c.name AS ColumnName
FROM sys.database_permissions perm
JOIN sys.database_principals dp ON dp.principal_id = perm.grantee_principal_id
JOIN sys.objects o ON o.object_id = perm.major_id
JOIN sys.schemas s ON s.schema_id = o.schema_id
LEFT JOIN sys.columns c ON c.object_id = perm.major_id AND c.column_id = perm.minor_id AND perm.minor_id <> 0
WHERE perm.class = 1 AND perm.major_id > 0 AND perm.state_desc IN ('GRANT', 'DENY')
ORDER BY s.name, o.name, dp.name, perm.permission_name;
"@
    return Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
}

function ConvertTo-SyncSqlGrantLine {
    <#
        Shared formatting for the pipe-delimited "-- [grant] ..." lines
        Build-Catalog.ps1 parses back into structured node.grants entries.
        Used by both the MSSQL and Oracle backends so the on-disk format (and
        the parser) only needs to exist once.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Permission,
        [Parameter(Mandatory)][string]$State,
        [Parameter(Mandatory)][string]$Grantee,
        [AllowEmptyString()][AllowNull()][string]$GranteeType,
        [AllowEmptyString()][AllowNull()][string]$Column
    )
    return "-- [grant] $Permission|$State|$Grantee|$GranteeType|$Column"
}

function Get-SyncSqlMsSqlGrantsIndex {
    <#
        Wraps Get-SyncSqlMsSqlGrants in a try/catch and returns a lookup
        ("schema.object" -> formatted "-- [grant] ..." lines) instead of raw
        rows, so callers get an empty index rather than an error when the
        query isn't available (permissions, edition, etc) - same shape as
        Get-SyncSqlMsSqlExtendedPropertiesIndex.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$ServerName
    )

    $index = @{}
    try {
        foreach ($row in (Get-SyncSqlMsSqlGrants -ConnectionInfo $ConnectionInfo -Database $Database)) {
            $key = "$($row.SchemaName).$($row.ObjectName)"
            if (-not $index.ContainsKey($key)) { $index[$key] = [System.Collections.Generic.List[string]]::new() }
            $index[$key].Add((ConvertTo-SyncSqlGrantLine -Permission $row.PermissionName -State $row.StateDesc `
                        -Grantee $row.GranteeName -GranteeType $row.GranteeType -Column ([string]$row.ColumnName)))
        }
    }
    catch {
        Write-SyncSqlLog "[$ServerName/$Database] sys.database_permissions extraction failed (continuing without it): $($_.Exception.Message)" -Level WARN
        return @{}
    }
    return $index
}

function Get-SyncSqlMsSqlColumnList {
    <#
        Full ordinal column list (name + data type) for tables and views -
        unlike Get-SyncSqlMsSqlExtendedPropertiesIndex's column map, this is
        not limited to columns that happen to have a description, so it can
        back a real "which columns exist" / column-lineage picture.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    sch.name AS SchemaName,
    o.name   AS TableName,
    c.name   AS ColumnName,
    ty.name  AS DataType,
    c.column_id AS OrdinalPosition
FROM sys.columns c
JOIN sys.objects o ON o.object_id = c.object_id
JOIN sys.schemas sch ON sch.schema_id = o.schema_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
ORDER BY sch.name, o.name, c.column_id;
"@
    return Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
}

function Get-SyncSqlMsSqlColumnListIndex {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$ServerName
    )

    $index = @{}
    try {
        foreach ($row in (Get-SyncSqlMsSqlColumnList -ConnectionInfo $ConnectionInfo -Database $Database)) {
            $key = "$($row.SchemaName).$($row.TableName)"
            if (-not $index.ContainsKey($key)) { $index[$key] = [System.Collections.Generic.List[string]]::new() }
            $index[$key].Add("-- [col] $($row.ColumnName)|$($row.DataType)")
        }
    }
    catch {
        Write-SyncSqlLog "[$ServerName/$Database] Column list extraction failed (continuing without it): $($_.Exception.Message)" -Level WARN
        return @{}
    }
    return $index
}

function Get-SyncSqlMsSqlTableVolume {
    <#
        Row counts and reserved/data/index size (KB) per table - the same
        sys.dm_db_partition_stats/sys.allocation_units aggregation
        sp_spaceused uses under the hood.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    sch.name AS SchemaName,
    t.name   AS TableName,
    SUM(CASE WHEN i.index_id IN (0, 1) THEN p.rows ELSE 0 END) AS RowCount,
    SUM(a.total_pages) * 8 AS ReservedKB,
    SUM(CASE WHEN i.index_id IN (0, 1) THEN a.used_pages ELSE 0 END) * 8 AS DataKB,
    SUM(CASE WHEN i.index_id > 1 THEN a.used_pages ELSE 0 END) * 8 AS IndexKB
FROM sys.tables t
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
JOIN sys.indexes i ON i.object_id = t.object_id
JOIN sys.partitions p ON p.object_id = i.object_id AND p.index_id = i.index_id
JOIN sys.allocation_units a ON a.container_id = p.partition_id
WHERE t.is_ms_shipped = 0
GROUP BY sch.name, t.name
ORDER BY sch.name, t.name;
"@
    return Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
}

function Get-SyncSqlMsSqlIndexMetrics {
    <#
        Per-index physical stats (fragmentation %, page count - via
        sys.dm_db_index_physical_stats in cheap 'LIMITED' mode, one
        whole-database scan reused across every index rather than one call
        per index) and usage counters (seeks/scans/lookups/updates - via
        sys.dm_db_index_usage_stats, which resets on service restart, so
        this is inherently "as of now", not a durable fact worth
        versioning).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    sch.name AS SchemaName,
    t.name   AS TableName,
    i.name   AS IndexName,
    AVG(ps.avg_fragmentation_in_percent) AS FragmentationPct,
    SUM(ps.page_count) AS PageCount,
    MAX(ISNULL(us.user_seeks, 0)) AS Seeks,
    MAX(ISNULL(us.user_scans, 0)) AS Scans,
    MAX(ISNULL(us.user_lookups, 0)) AS Lookups,
    MAX(ISNULL(us.user_updates, 0)) AS Updates
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
LEFT JOIN sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ps
    ON ps.object_id = i.object_id AND ps.index_id = i.index_id
LEFT JOIN sys.dm_db_index_usage_stats us
    ON us.object_id = i.object_id AND us.index_id = i.index_id AND us.database_id = DB_ID()
WHERE i.name IS NOT NULL AND t.is_ms_shipped = 0
GROUP BY sch.name, t.name, i.name
ORDER BY sch.name, t.name, i.name;
"@
    return Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
}

function Get-SyncSqlMsSqlOptimizerStatistics {
    <#
        The statistics actually consulted by the query optimizer for
        cardinality estimation - not the "CREATE STATISTICS" object
        definition (that's schema metadata; this is the live histogram
        summary behind it): rows, rows sampled, histogram step count,
        modification counter (how stale it is) and last-updated time, via
        sys.dm_db_stats_properties.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    sch.name AS SchemaName,
    t.name   AS TableName,
    s.name   AS StatName,
    sp.rows AS Rows,
    sp.rows_sampled AS RowsSampled,
    sp.steps AS Steps,
    sp.modification_counter AS ModificationCounter,
    sp.last_updated AS LastUpdated
FROM sys.stats s
JOIN sys.tables t ON t.object_id = s.object_id
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
CROSS APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) sp
WHERE s.name IS NOT NULL AND t.is_ms_shipped = 0
ORDER BY sch.name, t.name, s.name;
"@
    return Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
}

function Get-SyncSqlMsSqlMetricsSnapshot {
    <#
        Combines table volume + per-index metrics + optimizer statistics
        into one snapshot object per table (keyed "schema.table"), ready to
        be written via New-SyncSqlMetricsSnapshotFile and appended into that
        table's metrics history by Update-MetricsHistory.ps1. Best-effort:
        each of the three underlying queries degrades independently (with a
        warning) rather than failing the whole database, matching every
        other optional extraction step in this module.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$ServerName
    )

    $capturedAt = (Get-Date).ToUniversalTime().ToString('o')
    $snapshots = @{}

    try {
        foreach ($row in (Get-SyncSqlMsSqlTableVolume -ConnectionInfo $ConnectionInfo -Database $Database)) {
            $key = "$($row.SchemaName).$($row.TableName)"
            $snapshots[$key] = [ordered]@{
                capturedAt = $capturedAt
                rowCount   = [int64]$row.RowCount
                reservedKB = [int64]$row.ReservedKB
                dataKB     = [int64]$row.DataKB
                indexKB    = [int64]$row.IndexKB
                indexes    = @()
                statistics = @()
            }
        }
    }
    catch {
        Write-SyncSqlLog "[$ServerName/$Database] Table volume extraction failed (continuing without it): $($_.Exception.Message)" -Level WARN
    }

    try {
        $indexesByTable = @{}
        foreach ($row in (Get-SyncSqlMsSqlIndexMetrics -ConnectionInfo $ConnectionInfo -Database $Database)) {
            $key = "$($row.SchemaName).$($row.TableName)"
            if (-not $indexesByTable.ContainsKey($key)) { $indexesByTable[$key] = [System.Collections.Generic.List[object]]::new() }
            $indexesByTable[$key].Add([ordered]@{
                    name             = $row.IndexName
                    fragmentationPct = if ($row.FragmentationPct -is [DBNull]) { $null } else { [math]::Round([double]$row.FragmentationPct, 2) }
                    pageCount        = if ($row.PageCount -is [DBNull]) { $null } else { [int64]$row.PageCount }
                    seeks            = [int64]$row.Seeks
                    scans            = [int64]$row.Scans
                    lookups          = [int64]$row.Lookups
                    updates          = [int64]$row.Updates
                })
        }
        foreach ($key in $indexesByTable.Keys) {
            if ($snapshots.ContainsKey($key)) { $snapshots[$key]['indexes'] = @($indexesByTable[$key]) }
        }
    }
    catch {
        Write-SyncSqlLog "[$ServerName/$Database] Index metrics extraction failed (continuing without it): $($_.Exception.Message)" -Level WARN
    }

    try {
        $statsByTable = @{}
        foreach ($row in (Get-SyncSqlMsSqlOptimizerStatistics -ConnectionInfo $ConnectionInfo -Database $Database)) {
            $key = "$($row.SchemaName).$($row.TableName)"
            if (-not $statsByTable.ContainsKey($key)) { $statsByTable[$key] = [System.Collections.Generic.List[object]]::new() }
            $statsByTable[$key].Add([ordered]@{
                    name                = $row.StatName
                    rows                = if ($row.Rows -is [DBNull]) { $null } else { [int64]$row.Rows }
                    rowsSampled         = if ($row.RowsSampled -is [DBNull]) { $null } else { [int64]$row.RowsSampled }
                    steps               = if ($row.Steps -is [DBNull]) { $null } else { [int]$row.Steps }
                    modificationCounter = if ($row.ModificationCounter -is [DBNull]) { $null } else { [int64]$row.ModificationCounter }
                    lastUpdated         = if ($row.LastUpdated -is [DBNull]) { $null } else { ([datetime]$row.LastUpdated).ToUniversalTime().ToString('o') }
                })
        }
        foreach ($key in $statsByTable.Keys) {
            if ($snapshots.ContainsKey($key)) { $snapshots[$key]['statistics'] = @($statsByTable[$key]) }
        }
    }
    catch {
        Write-SyncSqlLog "[$ServerName/$Database] Optimizer statistics extraction failed (continuing without it): $($_.Exception.Message)" -Level WARN
    }

    return $snapshots
}

function Get-SyncSqlMsSqlForeignKeys {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    sch.name AS SchemaName,
    t.name   AS TableName,
    fk.name  AS ForeignKeyName,
    'ALTER TABLE ' + QUOTENAME(sch.name) + '.' + QUOTENAME(t.name) + ' ADD CONSTRAINT ' + QUOTENAME(fk.name) +
    ' FOREIGN KEY (' +
    STUFF((
        SELECT ', ' + QUOTENAME(c.name)
        FROM sys.foreign_key_columns fkc
        JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
        WHERE fkc.constraint_object_id = fk.object_id
        ORDER BY fkc.constraint_column_id
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') +
    ') REFERENCES ' + QUOTENAME(rsch.name) + '.' + QUOTENAME(rt.name) + ' (' +
    STUFF((
        SELECT ', ' + QUOTENAME(rc.name)
        FROM sys.foreign_key_columns fkc2
        JOIN sys.columns rc ON rc.object_id = fkc2.referenced_object_id AND rc.column_id = fkc2.referenced_column_id
        WHERE fkc2.constraint_object_id = fk.object_id
        ORDER BY fkc2.constraint_column_id
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') + ');' AS Definition
FROM sys.foreign_keys fk
JOIN sys.tables t ON t.object_id = fk.parent_object_id
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
JOIN sys.schemas rsch ON rsch.schema_id = rt.schema_id
ORDER BY sch.name, t.name, fk.name;
"@
    return Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
}

function Get-SyncSqlMsSqlCheckConstraints {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    sch.name AS SchemaName,
    t.name   AS TableName,
    cc.name  AS CheckName,
    'ALTER TABLE ' + QUOTENAME(sch.name) + '.' + QUOTENAME(t.name) + ' ADD CONSTRAINT ' + QUOTENAME(cc.name) +
    ' CHECK ' + cc.definition + ';' AS Definition
FROM sys.check_constraints cc
JOIN sys.tables t ON t.object_id = cc.parent_object_id
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
ORDER BY sch.name, t.name, cc.name;
"@
    return Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
}

function Get-SyncSqlMsSqlIndexes {
    <#
        Non-PK, non-unique-constraint indexes (those are already represented
        via the table's PRIMARY KEY / unique constraint clauses). Only plain
        rowstore CLUSTERED/NONCLUSTERED indexes get a runnable CREATE INDEX
        line; anything else (columnstore, XML, spatial, hash) gets an
        informational comment instead of a guessed-at DDL.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
SELECT
    sch.name AS SchemaName,
    t.name   AS TableName,
    i.name   AS IndexName,
    i.is_unique AS IsUnique,
    i.type_desc AS TypeDesc,
    STUFF((
        SELECT ', ' + QUOTENAME(c.name) + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE ' ASC' END
        FROM sys.index_columns ic
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
        ORDER BY ic.key_ordinal
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS KeyColumns,
    STUFF((
        SELECT ', ' + QUOTENAME(c.name)
        FROM sys.index_columns ic
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
        ORDER BY ic.index_column_id
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS IncludedColumns
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
JOIN sys.schemas sch ON sch.schema_id = t.schema_id
WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND i.index_id > 0 AND i.name IS NOT NULL
ORDER BY sch.name, t.name, i.name;
"@
    $rows = Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
    foreach ($row in $rows) {
        $typeDesc = [string]$row.TypeDesc
        if ($typeDesc -eq 'CLUSTERED' -or $typeDesc -eq 'NONCLUSTERED') {
            $prefix = if ($row.IsUnique) { "UNIQUE $typeDesc INDEX" } else { "$typeDesc INDEX" }
            $line = "CREATE $prefix " + "[$($row.IndexName)] ON [$($row.SchemaName)].[$($row.TableName)] ($($row.KeyColumns))"
            if (-not [string]::IsNullOrWhiteSpace([string]$row.IncludedColumns)) {
                $line += " INCLUDE ($($row.IncludedColumns))"
            }
            $line += ';'
        }
        else {
            $line = "-- Index [$($row.IndexName)] ($typeDesc) - see sys.indexes for full definition"
        }
        [pscustomobject]@{
            SchemaName = $row.SchemaName
            TableName  = $row.TableName
            Definition = $line
        }
    }
}

function Get-SyncSqlMsSqlTableSectionIndex {
    <#
        Shared runner for the per-table appended sections (foreign keys,
        check constraints, indexes): runs $Getter, groups rows by
        "schema.table" into formatted comment lines, and degrades to an
        empty index (with a warning) instead of failing the whole database
        if the underlying query errors.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][scriptblock]$Getter,
        [Parameter(Mandatory)][string]$SectionName,
        [Parameter(Mandatory)][string]$ServerName,
        [Parameter(Mandatory)][string]$Database
    )

    $index = @{}
    try {
        foreach ($row in (& $Getter)) {
            $key = "$($row.SchemaName).$($row.TableName)"
            if (-not $index.ContainsKey($key)) { $index[$key] = [System.Collections.Generic.List[string]]::new() }
            $index[$key].Add($row.Definition)
        }
    }
    catch {
        Write-SyncSqlLog "[$ServerName/$Database] $SectionName extraction failed (continuing without it): $($_.Exception.Message)" -Level WARN
        return @{}
    }
    return $index
}

function Get-SyncSqlMsSqlReplication {
    <#
        Best-effort, informational-only snapshot of transactional/snapshot
        replication publications configured on this database: publication
        name and the articles (objects) it replicates. Subscriber
        enumeration is intentionally left out - subscription table shapes
        vary enough across SQL Server versions/topologies that guessing at
        it reliably isn't worth the risk of a confusing wrong answer.
        Returns nothing (not an error) on a database that isn't a
        replication Publisher; callers should still wrap this since a
        Publisher can be locked down enough that even OBJECT_ID() checks on
        these tables fail under a restricted login.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$ConnectionInfo,
        [Parameter(Mandatory)][string]$Database
    )

    $query = @"
IF OBJECT_ID('dbo.syspublications') IS NULL
BEGIN
    SELECT CAST(NULL AS sysname) AS PublicationName, CAST(NULL AS NVARCHAR(MAX)) AS Description, CAST(NULL AS NVARCHAR(MAX)) AS Articles WHERE 1 = 0;
END
ELSE
BEGIN
    SELECT
        p.name AS PublicationName,
        CAST(p.description AS NVARCHAR(MAX)) AS Description,
        STUFF((
            SELECT ', ' + a.name
            FROM dbo.sysarticles a
            WHERE a.pubid = p.pubid
            ORDER BY a.name
            FOR XML PATH(''), TYPE
        ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS Articles
    FROM dbo.syspublications p
    ORDER BY p.name;
END
"@
    $rows = Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database $Database -Query $query
    foreach ($row in $rows) {
        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.Add("-- Publication: $($row.PublicationName)")
        if (-not [string]::IsNullOrWhiteSpace([string]$row.Description)) {
            $lines.Add("-- Description: $($row.Description)")
        }
        $lines.Add('-- Articles (replicated objects):')
        if ([string]::IsNullOrWhiteSpace([string]$row.Articles)) {
            $lines.Add('--   (none)')
        }
        else {
            foreach ($article in ($row.Articles -split ', ')) { $lines.Add("--   - $article") }
        }
        $lines.Add('-- Subscribers are not enumerated - see Replication Monitor for current subscription state.')

        [pscustomobject]@{
            PublicationName = $row.PublicationName
            Definition      = ($lines -join "`n")
        }
    }
}

function Get-SyncSqlMsSqlLinkedServers {
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$ConnectionInfo)

    $query = @"
SELECT
    s.name AS LinkedServerName,
    s.product, s.provider, s.data_source, s.provider_string, s.catalog,
    ll.remote_name AS RemoteLoginName,
    ll.uses_self_credential AS UsesSelfCredential
FROM sys.servers s
LEFT JOIN sys.linked_logins ll ON ll.server_id = s.server_id AND ll.local_principal_id <> 0
WHERE s.is_linked = 1
ORDER BY s.name;
"@
    $rows = Invoke-SyncSqlMsSqlQuery @ConnectionInfo -Database 'master' -Query $query

    $byServer = $rows | Group-Object -Property LinkedServerName
    foreach ($group in $byServer) {
        $row = $group.Group[0]
        $lines = @(
            "EXEC sp_addlinkedserver",
            "    @server = N'$($row.LinkedServerName)',",
            "    @srvproduct = N'$($row.product)',",
            "    @provider = N'$($row.provider)',",
            "    @datasrc = N'$($row.data_source)',",
            "    @provstr = N'$($row.provider_string)',",
            "    @catalog = N'$($row.catalog)';",
            "GO"
        )
        foreach ($login in $group.Group) {
            if (-not [string]::IsNullOrWhiteSpace([string]$login.RemoteLoginName)) {
                $useSelf = if ($login.UsesSelfCredential) { 'TRUE' } else { 'FALSE' }
                $lines += "-- Remote login mapping (password not extracted; re-set manually after restore):"
                $lines += "EXEC sp_addlinkedsrvlogin @rmtsrvname = N'$($row.LinkedServerName)', @useself = N'$useSelf', @rmtuser = N'$($login.RemoteLoginName)', @rmtpassword = N'########';"
            }
        }
        [pscustomobject]@{
            LinkedServerName = $row.LinkedServerName
            Definition       = ($lines -join "`n")
        }
    }
}

function Export-SyncSqlMsSqlServer {
    <#
        Orchestrates a full extraction of one MSSQL server entry into
        $StagingRoot, honoring the resolved database/schema/objectName
        filters and the allowed object type list. Also writes a volatile
        metrics snapshot (row counts, index fragmentation/usage, optimizer
        statistics) per table into $MetricsRoot when supplied - a separate
        staging area from $StagingRoot, since this data changes every run
        and must never be diffed as part of the table's own versioned file.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Server,
        [Parameter(Mandatory)][string]$Username,
        [Parameter(Mandatory)][string]$Password,
        [Parameter(Mandatory)]$Filters,
        [Parameter(Mandatory)][string[]]$AllowedObjectTypes,
        [Parameter(Mandatory)][string]$StagingRoot,
        [string]$MetricsRoot
    )

    $serverName = $Server.name
    $connectionInfo = @{
        ServerHost              = $Server.host
        Port                    = if ($Server.Contains('port') -and $Server.port) { [int]$Server.port } else { 1433 }
        Username                = $Username
        Password                = $Password
        Encrypt                 = if ($Server.Contains('encrypt')) { [bool]$Server.encrypt } else { $true }
        TrustServerCertificate  = if ($Server.Contains('trustServerCertificate')) { [bool]$Server.trustServerCertificate } else { $false }
    }

    $fileCount = 0

    if ($AllowedObjectTypes -contains 'LinkedServers') {
        Write-SyncSqlLog "[$serverName] Extracting linked servers"
        foreach ($ls in (Get-SyncSqlMsSqlLinkedServers -ConnectionInfo $connectionInfo)) {
            if (-not (Test-SyncSqlNameAllowed -Name $ls.LinkedServerName -Filter $Filters.objectNames)) { continue }
            New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName '_ServerLevel' `
                -ObjectType 'LinkedServers' -ObjectName $ls.LinkedServerName -Definition $ls.Definition | Out-Null
            $fileCount++
        }
    }

    $databases = Get-SyncSqlMsSqlDatabases -ConnectionInfo $connectionInfo
    foreach ($database in $databases) {
        if (-not (Test-SyncSqlNameAllowed -Name $database -Filter $Filters.databases)) { continue }
        Write-SyncSqlLog "[$serverName/$database] Extracting"

        $allowedSchemas = @{}
        foreach ($schemaRow in (Get-SyncSqlMsSqlSchemas -ConnectionInfo $connectionInfo -Database $database)) {
            $allowedSchemas[$schemaRow.SchemaName] = Test-SyncSqlNameAllowed -Name $schemaRow.SchemaName -Filter $Filters.schemas
        }

        if ($AllowedObjectTypes -contains 'Schemas') {
            foreach ($schemaName in $allowedSchemas.Keys) {
                if (-not $allowedSchemas[$schemaName]) { continue }
                New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $database `
                    -ObjectType 'Schemas' -ObjectName $schemaName -Definition "CREATE SCHEMA [$schemaName];" | Out-Null
                $fileCount++
            }
        }

        # Optional: sys.extended_properties (MS_Description etc). Never fatal -
        # Get-SyncSqlMsSqlExtendedPropertiesIndex swallows its own errors and
        # returns an empty index on failure.
        $extendedProperties = Get-SyncSqlMsSqlExtendedPropertiesIndex -ConnectionInfo $connectionInfo -Database $database -ServerName $serverName

        # Optional: sys.database_permissions (object/column grants) and the
        # full sys.columns list for tables/views. Both degrade to an empty
        # index (with a warning) rather than failing the database.
        $grants = Get-SyncSqlMsSqlGrantsIndex -ConnectionInfo $connectionInfo -Database $database -ServerName $serverName
        $columnList = Get-SyncSqlMsSqlColumnListIndex -ConnectionInfo $connectionInfo -Database $database -ServerName $serverName

        $needsModules = @('StoredProcedures', 'Views', 'Triggers', 'Functions') | Where-Object { $AllowedObjectTypes -contains $_ }
        if ($needsModules) {
            foreach ($obj in (Get-SyncSqlMsSqlModuleObjects -ConnectionInfo $connectionInfo -Database $database)) {
                $objectType = $script:SyncSqlMsSqlTypeMap[$obj.TypeCode.Trim()]
                if (-not $objectType -or $AllowedObjectTypes -notcontains $objectType) { continue }
                if (-not $allowedSchemas.ContainsKey($obj.SchemaName) -or -not $allowedSchemas[$obj.SchemaName]) { continue }
                if (-not (Test-SyncSqlNameAllowed -Name $obj.ObjectName -Filter $Filters.objectNames)) { continue }

                $key = "$($obj.SchemaName).$($obj.ObjectName)"
                $definition = $obj.Definition
                # Views have a real column list (sys.columns); procs/functions/triggers don't.
                if ($objectType -eq 'Views') {
                    $definition = Add-SyncSqlSectionBlock -Definition $definition -Title 'Columns' -SectionIndex $columnList -Key $key
                }
                $definition = Add-SyncSqlSectionBlock -Definition $definition -Title 'Grants' -SectionIndex $grants -Key $key
                $definition = Add-SyncSqlExtendedPropertiesBlock -Definition $definition -ExtendedPropertiesIndex $extendedProperties `
                    -SchemaName $obj.SchemaName -ObjectName $obj.ObjectName
                New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $database `
                    -SchemaName $obj.SchemaName -ObjectType $objectType -ObjectName $obj.ObjectName `
                    -Definition $definition | Out-Null
                $fileCount++
            }
        }

        if ($AllowedObjectTypes -contains 'Tables') {
            # Each of these degrades to an empty index (with a warning) on its
            # own if the underlying query fails - one missing section never
            # blocks the rest of the table's extraction.
            $foreignKeys = Get-SyncSqlMsSqlTableSectionIndex -ServerName $serverName -Database $database -SectionName 'Foreign key' `
                -Getter { Get-SyncSqlMsSqlForeignKeys -ConnectionInfo $connectionInfo -Database $database }
            $checkConstraints = Get-SyncSqlMsSqlTableSectionIndex -ServerName $serverName -Database $database -SectionName 'Check constraint' `
                -Getter { Get-SyncSqlMsSqlCheckConstraints -ConnectionInfo $connectionInfo -Database $database }
            $indexes = Get-SyncSqlMsSqlTableSectionIndex -ServerName $serverName -Database $database -SectionName 'Index' `
                -Getter { Get-SyncSqlMsSqlIndexes -ConnectionInfo $connectionInfo -Database $database }

            # Volatile, best-effort - never blocks table extraction if it fails.
            $metricsSnapshots = if ($MetricsRoot) {
                Get-SyncSqlMsSqlMetricsSnapshot -ConnectionInfo $connectionInfo -Database $database -ServerName $serverName
            } else { @{} }

            foreach ($table in (Get-SyncSqlMsSqlTables -ConnectionInfo $connectionInfo -Database $database)) {
                if (-not $allowedSchemas.ContainsKey($table.SchemaName) -or -not $allowedSchemas[$table.SchemaName]) { continue }
                if (-not (Test-SyncSqlNameAllowed -Name $table.TableName -Filter $Filters.objectNames)) { continue }

                $key = "$($table.SchemaName).$($table.TableName)"
                $definition = $table.Definition
                $definition = Add-SyncSqlSectionBlock -Definition $definition -Title 'Foreign Keys' -SectionIndex $foreignKeys -Key $key
                $definition = Add-SyncSqlSectionBlock -Definition $definition -Title 'Check Constraints' -SectionIndex $checkConstraints -Key $key
                $definition = Add-SyncSqlSectionBlock -Definition $definition -Title 'Indexes' -SectionIndex $indexes -Key $key
                $definition = Add-SyncSqlSectionBlock -Definition $definition -Title 'Columns' -SectionIndex $columnList -Key $key
                $definition = Add-SyncSqlSectionBlock -Definition $definition -Title 'Grants' -SectionIndex $grants -Key $key
                $definition = Add-SyncSqlExtendedPropertiesBlock -Definition $definition -ExtendedPropertiesIndex $extendedProperties `
                    -SchemaName $table.SchemaName -ObjectName $table.TableName

                New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $database `
                    -SchemaName $table.SchemaName -ObjectType 'Tables' -ObjectName $table.TableName `
                    -Definition $definition | Out-Null
                $fileCount++

                if ($MetricsRoot -and $metricsSnapshots.ContainsKey($key)) {
                    New-SyncSqlMetricsSnapshotFile -MetricsRoot $MetricsRoot -ServerName $serverName -DatabaseName $database `
                        -SchemaName $table.SchemaName -ObjectType 'Tables' -ObjectName $table.TableName `
                        -Snapshot $metricsSnapshots[$key] | Out-Null
                }
            }
        }

        if ($AllowedObjectTypes -contains 'Synonyms') {
            foreach ($syn in (Get-SyncSqlMsSqlSynonyms -ConnectionInfo $connectionInfo -Database $database)) {
                if (-not $allowedSchemas.ContainsKey($syn.SchemaName) -or -not $allowedSchemas[$syn.SchemaName]) { continue }
                if (-not (Test-SyncSqlNameAllowed -Name $syn.SynonymName -Filter $Filters.objectNames)) { continue }

                $definition = Add-SyncSqlSectionBlock -Definition $syn.Definition -Title 'Grants' -SectionIndex $grants `
                    -Key "$($syn.SchemaName).$($syn.SynonymName)"
                New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $database `
                    -SchemaName $syn.SchemaName -ObjectType 'Synonyms' -ObjectName $syn.SynonymName `
                    -Definition $definition | Out-Null
                $fileCount++
            }
        }

        if ($AllowedObjectTypes -contains 'Replication') {
            try {
                foreach ($pub in (Get-SyncSqlMsSqlReplication -ConnectionInfo $connectionInfo -Database $database)) {
                    if (-not (Test-SyncSqlNameAllowed -Name $pub.PublicationName -Filter $Filters.objectNames)) { continue }
                    New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $database `
                        -ObjectType 'Replication' -ObjectName $pub.PublicationName -Definition $pub.Definition | Out-Null
                    $fileCount++
                }
            }
            catch {
                Write-SyncSqlLog "[$serverName/$database] Replication extraction failed (continuing without it): $($_.Exception.Message)" -Level WARN
            }
        }
    }

    Write-SyncSqlLog "[$serverName] Wrote $fileCount object file(s)"
    return $fileCount
}

Export-ModuleMember -Function @(
    'Invoke-SyncSqlMsSqlQuery',
    'Get-SyncSqlMsSqlDatabases',
    'Get-SyncSqlMsSqlSchemas',
    'Get-SyncSqlMsSqlModuleObjects',
    'Get-SyncSqlMsSqlTables',
    'Get-SyncSqlMsSqlSynonyms',
    'Get-SyncSqlMsSqlLinkedServers',
    'Get-SyncSqlMsSqlExtendedProperties',
    'Get-SyncSqlMsSqlExtendedPropertiesIndex',
    'Add-SyncSqlExtendedPropertiesBlock',
    'Get-SyncSqlMsSqlGrants',
    'Get-SyncSqlMsSqlGrantsIndex',
    'ConvertTo-SyncSqlGrantLine',
    'Get-SyncSqlMsSqlColumnList',
    'Get-SyncSqlMsSqlColumnListIndex',
    'Get-SyncSqlMsSqlTableVolume',
    'Get-SyncSqlMsSqlIndexMetrics',
    'Get-SyncSqlMsSqlOptimizerStatistics',
    'Get-SyncSqlMsSqlMetricsSnapshot',
    'Get-SyncSqlMsSqlForeignKeys',
    'Get-SyncSqlMsSqlCheckConstraints',
    'Get-SyncSqlMsSqlIndexes',
    'Get-SyncSqlMsSqlTableSectionIndex',
    'Get-SyncSqlMsSqlReplication',
    'Export-SyncSqlMsSqlServer'
)
