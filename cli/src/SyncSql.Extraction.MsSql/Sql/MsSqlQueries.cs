namespace SyncSql.Extraction.MsSql.Sql;

/// <summary>
/// Every catalog-view query the MSSQL extractor runs, verbatim from SyncSql.MsSql.psm1 - none of these
/// take parameters (they're static catalog-view/DMV queries; the only variable is which database the
/// connection is open against), so there is no SQL-injection surface here.
/// </summary>
internal static class MsSqlQueries
{
    public const string Databases = "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name;";

    public const string Schemas = """
        SELECT s.name AS SchemaName, dp.name AS OwnerName
        FROM sys.schemas s
        JOIN sys.database_principals dp ON dp.principal_id = s.principal_id
        ORDER BY s.name;
        """;

    /// <summary>Procedures, views, functions and DML triggers in one pass - they all live in sys.sql_modules.</summary>
    public const string ModuleObjects = """
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
        """;

    /// <summary>
    /// MSSQL doesn't store a reusable "CREATE TABLE" text the way it does for procedures/views, so this
    /// rebuilds an approximate DDL fragment from catalog views: columns, data types, identity, defaults
    /// and the primary key. See <see cref="DdlAssembly.TableDdlBuilder"/> for how ColumnsDdl/PrimaryKeyDdl
    /// become the final CREATE TABLE text.
    /// </summary>
    public const string Tables = """
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
        """;

    public const string Synonyms = """
        SELECT sch.name AS SchemaName, syn.name AS SynonymName, syn.base_object_name AS BaseObjectName
        FROM sys.synonyms syn
        JOIN sys.schemas sch ON sch.schema_id = syn.schema_id
        ORDER BY sch.name, syn.name;
        """;

    /// <summary>MS_Description and friends (class = 1: object + column level).</summary>
    public const string ExtendedProperties = """
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
        """;

    /// <summary>Object/column-level GRANT/DENY (sys.database_permissions, class = 1: OBJECT_OR_COLUMN).</summary>
    public const string Grants = """
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
        """;

    /// <summary>Full ordinal column list (name + data type) for tables/views - not limited to columns with a description.</summary>
    public const string ColumnList = """
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
        """;

    /// <summary>Row counts and reserved/data/index size (KB) - the same sys.dm_db_partition_stats/sys.allocation_units aggregation sp_spaceused uses.</summary>
    public const string TableVolume = """
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
        """;

    /// <summary>Per-index fragmentation/page count (sys.dm_db_index_physical_stats, cheap 'LIMITED' mode) and usage counters (sys.dm_db_index_usage_stats, resets on service restart).</summary>
    public const string IndexMetrics = """
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
        """;

    /// <summary>The statistics actually consulted by the query optimizer - not the CREATE STATISTICS object definition - via sys.dm_db_stats_properties.</summary>
    public const string OptimizerStatistics = """
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
        """;

    public const string ForeignKeys = """
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
        """;

    public const string CheckConstraints = """
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
        """;

    /// <summary>Non-PK, non-unique-constraint indexes (those are already represented via the table's PRIMARY KEY clause).</summary>
    public const string Indexes = """
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
        """;

    /// <summary>
    /// Best-effort, informational-only publications/articles snapshot; empty result set (not an error) on
    /// a database that isn't a replication Publisher. Subscriber enumeration is deliberately out of scope
    /// - subscription table shapes vary too much across SQL Server versions/topologies.
    /// </summary>
    public const string Replication = """
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
        """;

    public const string LinkedServers = """
        SELECT
            s.name AS LinkedServerName,
            s.product AS Product,
            s.provider AS Provider,
            s.data_source AS DataSource,
            s.provider_string AS ProviderString,
            s.catalog AS Catalog,
            ll.remote_name AS RemoteLoginName,
            ll.uses_self_credential AS UsesSelfCredential
        FROM sys.servers s
        LEFT JOIN sys.linked_logins ll ON ll.server_id = s.server_id AND ll.local_principal_id <> 0
        WHERE s.is_linked = 1
        ORDER BY s.name;
        """;
}
