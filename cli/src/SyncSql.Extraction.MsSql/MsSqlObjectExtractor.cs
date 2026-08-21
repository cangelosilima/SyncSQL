using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;
using SyncSql.Extraction.MsSql.DdlAssembly;
using SyncSql.Extraction.MsSql.Sql;

namespace SyncSql.Extraction.MsSql;

/// <summary>
/// Extracts every allowed object from one MSSQL server: schemas, tables (columns/identity/defaults
/// rebuilt into a CREATE TABLE, plus Foreign Keys/Check Constraints/Indexes sections and a volatile
/// metrics snapshot), views/procedures/functions/triggers (via sys.sql_modules), synonyms, linked
/// servers, and a best-effort replication publication snapshot. A direct port of
/// SyncSql.MsSql.psm1's Export-SyncSqlMsSqlServer.
/// </summary>
public sealed class MsSqlObjectExtractor(ILogger<MsSqlObjectExtractor> logger) : IDatabaseObjectExtractor
{
    private static readonly IReadOnlyDictionary<string, string> TypeCodeMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["P"] = "StoredProcedures",
        ["V"] = "Views",
        ["TR"] = "Triggers",
        ["FN"] = "Functions",
        ["IF"] = "Functions",
        ["TF"] = "Functions",
    };

    private static readonly string[] ModuleObjectTypes = ["StoredProcedures", "Views", "Triggers", "Functions"];

    public DatabaseEngine Engine => DatabaseEngine.MsSql;

    public async Task<ExtractionOutcome> ExtractAsync(ServerConfig server, EffectiveFilters filters, ExtractionOptions options, CancellationToken cancellationToken)
    {
        List<ExtractedObject> objects = [];
        Dictionary<string, MetricsSnapshot> metrics = [];

        if (filters.ObjectTypes.Contains("LinkedServers"))
        {
            await using SqlConnection masterConnection = await MsSqlConnectionFactory.OpenAsync(server, "master", options.Credentials, cancellationToken);
            await ExtractLinkedServersAsync(masterConnection, server, filters, objects);
        }

        await using SqlConnection dbListConnection = await MsSqlConnectionFactory.OpenAsync(server, "master", options.Credentials, cancellationToken);
        IEnumerable<string> databases = await MsSqlCatalogReader.GetDatabasesAsync(dbListConnection);

        foreach (string database in databases)
        {
            if (!filters.Databases.IsAllowed(database))
            {
                continue;
            }

            logger.LogInformation("[{Server}/{Database}] Extracting", server.Name, database);
            await using SqlConnection connection = await MsSqlConnectionFactory.OpenAsync(server, database, options.Credentials, cancellationToken);
            await ExtractDatabaseAsync(connection, server, database, filters, options, objects, metrics);
        }

        return new ExtractionOutcome { Objects = objects, MetricsSnapshots = metrics };
    }

    private static async Task ExtractLinkedServersAsync(SqlConnection connection, ServerConfig server, EffectiveFilters filters, List<ExtractedObject> objects)
    {
        IEnumerable<LinkedServerRow> rows = await MsSqlCatalogReader.GetLinkedServersAsync(connection);
        foreach (IGrouping<string, LinkedServerRow> group in rows.GroupBy(r => r.LinkedServerName, StringComparer.OrdinalIgnoreCase))
        {
            if (!filters.ObjectNames.IsAllowed(group.Key))
            {
                continue;
            }

            LinkedServerRow first = group.First();
            string ddl = LinkedServerDdlBuilder.Build(
                group.Key, first.Product, first.Provider, first.DataSource, first.ProviderString, first.Catalog,
                [.. group.Select(g => (g.RemoteLoginName, g.UsesSelfCredential))]);

            objects.Add(new ExtractedObject
            {
                Server = server.Name,
                Database = "_ServerLevel",
                Type = "LinkedServers",
                Name = group.Key,
                Ddl = ddl,
                Engine = DatabaseEngine.MsSql,
            });
        }
    }

    private async Task ExtractDatabaseAsync(
        SqlConnection connection,
        ServerConfig server,
        string database,
        EffectiveFilters filters,
        ExtractionOptions options,
        List<ExtractedObject> objects,
        Dictionary<string, MetricsSnapshot> metrics)
    {
        IEnumerable<SchemaRow> schemaRows = await MsSqlCatalogReader.GetSchemasAsync(connection);
        Dictionary<string, bool> allowedSchemas = schemaRows.ToDictionary(
            r => r.SchemaName, r => filters.Schemas.IsAllowed(r.SchemaName), StringComparer.OrdinalIgnoreCase);

        if (filters.ObjectTypes.Contains("Schemas"))
        {
            foreach ((string schemaName, bool allowed) in allowedSchemas)
            {
                if (!allowed)
                {
                    continue;
                }

                objects.Add(new ExtractedObject
                {
                    Server = server.Name,
                    Database = database,
                    Type = "Schemas",
                    Name = schemaName,
                    Ddl = $"CREATE SCHEMA [{schemaName}];",
                    Engine = DatabaseEngine.MsSql,
                });
            }
        }

        Dictionary<string, ExtendedPropertiesEntry> extendedProperties = await TryLoadAsync(
            () => LoadExtendedPropertiesAsync(connection), server.Name, database, "sys.extended_properties");
        Dictionary<string, List<GrantEntry>> grants = await TryLoadAsync(
            () => LoadGrantsAsync(connection), server.Name, database, "sys.database_permissions");
        Dictionary<string, List<ExtractedColumn>> columnList = await TryLoadAsync(
            () => LoadColumnListAsync(connection), server.Name, database, "Column list");

        if (ModuleObjectTypes.Any(filters.ObjectTypes.Contains))
        {
            await ExtractModuleObjectsAsync(connection, server, database, filters, allowedSchemas, extendedProperties, grants, columnList, objects);
        }

        if (filters.ObjectTypes.Contains("Tables"))
        {
            await ExtractTablesAsync(connection, server, database, filters, options, allowedSchemas, extendedProperties, grants, columnList, objects, metrics);
        }

        if (filters.ObjectTypes.Contains("Synonyms"))
        {
            await ExtractSynonymsAsync(connection, server, database, filters, allowedSchemas, grants, objects);
        }

        if (filters.ObjectTypes.Contains("Replication"))
        {
            await ExtractReplicationAsync(connection, server, database, filters, objects);
        }
    }

    private static async Task ExtractModuleObjectsAsync(
        SqlConnection connection, ServerConfig server, string database, EffectiveFilters filters,
        IReadOnlyDictionary<string, bool> allowedSchemas,
        IReadOnlyDictionary<string, ExtendedPropertiesEntry> extendedProperties,
        IReadOnlyDictionary<string, List<GrantEntry>> grants,
        IReadOnlyDictionary<string, List<ExtractedColumn>> columnList,
        List<ExtractedObject> objects)
    {
        foreach (ModuleObjectRow row in await MsSqlCatalogReader.GetModuleObjectsAsync(connection))
        {
            if (!TypeCodeMap.TryGetValue(row.TypeCode.Trim(), out string? objectType) || !filters.ObjectTypes.Contains(objectType))
            {
                continue;
            }
            if (!allowedSchemas.TryGetValue(row.SchemaName, out bool schemaAllowed) || !schemaAllowed)
            {
                continue;
            }
            if (!filters.ObjectNames.IsAllowed(row.ObjectName))
            {
                continue;
            }

            string key = $"{row.SchemaName}.{row.ObjectName}";
            objects.Add(new ExtractedObject
            {
                Server = server.Name,
                Database = database,
                Schema = row.SchemaName,
                Type = objectType,
                Name = row.ObjectName,
                Ddl = row.Definition,
                Engine = DatabaseEngine.MsSql,
                Description = extendedProperties.GetValueOrDefault(key)?.ObjectDescription,
                Columns = objectType == "Views" ? MergeColumns(columnList, extendedProperties, key) : [],
                Grants = grants.GetValueOrDefault(key) ?? [],
            });
        }
    }

    private async Task ExtractTablesAsync(
        SqlConnection connection, ServerConfig server, string database, EffectiveFilters filters, ExtractionOptions options,
        IReadOnlyDictionary<string, bool> allowedSchemas,
        IReadOnlyDictionary<string, ExtendedPropertiesEntry> extendedProperties,
        IReadOnlyDictionary<string, List<GrantEntry>> grants,
        IReadOnlyDictionary<string, List<ExtractedColumn>> columnList,
        List<ExtractedObject> objects,
        Dictionary<string, MetricsSnapshot> metrics)
    {
        Dictionary<string, List<string>> foreignKeys = await TryLoadAsync(
            () => LoadTableSectionAsync(MsSqlCatalogReader.GetForeignKeysAsync, connection), server.Name, database, "Foreign key");
        Dictionary<string, List<string>> checkConstraints = await TryLoadAsync(
            () => LoadTableSectionAsync(MsSqlCatalogReader.GetCheckConstraintsAsync, connection), server.Name, database, "Check constraint");
        Dictionary<string, List<string>> indexes = await TryLoadAsync(
            () => LoadIndexSectionAsync(connection), server.Name, database, "Index");

        Dictionary<string, MetricsSnapshot> snapshotsByKey = options.CaptureMetrics
            ? await TryLoadAsync(() => LoadMetricsSnapshotsAsync(connection), server.Name, database, "Metrics")
            : [];

        foreach (TableRow table in await MsSqlCatalogReader.GetTablesAsync(connection))
        {
            if (!allowedSchemas.TryGetValue(table.SchemaName, out bool schemaAllowed) || !schemaAllowed)
            {
                continue;
            }
            if (!filters.ObjectNames.IsAllowed(table.TableName))
            {
                continue;
            }

            string key = $"{table.SchemaName}.{table.TableName}";
            string ddl = TableDdlBuilder.Build(table.SchemaName, table.TableName, table.ColumnsDdl, table.PrimaryKeyDdl);

            List<ExtractedSection> sections = [];
            AddSection(sections, "Foreign Keys", foreignKeys, key);
            AddSection(sections, "Check Constraints", checkConstraints, key);
            AddSection(sections, "Indexes", indexes, key);

            objects.Add(new ExtractedObject
            {
                Server = server.Name,
                Database = database,
                Schema = table.SchemaName,
                Type = "Tables",
                Name = table.TableName,
                Ddl = ddl,
                Engine = DatabaseEngine.MsSql,
                Description = extendedProperties.GetValueOrDefault(key)?.ObjectDescription,
                Columns = MergeColumns(columnList, extendedProperties, key),
                Grants = grants.GetValueOrDefault(key) ?? [],
                Sections = sections,
            });

            if (options.CaptureMetrics && snapshotsByKey.TryGetValue(key, out MetricsSnapshot? snapshot))
            {
                string id = ExtractedObjectFile.ObjectId(server.Name, database, table.SchemaName, "Tables", table.TableName);
                metrics[id] = snapshot;
            }
        }
    }

    private static async Task ExtractSynonymsAsync(
        SqlConnection connection, ServerConfig server, string database, EffectiveFilters filters,
        IReadOnlyDictionary<string, bool> allowedSchemas, IReadOnlyDictionary<string, List<GrantEntry>> grants,
        List<ExtractedObject> objects)
    {
        foreach (SynonymRow row in await MsSqlCatalogReader.GetSynonymsAsync(connection))
        {
            if (!allowedSchemas.TryGetValue(row.SchemaName, out bool schemaAllowed) || !schemaAllowed)
            {
                continue;
            }
            if (!filters.ObjectNames.IsAllowed(row.SynonymName))
            {
                continue;
            }

            string key = $"{row.SchemaName}.{row.SynonymName}";
            objects.Add(new ExtractedObject
            {
                Server = server.Name,
                Database = database,
                Schema = row.SchemaName,
                Type = "Synonyms",
                Name = row.SynonymName,
                Ddl = $"CREATE SYNONYM [{row.SchemaName}].[{row.SynonymName}] FOR {row.BaseObjectName};",
                Engine = DatabaseEngine.MsSql,
                Grants = grants.GetValueOrDefault(key) ?? [],
            });
        }
    }

    private async Task ExtractReplicationAsync(SqlConnection connection, ServerConfig server, string database, EffectiveFilters filters, List<ExtractedObject> objects)
    {
        try
        {
            foreach (ReplicationRow row in await MsSqlCatalogReader.GetReplicationAsync(connection))
            {
                if (row.PublicationName is null || !filters.ObjectNames.IsAllowed(row.PublicationName))
                {
                    continue;
                }

                objects.Add(new ExtractedObject
                {
                    Server = server.Name,
                    Database = database,
                    Type = "Replication",
                    Name = row.PublicationName,
                    Ddl = ReplicationDdlBuilder.Build(row.PublicationName, row.Description, row.Articles),
                    Engine = DatabaseEngine.MsSql,
                });
            }
        }
        catch (SqlException ex)
        {
            logger.LogWarning("[{Server}/{Database}] Replication extraction failed (continuing without it): {Message}", server.Name, database, ex.Message);
        }
    }

    // --- Optional/best-effort indexes: each degrades independently to empty on failure. ---

    private static async Task<Dictionary<string, ExtendedPropertiesEntry>> LoadExtendedPropertiesAsync(SqlConnection connection)
    {
        Dictionary<string, string> objectDescriptions = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Dictionary<string, string>> columnDescriptions = new(StringComparer.OrdinalIgnoreCase);

        foreach (ExtendedPropertyRow row in await MsSqlCatalogReader.GetExtendedPropertiesAsync(connection))
        {
            if (!row.PropertyName.Contains("Description", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string key = $"{row.SchemaName}.{row.ObjectName}";
            if (string.IsNullOrWhiteSpace(row.ColumnName))
            {
                objectDescriptions.TryAdd(key, row.PropertyValue ?? string.Empty);
            }
            else
            {
                if (!columnDescriptions.TryGetValue(key, out Dictionary<string, string>? perColumn))
                {
                    perColumn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    columnDescriptions[key] = perColumn;
                }
                perColumn[row.ColumnName] = row.PropertyValue ?? string.Empty;
            }
        }

        HashSet<string> keys = new(objectDescriptions.Keys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(columnDescriptions.Keys);

        Dictionary<string, ExtendedPropertiesEntry> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in keys)
        {
            result[key] = new ExtendedPropertiesEntry(
                objectDescriptions.GetValueOrDefault(key),
                columnDescriptions.GetValueOrDefault(key) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        return result;
    }

    private static async Task<Dictionary<string, List<GrantEntry>>> LoadGrantsAsync(SqlConnection connection)
    {
        Dictionary<string, List<GrantEntry>> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (GrantRow row in await MsSqlCatalogReader.GetGrantsAsync(connection))
        {
            string key = $"{row.SchemaName}.{row.ObjectName}";
            if (!index.TryGetValue(key, out List<GrantEntry>? list))
            {
                list = [];
                index[key] = list;
            }

            GrantState state = string.Equals(row.StateDesc, "DENY", StringComparison.OrdinalIgnoreCase) ? GrantState.Deny : GrantState.Grant;
            list.Add(new GrantEntry(row.PermissionName, state, row.GranteeName, row.GranteeType, row.ColumnName));
        }

        return index;
    }

    private static async Task<Dictionary<string, List<ExtractedColumn>>> LoadColumnListAsync(SqlConnection connection)
    {
        Dictionary<string, List<ExtractedColumn>> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (ColumnListRow row in await MsSqlCatalogReader.GetColumnListAsync(connection))
        {
            string key = $"{row.SchemaName}.{row.TableName}";
            if (!index.TryGetValue(key, out List<ExtractedColumn>? list))
            {
                list = [];
                index[key] = list;
            }

            list.Add(new ExtractedColumn(row.ColumnName, row.DataType, null));
        }

        return index;
    }

    private static async Task<Dictionary<string, List<string>>> LoadTableSectionAsync(Func<SqlConnection, Task<IEnumerable<TableSectionRow>>> query, SqlConnection connection)
    {
        Dictionary<string, List<string>> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (TableSectionRow row in await query(connection))
        {
            string key = $"{row.SchemaName}.{row.TableName}";
            if (!index.TryGetValue(key, out List<string>? list))
            {
                list = [];
                index[key] = list;
            }

            list.Add(row.Definition);
        }

        return index;
    }

    private static async Task<Dictionary<string, List<string>>> LoadIndexSectionAsync(SqlConnection connection)
    {
        Dictionary<string, List<string>> index = new(StringComparer.OrdinalIgnoreCase);
        foreach (IndexRow row in await MsSqlCatalogReader.GetIndexesAsync(connection))
        {
            string key = $"{row.SchemaName}.{row.TableName}";
            if (!index.TryGetValue(key, out List<string>? list))
            {
                list = [];
                index[key] = list;
            }

            list.Add(IndexDdlBuilder.Build(row.SchemaName, row.TableName, row.IndexName, row.IsUnique, row.TypeDesc, row.KeyColumns, row.IncludedColumns));
        }

        return index;
    }

    private static async Task<Dictionary<string, MetricsSnapshot>> LoadMetricsSnapshotsAsync(SqlConnection connection)
    {
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        Dictionary<string, MetricsSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);

        foreach (TableVolumeRow row in await MsSqlCatalogReader.GetTableVolumeAsync(connection))
        {
            string key = $"{row.SchemaName}.{row.TableName}";
            snapshots[key] = new MetricsSnapshot
            {
                CapturedAt = capturedAt,
                RowCount = row.RowCount,
                ReservedKB = row.ReservedKB,
                DataKB = row.DataKB,
                IndexKB = row.IndexKB,
            };
        }

        Dictionary<string, List<CatalogIndexMetric>> indexesByTable = new(StringComparer.OrdinalIgnoreCase);
        foreach (IndexMetricRow row in await MsSqlCatalogReader.GetIndexMetricsAsync(connection))
        {
            string key = $"{row.SchemaName}.{row.TableName}";
            if (!indexesByTable.TryGetValue(key, out List<CatalogIndexMetric>? list))
            {
                list = [];
                indexesByTable[key] = list;
            }

            list.Add(new CatalogIndexMetric
            {
                Name = row.IndexName,
                FragmentationPct = row.FragmentationPct is { } f ? Math.Round(f, 2) : null,
                PageCount = row.PageCount,
                Seeks = row.Seeks,
                Scans = row.Scans,
                Lookups = row.Lookups,
                Updates = row.Updates,
            });
        }

        Dictionary<string, List<CatalogStatMetric>> statsByTable = new(StringComparer.OrdinalIgnoreCase);
        foreach (OptimizerStatisticRow row in await MsSqlCatalogReader.GetOptimizerStatisticsAsync(connection))
        {
            string key = $"{row.SchemaName}.{row.TableName}";
            if (!statsByTable.TryGetValue(key, out List<CatalogStatMetric>? list))
            {
                list = [];
                statsByTable[key] = list;
            }

            list.Add(new CatalogStatMetric
            {
                Name = row.StatName,
                Rows = row.Rows,
                RowsSampled = row.RowsSampled,
                Steps = row.Steps,
                ModificationCounter = row.ModificationCounter,
                LastUpdated = row.LastUpdated is { } d ? new DateTimeOffset(d, TimeSpan.Zero) : null,
            });
        }

        foreach ((string key, MetricsSnapshot snapshot) in snapshots.ToArray())
        {
            snapshots[key] = snapshot with
            {
                Indexes = indexesByTable.GetValueOrDefault(key) ?? [],
                Statistics = statsByTable.GetValueOrDefault(key) ?? [],
            };
        }

        return snapshots;
    }

    private static void AddSection(List<ExtractedSection> sections, string title, IReadOnlyDictionary<string, List<string>> index, string key)
    {
        if (index.TryGetValue(key, out List<string>? lines))
        {
            sections.Add(new ExtractedSection(title, string.Join('\n', lines)));
        }
    }

    private static IReadOnlyList<ExtractedColumn> MergeColumns(
        IReadOnlyDictionary<string, List<ExtractedColumn>> columnList,
        IReadOnlyDictionary<string, ExtendedPropertiesEntry> extendedProperties,
        string key)
    {
        if (!columnList.TryGetValue(key, out List<ExtractedColumn>? columns))
        {
            return [];
        }

        IReadOnlyDictionary<string, string> descriptions = extendedProperties.GetValueOrDefault(key)?.ColumnDescriptions
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return [.. columns.Select(c => descriptions.TryGetValue(c.Name, out string? description)
            ? c with { Description = description }
            : c)];
    }

    private async Task<T> TryLoadAsync<T>(Func<Task<T>> load, string serverName, string database, string sectionName) where T : new()
    {
        try
        {
            return await load();
        }
        catch (SqlException ex)
        {
            logger.LogWarning("[{Server}/{Database}] {Section} extraction failed (continuing without it): {Message}", serverName, database, sectionName, ex.Message);
            return new T();
        }
    }
}

/// <summary>One object's/its columns' documented descriptions (MS_Description and friends, class = 1) - object-level plus per-column.</summary>
internal sealed record ExtendedPropertiesEntry(string? ObjectDescription, IReadOnlyDictionary<string, string> ColumnDescriptions);
