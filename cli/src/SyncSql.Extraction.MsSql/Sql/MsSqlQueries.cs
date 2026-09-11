namespace SyncSql.Extraction.MsSql.Sql;

/// <summary>
/// Every catalog-view query the MSSQL extractor runs, verbatim from SyncSql.MsSql.psm1 - none of these
/// take parameters (they're static catalog-view/DMV queries; the only variable is which database the
/// connection is open against), so there is no SQL-injection surface here.
/// </summary>
internal static class MsSqlQueries
{
    public const string Databases = "SELECT name FROM sys.databases WHERE state = 0 ORDER BY name;";

    public const string ServiceBrokerGuid = "SELECT service_broker_guid FROM sys.databases WHERE database_id = DB_ID();";

    public const string ColumnDefinitions = """
        SELECT c.object_id AS ObjectId, c.name AS ColumnName, ty.name AS TypeName, SCHEMA_NAME(ty.schema_id) AS TypeSchema,
            ty.is_user_defined AS IsUserDefined, c.max_length AS MaxLength, c.precision AS Precision, c.scale AS Scale,
            c.is_nullable AS IsNullable, cc.definition AS ComputedDefinition, ISNULL(cc.is_persisted, 0) AS IsPersisted,
            c.collation_name AS CollationName, dc.name AS DefaultName, dc.definition AS DefaultDefinition,
            CONVERT(varchar(40), ic.seed_value) AS IdentitySeed, CONVERT(varchar(40), ic.increment_value) AS IdentityIncrement,
            ISNULL(ic.is_not_for_replication, 0) AS IdentityNotForReplication,
            c.is_rowguidcol AS IsRowGuid, c.is_sparse AS IsSparse, c.is_column_set AS IsColumnSet, c.is_filestream AS IsFileStream,
            SCHEMA_NAME(x.schema_id) AS XmlCollectionSchema, x.name AS XmlCollectionName, c.is_xml_document AS IsXmlDocument
        FROM sys.columns c
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
        LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
        LEFT JOIN sys.xml_schema_collections x ON x.xml_collection_id = c.xml_collection_id AND c.xml_collection_id > 0
        WHERE EXISTS (SELECT 1 FROM sys.tables t WHERE t.object_id = c.object_id AND t.is_ms_shipped = 0)
            OR EXISTS (SELECT 1 FROM sys.table_types tt WHERE tt.type_table_object_id = c.object_id)
        ORDER BY c.object_id, c.column_id;
        """;

    public const string Types = """
        SELECT s.name AS SchemaName, ty.name AS TypeName, TYPE_NAME(ty.system_type_id) AS BaseTypeName,
            ty.max_length AS MaxLength, ty.precision AS Precision, ty.scale AS Scale, ty.is_nullable AS IsNullable,
            ty.is_table_type AS IsTableType, ty.is_assembly_type AS IsAssemblyType,
            ISNULL(tt.is_memory_optimized, 0) AS IsMemoryOptimized, tt.type_table_object_id AS TableObjectId,
            a.name AS AssemblyName, at.assembly_class AS AssemblyClass,
            CASE WHEN ty.default_object_id > 0 THEN QUOTENAME(OBJECT_SCHEMA_NAME(ty.default_object_id)) + '.' + QUOTENAME(OBJECT_NAME(ty.default_object_id)) END AS DefaultName,
            CASE WHEN ty.rule_object_id > 0 THEN QUOTENAME(OBJECT_SCHEMA_NAME(ty.rule_object_id)) + '.' + QUOTENAME(OBJECT_NAME(ty.rule_object_id)) END AS RuleName,
            STUFF((SELECT ',' + CHAR(10) + '    ' + d.Definition
                FROM (
                    SELECT i.index_id AS SortOrder,
                        CASE WHEN i.is_primary_key = 1 THEN 'PRIMARY KEY ' WHEN i.is_unique_constraint = 1 THEN 'UNIQUE '
                            ELSE 'INDEX ' + QUOTENAME(i.name) + ' ' END + REPLACE((i.type_desc COLLATE DATABASE_DEFAULT), '_', ' ') + ' (' +
                        STUFF((SELECT ', ' + QUOTENAME(c.name) + CASE WHEN i.type = 7 THEN '' WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE ' ASC' END
                            FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                            WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
                            ORDER BY ic.key_ordinal FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') + ')' +
                        CASE WHEN i.type = 7 THEN ' WITH (BUCKET_COUNT = ' + CONVERT(varchar(20), hi.bucket_count) + ')'
                            WHEN i.ignore_dup_key = 1 THEN ' WITH (IGNORE_DUP_KEY = ON)' ELSE '' END AS Definition
                    FROM sys.indexes i LEFT JOIN sys.hash_indexes hi ON hi.object_id = i.object_id AND hi.index_id = i.index_id
                    WHERE i.object_id = tt.type_table_object_id AND i.index_id > 0
                    UNION ALL
                    SELECT 10000 + cc.object_id, 'CHECK ' + cc.definition
                    FROM sys.check_constraints cc WHERE cc.parent_object_id = tt.type_table_object_id
                ) d ORDER BY d.SortOrder FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') AS ConstraintsDdl
        FROM sys.types ty JOIN sys.schemas s ON s.schema_id = ty.schema_id
        LEFT JOIN sys.table_types tt ON tt.user_type_id = ty.user_type_id
        LEFT JOIN sys.assembly_types at ON at.user_type_id = ty.user_type_id
        LEFT JOIN sys.assemblies a ON a.assembly_id = at.assembly_id
        WHERE ty.is_user_defined = 1
        ORDER BY s.name, ty.name;
        """;

    // Schema/type permissions live in classes 3/6, not OBJECT_OR_COLUMN (class 1).
    // Preserve grant option, column-level REVOKE exceptions and the granting principal.
    public const string Permissions = """
        SELECT
            CASE p.class WHEN 3 THEN 'SCHEMA' WHEN 6 THEN 'TYPE' ELSE 'OBJECT' END AS Scope,
            s.name AS SchemaName, CASE WHEN p.class = 3 THEN s.name ELSE COALESCE(o.name, ty.name) END AS ObjectName,
            CASE p.state WHEN 'D' THEN 'DENY' WHEN 'R' THEN 'REVOKE' ELSE 'GRANT' END + ' ' + (p.permission_name COLLATE DATABASE_DEFAULT) +
            ' ON ' + CASE p.class WHEN 3 THEN 'SCHEMA::' + QUOTENAME(s.name)
                WHEN 6 THEN 'TYPE::' + QUOTENAME(s.name) + '.' + QUOTENAME(ty.name)
                ELSE 'OBJECT::' + QUOTENAME(s.name) + '.' + QUOTENAME(o.name) END +
            CASE WHEN p.class = 1 AND p.minor_id > 0 THEN ' (' + QUOTENAME(c.name) + ')' ELSE '' END +
            CASE p.state WHEN 'R' THEN ' FROM ' ELSE ' TO ' END + QUOTENAME(grantee.name) +
            CASE p.state WHEN 'W' THEN ' WITH GRANT OPTION' ELSE '' END + ' AS ' + QUOTENAME(grantor.name) + ';' AS Definition
        FROM sys.database_permissions p
        LEFT JOIN sys.objects o ON p.class = 1 AND o.object_id = p.major_id
        LEFT JOIN sys.types ty ON p.class = 6 AND ty.user_type_id = p.major_id
        JOIN sys.schemas s ON s.schema_id = CASE p.class WHEN 3 THEN p.major_id WHEN 6 THEN ty.schema_id ELSE o.schema_id END
        LEFT JOIN sys.columns c ON p.class = 1 AND c.object_id = p.major_id AND c.column_id = p.minor_id
        JOIN sys.database_principals grantee ON grantee.principal_id = p.grantee_principal_id
        JOIN sys.database_principals grantor ON grantor.principal_id = p.grantor_principal_id
        WHERE p.class IN (1, 3, 6)
        ORDER BY s.name, ObjectName, p.class, grantee.name, p.permission_name, p.minor_id, grantor.name;
        """;

    public const string Ownership = """
        SELECT 'OBJECT' AS Scope, s.name AS SchemaName, o.name AS ObjectName,
            'ALTER AUTHORIZATION ON OBJECT::' + QUOTENAME(s.name) + '.' + QUOTENAME(o.name) + ' TO ' + QUOTENAME(p.name) + ';' AS Definition
        FROM sys.objects o
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        JOIN sys.database_principals p ON p.principal_id = o.principal_id
        WHERE o.is_ms_shipped = 0
        UNION ALL
        SELECT 'TYPE', s.name, t.name,
            'ALTER AUTHORIZATION ON TYPE::' + QUOTENAME(s.name) + '.' + QUOTENAME(t.name) + ' TO ' + QUOTENAME(p.name) + ';'
        FROM sys.types t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.database_principals p ON p.principal_id = t.principal_id
        WHERE t.is_user_defined = 1
        ORDER BY SchemaName, ObjectName, Scope;
        """;

    public const string PropertyDefinitions = """
        SELECT CASE ep.class WHEN 3 THEN 'SCHEMA' WHEN 6 THEN 'TYPE' ELSE 'OBJECT' END AS Scope,
            s.name AS SchemaName, CASE WHEN ep.class = 3 THEN s.name ELSE COALESCE(o.name, ty.name) END AS ObjectName,
            'DECLARE @propertyValue sql_variant = ' + CASE WHEN ep.value IS NULL THEN 'NULL' ELSE
                'CONVERT(' + CONVERT(nvarchar(128), SQL_VARIANT_PROPERTY(ep.value, 'BaseType')) +
                CASE
                    WHEN SQL_VARIANT_PROPERTY(ep.value, 'BaseType') IN ('nvarchar', 'nchar')
                        THEN '(' + CONVERT(varchar(10), CONVERT(int, SQL_VARIANT_PROPERTY(ep.value, 'MaxLength')) / 2) + ')'
                    WHEN SQL_VARIANT_PROPERTY(ep.value, 'BaseType') IN ('varchar', 'char', 'varbinary', 'binary')
                        THEN '(' + CONVERT(varchar(10), SQL_VARIANT_PROPERTY(ep.value, 'MaxLength')) + ')'
                    WHEN SQL_VARIANT_PROPERTY(ep.value, 'BaseType') IN ('decimal', 'numeric')
                        THEN '(' + CONVERT(varchar(10), SQL_VARIANT_PROPERTY(ep.value, 'Precision')) + ',' + CONVERT(varchar(10), SQL_VARIANT_PROPERTY(ep.value, 'Scale')) + ')'
                    WHEN SQL_VARIANT_PROPERTY(ep.value, 'BaseType') IN ('time', 'datetime2', 'datetimeoffset')
                        THEN '(' + CONVERT(varchar(10), SQL_VARIANT_PROPERTY(ep.value, 'Scale')) + ')'
                    ELSE '' END + ', ' +
                CASE WHEN SQL_VARIANT_PROPERTY(ep.value, 'BaseType') IN ('varbinary', 'binary')
                    THEN CONVERT(varchar(max), CONVERT(varbinary(max), ep.value), 1)
                    ELSE 'N''' + REPLACE(CASE WHEN SQL_VARIANT_PROPERTY(ep.value, 'BaseType') IN ('float', 'real')
                        THEN CONVERT(nvarchar(max), CONVERT(float, ep.value), 3)
                        ELSE CONVERT(nvarchar(max), ep.value, 126) END, '''', '''''') + '''' END + ', 126)' END + ';' + CHAR(10) +
            'EXEC sys.sp_addextendedproperty @name = N''' + REPLACE(ep.name, '''', '''''') + ''', @value = @propertyValue, ' +
            '@level0type = N''SCHEMA'', @level0name = N''' + REPLACE(s.name, '''', '''''') + '''' +
            CASE WHEN ep.class = 3 THEN '' ELSE ', @level1type = N''' +
                CASE WHEN ep.class = 6 THEN 'TYPE' WHEN o.type = 'U' THEN 'TABLE' WHEN o.type = 'V' THEN 'VIEW'
                    WHEN o.type IN ('P', 'PC') THEN 'PROCEDURE' WHEN o.type IN ('TR', 'TA') THEN 'TRIGGER'
                    WHEN o.type = 'SN' THEN 'SYNONYM' ELSE 'FUNCTION' END +
                ''', @level1name = N''' + REPLACE(COALESCE(o.name, ty.name), '''', '''''') + '''' END +
            CASE WHEN ep.class = 1 AND ep.minor_id > 0 THEN ', @level2type = N''COLUMN'', @level2name = N''' + REPLACE(c.name, '''', '''''') + '''' ELSE '' END +
            ';' + CHAR(10) + 'GO' AS Definition
        FROM sys.extended_properties ep
        LEFT JOIN sys.objects o ON ep.class = 1 AND o.object_id = ep.major_id
        LEFT JOIN sys.types ty ON ep.class = 6 AND ty.user_type_id = ep.major_id
        JOIN sys.schemas s ON s.schema_id = CASE ep.class WHEN 3 THEN ep.major_id WHEN 6 THEN ty.schema_id ELSE o.schema_id END
        LEFT JOIN sys.columns c ON ep.class = 1 AND c.object_id = ep.major_id AND c.column_id = ep.minor_id
        WHERE ep.class IN (3, 6) OR (ep.class = 1 AND o.type IN ('U', 'V', 'P', 'PC', 'TR', 'TA', 'SN', 'FN', 'IF', 'TF', 'FS', 'FT'))
        ORDER BY s.name, ObjectName, ep.class, ep.minor_id, ep.name;
        """;

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
            m.definition AS Definition,
            m.uses_ansi_nulls AS UsesAnsiNulls,
            m.uses_quoted_identifier AS UsesQuotedIdentifier,
            ISNULL(tr.is_disabled, 0) AS IsDisabled,
            OBJECT_SCHEMA_NAME(tr.parent_id) AS ParentSchemaName,
            OBJECT_NAME(tr.parent_id) AS ParentObjectName
        FROM sys.sql_modules m
        JOIN sys.objects o ON o.object_id = m.object_id
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        LEFT JOIN sys.triggers tr ON tr.object_id = o.object_id
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
            t.object_id AS ObjectId,

            (
                SELECT '  CONSTRAINT ' + QUOTENAME(kc.name) + ' PRIMARY KEY ' + (i.type_desc COLLATE DATABASE_DEFAULT) + ' (' +
                    STUFF((
                        SELECT ', ' + QUOTENAME(c2.name) + CASE WHEN ic2.is_descending_key = 1 THEN ' DESC' ELSE ' ASC' END
                        FROM sys.index_columns ic2
                        JOIN sys.columns c2 ON c2.object_id = ic2.object_id AND c2.column_id = ic2.column_id
                        WHERE ic2.object_id = kc.parent_object_id AND ic2.index_id = kc.unique_index_id AND ic2.key_ordinal > 0
                        ORDER BY ic2.key_ordinal
                        FOR XML PATH(''), TYPE
                    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') + ')'
                FROM sys.key_constraints kc
                JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
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
        WHERE perm.class = 1 AND perm.major_id > 0 AND perm.state_desc IN ('GRANT', 'DENY', 'GRANT_WITH_GRANT_OPTION')
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

    /// <summary>
    /// Row counts and reserved/data/index size (KB) - the same sys.dm_db_partition_stats/sys.allocation_units
    /// aggregation sp_spaceused uses. ROWCOUNT is a reserved word (SET ROWCOUNT), so that alias has to be
    /// bracketed or the whole batch fails to parse - "Incorrect syntax near the keyword 'RowCount'" - and
    /// takes every table's volume metrics for that database with it. The brackets are SQL syntax only:
    /// the column still comes back named RowCount for Dapper to map.
    /// </summary>
    public const string TableVolume = """
        SELECT
            sch.name AS SchemaName,
            t.name   AS TableName,
            SUM(CASE WHEN i.index_id IN (0, 1) THEN p.rows ELSE 0 END) AS [RowCount],
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
            sp.rows AS [Rows],
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
            'ALTER TABLE ' + QUOTENAME(sch.name) + '.' + QUOTENAME(t.name) +
            CASE WHEN fk.is_not_trusted = 1 THEN ' WITH NOCHECK' ELSE ' WITH CHECK' END + ' ADD CONSTRAINT ' + QUOTENAME(fk.name) +
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
            ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') + ')' +
            ' ON DELETE ' + REPLACE((fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT), '_', ' ') +
            ' ON UPDATE ' + REPLACE((fk.update_referential_action_desc COLLATE DATABASE_DEFAULT), '_', ' ') +
            CASE WHEN fk.is_not_for_replication = 1 THEN ' NOT FOR REPLICATION' ELSE '' END + ';' +
            CASE WHEN fk.is_disabled = 1 THEN CHAR(10) + 'ALTER TABLE ' + QUOTENAME(sch.name) + '.' + QUOTENAME(t.name) +
                ' NOCHECK CONSTRAINT ' + QUOTENAME(fk.name) + ';' ELSE '' END AS Definition
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
            'ALTER TABLE ' + QUOTENAME(sch.name) + '.' + QUOTENAME(t.name) +
            CASE WHEN cc.is_not_trusted = 1 THEN ' WITH NOCHECK' ELSE ' WITH CHECK' END + ' ADD CONSTRAINT ' + QUOTENAME(cc.name) +
            ' CHECK ' + CASE WHEN cc.is_not_for_replication = 1 THEN 'NOT FOR REPLICATION ' ELSE '' END + cc.definition + ';' +
            CASE WHEN cc.is_disabled = 1 THEN CHAR(10) + 'ALTER TABLE ' + QUOTENAME(sch.name) + '.' + QUOTENAME(t.name) +
                ' NOCHECK CONSTRAINT ' + QUOTENAME(cc.name) + ';' ELSE '' END AS Definition
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
            (i.type_desc COLLATE DATABASE_DEFAULT) AS TypeDesc,
            i.filter_definition AS FilterDefinition,
            i.fill_factor AS [FillFactor],
            i.is_padded AS IsPadded,
            i.ignore_dup_key AS IgnoreDupKey,
            i.allow_row_locks AS AllowRowLocks,
            i.allow_page_locks AS AllowPageLocks,
            i.is_disabled AS IsDisabled,
            STUFF((
                SELECT ', ' + QUOTENAME(c.name) + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE ' ASC' END
                FROM sys.index_columns ic
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0 AND ic.key_ordinal > 0
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

    public const string UniqueConstraints = """
        SELECT s.name AS SchemaName, t.name AS TableName,
            'ALTER TABLE ' + QUOTENAME(s.name) + '.' + QUOTENAME(t.name) + ' ADD CONSTRAINT ' + QUOTENAME(kc.name) +
            ' UNIQUE ' + (i.type_desc COLLATE DATABASE_DEFAULT) + ' (' +
            STUFF((SELECT ', ' + QUOTENAME(c.name) + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE ' ASC' END
                FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
                ORDER BY ic.key_ordinal FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') + ')' +
            CASE WHEN i.ignore_dup_key = 1 THEN ' WITH (IGNORE_DUP_KEY = ON)' ELSE '' END + ';' AS Definition
        FROM sys.key_constraints kc
        JOIN sys.tables t ON t.object_id = kc.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
        WHERE kc.type = 'UQ' AND t.is_ms_shipped = 0
        ORDER BY s.name, t.name, kc.name;
        """;

    /// <summary>
    /// Best-effort, informational-only publications/articles snapshot; empty result set (not an error) on
    /// a database that isn't a replication Publisher. Subscriber enumeration is deliberately out of scope
    /// - subscription table shapes vary too much across SQL Server versions/topologies.
    /// </summary>
    public const string Replication = """
        IF OBJECT_ID('dbo.syspublications') IS NULL
        BEGIN
            SELECT CAST(NULL AS sysname) AS PublicationName, CAST(NULL AS NVARCHAR(MAX)) AS Description,
                CAST(NULL AS NVARCHAR(MAX)) AS Articles, CAST(NULL AS NVARCHAR(MAX)) AS SourceDefinitions WHERE 1 = 0;
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
                ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS Articles,
                (SELECT 'EXEC sys.sp_addarticle @publication = N''' + REPLACE(p.name, '''', '''''')
                    + ''', @article = N''' + REPLACE(a.name, '''', '''''')
                    + ''', @source_owner = N''' + REPLACE(OBJECT_SCHEMA_NAME(a.objid), '''', '''''')
                    + ''', @source_object = N''' + REPLACE(OBJECT_NAME(a.objid), '''', '''''') + ''';' + CHAR(10)
                 FROM dbo.sysarticles a WHERE a.pubid = p.pubid ORDER BY a.name
                 FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)') AS SourceDefinitions
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
            s.location AS Location,
            s.is_data_access_enabled AS DataAccess,
            s.is_remote_login_enabled AS Rpc,
            s.is_rpc_out_enabled AS RpcOut,
            s.is_collation_compatible AS CollationCompatible,
            s.uses_remote_collation AS UseRemoteCollation,
            s.lazy_schema_validation AS LazySchemaValidation,
            s.is_remote_proc_transaction_promotion_enabled AS RemoteProcTransactionPromotion,
            s.collation_name AS CollationName,
            s.connect_timeout AS ConnectTimeout,
            s.query_timeout AS QueryTimeout,
            sp.name AS LocalLoginName,
            ll.remote_name AS RemoteLoginName,
            ll.uses_self_credential AS UsesSelfCredential
        FROM sys.servers s
        LEFT JOIN sys.linked_logins ll ON ll.server_id = s.server_id
        LEFT JOIN sys.server_principals sp ON sp.principal_id = ll.local_principal_id
        WHERE s.is_linked = 1
        ORDER BY s.name, ll.local_principal_id, ll.remote_name;
        """;
}
