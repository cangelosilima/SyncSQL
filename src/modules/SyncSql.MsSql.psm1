#Requires -Version 7.0
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
        filters and the allowed object type list.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Server,
        [Parameter(Mandatory)][string]$Username,
        [Parameter(Mandatory)][string]$Password,
        [Parameter(Mandatory)]$Filters,
        [Parameter(Mandatory)][string[]]$AllowedObjectTypes,
        [Parameter(Mandatory)][string]$StagingRoot
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

        $needsModules = @('StoredProcedures', 'Views', 'Triggers', 'Functions') | Where-Object { $AllowedObjectTypes -contains $_ }
        if ($needsModules) {
            foreach ($obj in (Get-SyncSqlMsSqlModuleObjects -ConnectionInfo $connectionInfo -Database $database)) {
                $objectType = $script:SyncSqlMsSqlTypeMap[$obj.TypeCode.Trim()]
                if (-not $objectType -or $AllowedObjectTypes -notcontains $objectType) { continue }
                if (-not $allowedSchemas.ContainsKey($obj.SchemaName) -or -not $allowedSchemas[$obj.SchemaName]) { continue }
                if (-not (Test-SyncSqlNameAllowed -Name $obj.ObjectName -Filter $Filters.objectNames)) { continue }

                $definition = Add-SyncSqlExtendedPropertiesBlock -Definition $obj.Definition -ExtendedPropertiesIndex $extendedProperties `
                    -SchemaName $obj.SchemaName -ObjectName $obj.ObjectName
                New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $database `
                    -SchemaName $obj.SchemaName -ObjectType $objectType -ObjectName $obj.ObjectName `
                    -Definition $definition | Out-Null
                $fileCount++
            }
        }

        if ($AllowedObjectTypes -contains 'Tables') {
            foreach ($table in (Get-SyncSqlMsSqlTables -ConnectionInfo $connectionInfo -Database $database)) {
                if (-not $allowedSchemas.ContainsKey($table.SchemaName) -or -not $allowedSchemas[$table.SchemaName]) { continue }
                if (-not (Test-SyncSqlNameAllowed -Name $table.TableName -Filter $Filters.objectNames)) { continue }

                $definition = Add-SyncSqlExtendedPropertiesBlock -Definition $table.Definition -ExtendedPropertiesIndex $extendedProperties `
                    -SchemaName $table.SchemaName -ObjectName $table.TableName
                New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $database `
                    -SchemaName $table.SchemaName -ObjectType 'Tables' -ObjectName $table.TableName `
                    -Definition $definition | Out-Null
                $fileCount++
            }
        }

        if ($AllowedObjectTypes -contains 'Synonyms') {
            foreach ($syn in (Get-SyncSqlMsSqlSynonyms -ConnectionInfo $connectionInfo -Database $database)) {
                if (-not $allowedSchemas.ContainsKey($syn.SchemaName) -or -not $allowedSchemas[$syn.SchemaName]) { continue }
                if (-not (Test-SyncSqlNameAllowed -Name $syn.SynonymName -Filter $Filters.objectNames)) { continue }

                New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $database `
                    -SchemaName $syn.SchemaName -ObjectType 'Synonyms' -ObjectName $syn.SynonymName `
                    -Definition $syn.Definition | Out-Null
                $fileCount++
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
    'Export-SyncSqlMsSqlServer'
)
