#Requires -Version 7.0
<#
    SyncSql.Oracle.psm1
    Oracle extraction backend. Uses the Oracle.ManagedDataAccess.Core
    NuGet package (pure managed ADO.NET driver, no Oracle Instant Client
    required) loaded via Add-Type, and DBMS_METADATA.GET_DDL for
    ready-to-diff object DDL text.
#>

Set-StrictMode -Version Latest

# Maps the config's objectTypes vocabulary to ALL_OBJECTS.OBJECT_TYPE values.
$script:SyncSqlOracleObjectTypeMap = [ordered]@{
    'Tables'        = 'TABLE'
    'Views'         = 'VIEW'
    'Procedures'    = 'PROCEDURE'
    'Functions'     = 'FUNCTION'
    'Packages'      = 'PACKAGE'
    'PackageBodies' = 'PACKAGE BODY'
    'Triggers'      = 'TRIGGER'
    'Synonyms'      = 'SYNONYM'
}

# Maps ALL_OBJECTS.OBJECT_TYPE values to the type name DBMS_METADATA.GET_DDL expects.
$script:SyncSqlOracleDdlTypeMap = @{
    'TABLE'        = 'TABLE'
    'VIEW'         = 'VIEW'
    'PROCEDURE'    = 'PROCEDURE'
    'FUNCTION'     = 'FUNCTION'
    'PACKAGE'      = 'PACKAGE'
    'PACKAGE BODY' = 'PACKAGE_BODY'
    'TRIGGER'      = 'TRIGGER'
    'SYNONYM'      = 'SYNONYM'
}

function Find-SyncSqlOracleDriverDll {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$CacheDir)

    if (-not (Test-Path -LiteralPath $CacheDir)) { return $null }

    $candidates = Get-ChildItem -LiteralPath $CacheDir -Recurse -Filter 'Oracle.ManagedDataAccess.dll' -ErrorAction SilentlyContinue
    if (-not $candidates) { return $null }

    $preferred = $candidates | Where-Object { $_.FullName -match 'netstandard2\.1' } | Select-Object -First 1
    if ($preferred) { return $preferred.FullName }

    return ($candidates | Select-Object -First 1).FullName
}

function Import-SyncSqlOracleDriver {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$DllPath)

    if (-not (Test-Path -LiteralPath $DllPath)) {
        throw "Oracle managed driver not found at '$DllPath'. Run Bootstrap-Dependencies.ps1 first."
    }
    if (-not ([System.Management.Automation.PSTypeName]'Oracle.ManagedDataAccess.Client.OracleConnection').Type) {
        Add-Type -Path $DllPath -ErrorAction Stop
    }
}

function New-SyncSqlOracleConnection {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$OracleHost,
        [int]$Port = 1521,
        [Parameter(Mandatory)][string]$ServiceName,
        [Parameter(Mandatory)][string]$Username,
        [Parameter(Mandatory)][string]$Password
    )

    $connString = "User Id=$Username;Password=$Password;Data Source=$OracleHost`:$Port/$ServiceName;"
    $connection = [Oracle.ManagedDataAccess.Client.OracleConnection]::new($connString)
    $connection.Open()

    # Trim noisy, environment-specific clauses so re-runs produce clean diffs.
    $prep = $connection.CreateCommand()
    $prep.CommandText = @"
BEGIN
  DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'STORAGE', FALSE);
  DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'SEGMENT_ATTRIBUTES', FALSE);
  DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'SQLTERMINATOR', TRUE);
  DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'PRETTY', TRUE);
END;
"@
    $prep.ExecuteNonQuery() | Out-Null

    return $connection
}

function Invoke-SyncSqlOracleQuery {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Connection,
        [Parameter(Mandatory)][string]$Query,
        [hashtable]$Parameters = @{}
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Query
    foreach ($key in $Parameters.Keys) {
        $command.Parameters.Add($key, $Parameters[$key]) | Out-Null
    }

    $adapter = [Oracle.ManagedDataAccess.Client.OracleDataAdapter]::new($command)
    $table = [System.Data.DataTable]::new()
    $adapter.Fill($table) | Out-Null
    return $table.Rows
}

function Get-SyncSqlOracleDdl {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Connection,
        [Parameter(Mandatory)][string]$DdlObjectType,
        [Parameter(Mandatory)][string]$ObjectName,
        [Parameter(Mandatory)][string]$Owner
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = "SELECT DBMS_METADATA.GET_DDL(:objType, :objName, :owner) FROM DUAL"
    $command.Parameters.Add('objType', $DdlObjectType) | Out-Null
    $command.Parameters.Add('objName', $ObjectName) | Out-Null
    $command.Parameters.Add('owner', $Owner) | Out-Null

    $result = $command.ExecuteScalar()
    return [string]$result
}

function Get-SyncSqlOracleSchemas {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Connection)

    $rows = Invoke-SyncSqlOracleQuery -Connection $Connection -Query "SELECT DISTINCT OWNER FROM ALL_OBJECTS ORDER BY OWNER"
    return @($rows | ForEach-Object { $_['OWNER'] })
}

function Get-SyncSqlOracleObjectList {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Connection,
        [Parameter(Mandatory)][string]$Owner,
        [Parameter(Mandatory)][string]$OracleObjectType
    )

    $query = @"
SELECT object_name AS ObjectName
FROM ALL_OBJECTS
WHERE owner = :owner
  AND object_type = :objType
  AND generated = 'N'
  AND temporary = 'N'
ORDER BY object_name
"@
    $rows = Invoke-SyncSqlOracleQuery -Connection $Connection -Query $query -Parameters @{ owner = $Owner; objType = $OracleObjectType }
    return @($rows | ForEach-Object { $_['ObjectName'] })
}

function Get-SyncSqlOracleDatabaseLinks {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Connection)

    $query = "SELECT OWNER, DB_LINK FROM ALL_DB_LINKS ORDER BY OWNER, DB_LINK"
    $rows = Invoke-SyncSqlOracleQuery -Connection $Connection -Query $query
    foreach ($row in $rows) {
        [pscustomobject]@{
            Owner   = [string]$row['OWNER']
            DbLink  = [string]$row['DB_LINK']
        }
    }
}

function Get-SyncSqlOracleGrantsIndex {
    <#
        Object-level (ALL_TAB_PRIVS) and column-level (ALL_COL_PRIVS) grants
        for every object owned by $Owner, folded into a lookup
        ("owner.objectName" -> formatted "-- [grant] ..." lines, same format
        ConvertTo-SyncSqlGrantLine produces for MSSQL) that Build-Catalog.ps1
        parses into structured node.grants entries. Oracle has no DENY
        concept, so state is always 'GRANT'; ALL_TAB_PRIVS/ALL_COL_PRIVS
        don't reliably expose whether a grantee is a user or a role across
        versions, so granteeType is left blank. Best-effort: these views
        require privileges the connecting account may not have, so failures
        degrade to an empty index rather than failing the whole owner.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Connection,
        [Parameter(Mandatory)][string]$Owner,
        [Parameter(Mandatory)][string]$ServerName
    )

    $index = @{}
    try {
        $objectQuery = "SELECT GRANTEE, TABLE_NAME, PRIVILEGE FROM ALL_TAB_PRIVS WHERE OWNER = :owner ORDER BY TABLE_NAME, GRANTEE, PRIVILEGE"
        foreach ($row in (Invoke-SyncSqlOracleQuery -Connection $Connection -Query $objectQuery -Parameters @{ owner = $Owner })) {
            $key = "$Owner.$($row['TABLE_NAME'])"
            if (-not $index.ContainsKey($key)) { $index[$key] = [System.Collections.Generic.List[string]]::new() }
            $index[$key].Add((ConvertTo-SyncSqlGrantLine -Permission ([string]$row['PRIVILEGE']) -State 'GRANT' `
                        -Grantee ([string]$row['GRANTEE']) -GranteeType $null -Column $null))
        }

        $columnQuery = "SELECT GRANTEE, TABLE_NAME, COLUMN_NAME, PRIVILEGE FROM ALL_COL_PRIVS WHERE OWNER = :owner ORDER BY TABLE_NAME, GRANTEE, PRIVILEGE"
        foreach ($row in (Invoke-SyncSqlOracleQuery -Connection $Connection -Query $columnQuery -Parameters @{ owner = $Owner })) {
            $key = "$Owner.$($row['TABLE_NAME'])"
            if (-not $index.ContainsKey($key)) { $index[$key] = [System.Collections.Generic.List[string]]::new() }
            $index[$key].Add((ConvertTo-SyncSqlGrantLine -Permission ([string]$row['PRIVILEGE']) -State 'GRANT' `
                        -Grantee ([string]$row['GRANTEE']) -GranteeType $null -Column ([string]$row['COLUMN_NAME'])))
        }
    }
    catch {
        Write-SyncSqlLog "[$ServerName/$Owner] ALL_TAB_PRIVS/ALL_COL_PRIVS extraction failed (continuing without it): $($_.Exception.Message)" -Level WARN
        return @{}
    }
    return $index
}

function Get-SyncSqlOracleColumnListIndex {
    <#
        Full ordinal column list (name + data type) for every table/view
        owned by $Owner, from ALL_TAB_COLUMNS (covers both - Oracle doesn't
        distinguish table vs. view columns in that view). Same "-- [col]
        name|dataType" line format the MSSQL backend and Build-Catalog.ps1's
        parser use.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Connection,
        [Parameter(Mandatory)][string]$Owner,
        [Parameter(Mandatory)][string]$ServerName
    )

    $index = @{}
    try {
        $query = "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, COLUMN_ID FROM ALL_TAB_COLUMNS WHERE OWNER = :owner ORDER BY TABLE_NAME, COLUMN_ID"
        foreach ($row in (Invoke-SyncSqlOracleQuery -Connection $Connection -Query $query -Parameters @{ owner = $Owner })) {
            $key = "$Owner.$($row['TABLE_NAME'])"
            if (-not $index.ContainsKey($key)) { $index[$key] = [System.Collections.Generic.List[string]]::new() }
            $index[$key].Add("-- [col] $($row['COLUMN_NAME'])|$($row['DATA_TYPE'])")
        }
    }
    catch {
        Write-SyncSqlLog "[$ServerName/$Owner] ALL_TAB_COLUMNS extraction failed (continuing without it): $($_.Exception.Message)" -Level WARN
        return @{}
    }
    return $index
}

function Export-SyncSqlOracleServer {
    <#
        Orchestrates a full extraction of one Oracle server entry into
        $StagingRoot, honoring the resolved schema/objectName filters and
        the allowed object type list. Oracle has no "database" concept
        equivalent to MSSQL, so the configured service name is used as the
        DatabaseName path segment.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Server,
        [Parameter(Mandatory)][string]$Username,
        [Parameter(Mandatory)][string]$Password,
        [Parameter(Mandatory)]$Filters,
        [Parameter(Mandatory)][string[]]$AllowedObjectTypes,
        [Parameter(Mandatory)][string]$StagingRoot,
        [Parameter(Mandatory)][string]$DriverDllPath
    )

    $serverName  = $Server.name
    $serviceName = $Server.serviceName
    if ([string]::IsNullOrWhiteSpace($serviceName)) {
        throw "Oracle server '$serverName' is missing required key 'serviceName'."
    }

    Import-SyncSqlOracleDriver -DllPath $DriverDllPath

    $port = if ($Server.Contains('port') -and $Server.port) { [int]$Server.port } else { 1521 }
    $connection = New-SyncSqlOracleConnection -OracleHost $Server.host -Port $port -ServiceName $serviceName `
        -Username $Username -Password $Password

    $fileCount = 0
    try {
        $allOwners = Get-SyncSqlOracleSchemas -Connection $connection
        $allowedOwners = @($allOwners | Where-Object { Test-SyncSqlNameAllowed -Name $_ -Filter $Filters.schemas })

        if ($AllowedObjectTypes -contains 'Schemas') {
            foreach ($owner in $allowedOwners) {
                $definition = "-- Oracle schema/user: $owner"
                try {
                    $definition = Get-SyncSqlOracleDdl -Connection $connection -DdlObjectType 'USER' -ObjectName $owner -Owner $owner
                }
                catch {
                    Write-SyncSqlLog "[$serverName] Could not extract CREATE USER DDL for '$owner' (insufficient privileges?): $($_.Exception.Message)" -Level WARN
                }
                New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $serviceName `
                    -ObjectType 'Schemas' -ObjectName $owner -Definition $definition | Out-Null
                $fileCount++
            }
        }

        # Optional: object/column grants (ALL_TAB_PRIVS/ALL_COL_PRIVS) and the
        # full column list (ALL_TAB_COLUMNS) for tables/views, one query pass
        # per owner regardless of how many object types are being extracted
        # for that owner - both degrade to an empty index on failure.
        $grantsByOwner = @{}
        $columnListByOwner = @{}

        foreach ($configType in $script:SyncSqlOracleObjectTypeMap.Keys) {
            if ($AllowedObjectTypes -notcontains $configType) { continue }
            $oracleType = $script:SyncSqlOracleObjectTypeMap[$configType]
            $ddlType = $script:SyncSqlOracleDdlTypeMap[$oracleType]

            foreach ($owner in $allowedOwners) {
                if (-not $grantsByOwner.ContainsKey($owner)) {
                    $grantsByOwner[$owner] = Get-SyncSqlOracleGrantsIndex -Connection $connection -Owner $owner -ServerName $serverName
                }
                if ($oracleType -in @('TABLE', 'VIEW') -and -not $columnListByOwner.ContainsKey($owner)) {
                    $columnListByOwner[$owner] = Get-SyncSqlOracleColumnListIndex -Connection $connection -Owner $owner -ServerName $serverName
                }

                $objectNames = Get-SyncSqlOracleObjectList -Connection $connection -Owner $owner -OracleObjectType $oracleType
                foreach ($objectName in $objectNames) {
                    if (-not (Test-SyncSqlNameAllowed -Name $objectName -Filter $Filters.objectNames)) { continue }

                    try {
                        $definition = Get-SyncSqlOracleDdl -Connection $connection -DdlObjectType $ddlType -ObjectName $objectName -Owner $owner
                    }
                    catch {
                        Write-SyncSqlLog "[$serverName/$serviceName] Failed to extract DDL for $owner.$objectName ($configType): $($_.Exception.Message)" -Level WARN
                        continue
                    }

                    $key = "$owner.$objectName"
                    if ($oracleType -in @('TABLE', 'VIEW')) {
                        $definition = Add-SyncSqlSectionBlock -Definition $definition -Title 'Columns' -SectionIndex $columnListByOwner[$owner] -Key $key
                    }
                    $definition = Add-SyncSqlSectionBlock -Definition $definition -Title 'Grants' -SectionIndex $grantsByOwner[$owner] -Key $key

                    New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $serviceName `
                        -SchemaName $owner -ObjectType $configType -ObjectName $objectName -Definition $definition | Out-Null
                    $fileCount++
                }
            }
        }

        if ($AllowedObjectTypes -contains 'DatabaseLinks') {
            foreach ($link in (Get-SyncSqlOracleDatabaseLinks -Connection $connection)) {
                if (-not (Test-SyncSqlNameAllowed -Name $link.Owner -Filter $Filters.schemas)) { continue }
                if (-not (Test-SyncSqlNameAllowed -Name $link.DbLink -Filter $Filters.objectNames)) { continue }

                try {
                    $definition = Get-SyncSqlOracleDdl -Connection $connection -DdlObjectType 'DB_LINK' -ObjectName $link.DbLink -Owner $link.Owner
                }
                catch {
                    Write-SyncSqlLog "[$serverName/$serviceName] Failed to extract DDL for db link $($link.Owner).$($link.DbLink): $($_.Exception.Message)" -Level WARN
                    continue
                }

                New-SyncSqlObjectFile -StagingRoot $StagingRoot -ServerName $serverName -DatabaseName $serviceName `
                    -SchemaName $link.Owner -ObjectType 'DatabaseLinks' -ObjectName $link.DbLink -Definition $definition | Out-Null
                $fileCount++
            }
        }
    }
    finally {
        $connection.Dispose()
    }

    Write-SyncSqlLog "[$serverName] Wrote $fileCount object file(s)"
    return $fileCount
}

Export-ModuleMember -Function @(
    'Find-SyncSqlOracleDriverDll',
    'Import-SyncSqlOracleDriver',
    'New-SyncSqlOracleConnection',
    'Invoke-SyncSqlOracleQuery',
    'Get-SyncSqlOracleDdl',
    'Get-SyncSqlOracleSchemas',
    'Get-SyncSqlOracleObjectList',
    'Get-SyncSqlOracleDatabaseLinks',
    'Get-SyncSqlOracleGrantsIndex',
    'Get-SyncSqlOracleColumnListIndex',
    'Export-SyncSqlOracleServer'
)
