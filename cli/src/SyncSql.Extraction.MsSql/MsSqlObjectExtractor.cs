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
public sealed class MsSqlObjectExtractor(ILogger<MsSqlObjectExtractor> logger, TimeProvider timeProvider) : IDatabaseObjectExtractor
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
        List<DiscoveredLinkedServer> discoveredLinkedServers = [];

        // One read of sys.servers serves both callers: the LinkedServers objects to diff, and the leads
        // the caller may want to follow up on (see LinkedServerFollowUpPlanner).
        if (filters.ObjectTypes.Contains("LinkedServers") || options.DiscoverLinkedServers)
        {
            await using SqlConnection masterConnection = await MsSqlConnectionFactory.OpenAsync(server, "master", options.Credentials, cancellationToken);
            IReadOnlyList<IGrouping<string, LinkedServerRow>> linkedServers =
                [.. (await MsSqlCatalogReader.GetLinkedServersAsync(masterConnection)).GroupBy(r => r.LinkedServerName, StringComparer.OrdinalIgnoreCase)];

            if (filters.ObjectTypes.Contains("LinkedServers"))
            {
                AddLinkedServerObjects(linkedServers, server, filters, objects);
            }

            if (options.DiscoverLinkedServers)
            {
                discoveredLinkedServers = [.. linkedServers.Select(ToDiscoveredLinkedServer)];
            }
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

        return new ExtractionOutcome
        {
            Objects = objects,
            MetricsSnapshots = metrics,
            DiscoveredLinkedServers = discoveredLinkedServers,
        };
    }

    /// <summary>
    /// One linked server as a follow-up lead: where it points, which database it pins, and which remote
    /// login it maps to - the last one being what decides whether the credentials in hand will work on
    /// the far side. sys.linked_logins.uses_self_credential = 1 means the local login is passed through
    /// unchanged, i.e. the same username reaches the other side; 0 means the mapped remote_name is used
    /// instead.
    /// </summary>
    private static DiscoveredLinkedServer ToDiscoveredLinkedServer(IGrouping<string, LinkedServerRow> group)
    {
        LinkedServerRow first = group.First();
        return new DiscoveredLinkedServer
        {
            Name = group.Key,
            Product = first.Product,
            Provider = first.Provider,
            DataSource = first.DataSource,
            Catalog = first.Catalog,
            RemoteLoginNames = [.. group.Select(r => r.RemoteLoginName).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).Distinct(StringComparer.OrdinalIgnoreCase)],
            UsesLocalLogin = group.Any(r => r.UsesSelfCredential == true),
        };
    }

    private static void AddLinkedServerObjects(
        IEnumerable<IGrouping<string, LinkedServerRow>> linkedServers, ServerConfig server, EffectiveFilters filters, List<ExtractedObject> objects)
    {
        foreach (IGrouping<string, LinkedServerRow> group in linkedServers)
        {
            if (!filters.ObjectNames.IsAllowed(group.Key))
            {
                continue;
            }

            LinkedServerRow first = group.First();
            string ddl = LinkedServerDdlBuilder.Build(first, [.. group]);

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
        List<SchemaRow> schemaRows = [.. await MsSqlCatalogReader.GetSchemasAsync(connection)];
        int firstObject = objects.Count;
        Dictionary<string, bool> allowedSchemas = schemaRows.ToDictionary(
            r => r.SchemaName, r => filters.Schemas.IsAllowed(r.SchemaName), StringComparer.OrdinalIgnoreCase);

        if (filters.ObjectTypes.Contains("Schemas"))
        {
            foreach (SchemaRow schema in schemaRows)
            {
                string schemaName = schema.SchemaName;
                if (!allowedSchemas[schemaName])
                {
                    continue;
                }

                objects.Add(new ExtractedObject
                {
                    Server = server.Name,
                    Database = database,
                    Type = "Schemas",
                    Name = schemaName,
                    Ddl = SchemaDdlBuilder.Build(schemaName, schema.OwnerName),
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

        ILookup<int, ColumnDefinitionRow> columnDefinitions = (filters.ObjectTypes.Contains("Tables") || filters.ObjectTypes.Contains("Types")
            ? await MsSqlCatalogReader.GetColumnDefinitionsAsync(connection) : []).ToLookup(column => column.ObjectId);
        if (filters.ObjectTypes.Contains("Types"))
        {
            foreach (TypeRow type in await MsSqlCatalogReader.GetTypesAsync(connection))
            {
                if (!filters.Schemas.IsAllowed(type.SchemaName) || !filters.ObjectNames.IsAllowed(type.TypeName))
                {
                    continue;
                }

                objects.Add(new ExtractedObject
                {
                    Server = server.Name,
                    Database = database,
                    Schema = type.SchemaName,
                    Type = "Types",
                    Name = type.TypeName,
                    Ddl = TypeDdlBuilder.Build(type, columnDefinitions[type.TableObjectId ?? 0]),
                    Engine = DatabaseEngine.MsSql,
                });
            }
        }

        if (ModuleObjectTypes.Any(filters.ObjectTypes.Contains))
        {
            await ExtractModuleObjectsAsync(connection, server, database, filters, allowedSchemas, extendedProperties, grants, columnList, objects);
        }

        if (filters.ObjectTypes.Contains("Tables"))
        {
            await ExtractTablesAsync(connection, server, database, filters, options, allowedSchemas, extendedProperties, grants, columnList, columnDefinitions, objects, metrics);
        }

        if (filters.ObjectTypes.Contains("Synonyms"))
        {
            await ExtractSynonymsAsync(connection, server, database, filters, allowedSchemas, grants, objects);
        }

        if (filters.ObjectTypes.Contains("Replication"))
        {
            await ExtractReplicationAsync(connection, server, database, filters, objects);
        }

        if (ServiceBrokerReader.ObjectTypes.Any(filters.ObjectTypes.Contains))
        {
            foreach (BrokerRow row in await ServiceBrokerReader.ReadAsync(connection))
            {
                if (!filters.ObjectTypes.Contains(row.Type) || !filters.ObjectNames.IsAllowed(row.Name)
                    || (row.SchemaName is not null && !filters.Schemas.IsAllowed(row.SchemaName)))
                {
                    continue;
                }
                objects.Add(new ExtractedObject
                {
                    Server = server.Name,
                    Database = database,
                    Schema = row.SchemaName,
                    Type = row.Type,
                    Name = row.Name,
                    Ddl = row.Ddl,
                    Engine = DatabaseEngine.MsSql,
                    Grants = row.Type == "Queues" ? grants.GetValueOrDefault($"{row.SchemaName}.{row.Name}") ?? [] : [],
                });
            }
        }

        await AppendConfigurationAsync(connection, server.Name, database, objects, firstObject);
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
                Ddl = ModuleDdlBuilder.Build(row),
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
        ILookup<int, ColumnDefinitionRow> columnDefinitions,
        List<ExtractedObject> objects,
        Dictionary<string, MetricsSnapshot> metrics)
    {
        Dictionary<string, List<string>> foreignKeys = await TryLoadAsync(
            () => LoadTableSectionAsync(MsSqlCatalogReader.GetForeignKeysAsync, connection), server.Name, database, "Foreign key");
        Dictionary<string, List<string>> uniqueConstraints = await TryLoadAsync(
            () => LoadTableSectionAsync(MsSqlCatalogReader.GetUniqueConstraintsAsync, connection), server.Name, database, "Unique constraint");
        Dictionary<string, List<string>> checkConstraints = await TryLoadAsync(
            () => LoadTableSectionAsync(MsSqlCatalogReader.GetCheckConstraintsAsync, connection), server.Name, database, "Check constraint");
        Dictionary<string, List<string>> indexes = await TryLoadAsync(
            () => LoadIndexSectionAsync(connection), server.Name, database, "Index");

        Dictionary<string, MetricsSnapshot> snapshotsByKey = options.CaptureMetrics
            ? await LoadMetricsSnapshotsAsync(connection, server.Name, database)
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
            string columnsDdl = string.Join(",\n", columnDefinitions[table.ObjectId].Select(column => "    " + ColumnDdlBuilder.Build(column)));
            string ddl = TableDdlBuilder.Build(table.SchemaName, table.TableName, columnsDdl, table.PrimaryKeyDdl);

            List<ExtractedSection> sections = [];
            AddSection(sections, "Foreign Keys", foreignKeys, key);
            AddSection(sections, "Unique Constraints", uniqueConstraints, key);
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
                Ddl = $"CREATE SYNONYM {SqlText.Identifier(row.SchemaName)}.{SqlText.Identifier(row.SynonymName)} FOR {row.BaseObjectName};",
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
                    Ddl = ReplicationDdlBuilder.Build(row.PublicationName, row.Description, row.Articles, row.SourceDefinitions),
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

    private async Task AppendConfigurationAsync(SqlConnection connection, string server, string database, List<ExtractedObject> objects, int firstObject)
    {
        (string Title, Func<SqlConnection, Task<IEnumerable<ConfigurationRow>>> Read)[] readers =
        [
            ("Ownership", MsSqlCatalogReader.GetOwnershipAsync),
            ("Permissions", MsSqlCatalogReader.GetPermissionsAsync),
            ("Property Definitions", MsSqlCatalogReader.GetPropertyDefinitionsAsync),
        ];
        foreach ((string title, var read) in readers)
        {
            List<ConfigurationRow> rows = await TryLoadAsync(
                async () => new List<ConfigurationRow>(await read(connection)), server, database, title);
            var index = rows.ToLookup(row => (row.Scope, row.SchemaName, row.ObjectName));
            for (int i = firstObject; i < objects.Count; i++)
            {
                ExtractedObject obj = objects[i];
                string scope = obj.Type == "Schemas" ? "SCHEMA" : obj.Type == "Types" ? "TYPE" : "OBJECT";
                string definition = string.Join('\n', index[(scope, obj.Schema ?? obj.Name, obj.Name)].Select(row => row.Definition));
                if (definition.Length > 0)
                {
                    // Module CREATE statements must finish their batch before ALTER/GRANT/EXEC.
                    objects[i] = obj with { Sections = [.. obj.Sections, new ExtractedSection(title, "GO\n" + definition)] };
                }
            }
        }
    }

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

            list.Add(IndexDdlBuilder.Build(row));
        }

        return index;
    }

    /// <summary>
    /// The three metrics queries degrade independently, because they need different permissions: row
    /// counts and sizes read catalog views any reader can see, while the index and optimizer-statistics
    /// DMVs need VIEW DATABASE STATE (or VIEW SERVER STATE) - a permission a read-only extraction login
    /// often doesn't have. Loading them as one unit meant a denied DMV threw away the volume metrics
    /// too; now a permissions gap costs only the part it actually covers.
    /// </summary>
    private async Task<Dictionary<string, MetricsSnapshot>> LoadMetricsSnapshotsAsync(SqlConnection connection, string serverName, string database)
    {
        DateTimeOffset capturedAt = timeProvider.GetUtcNow();
        Dictionary<string, MetricsSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);

        List<TableVolumeRow> volumeRows = await TryLoadAsync(
            async () => new List<TableVolumeRow>(await MsSqlCatalogReader.GetTableVolumeAsync(connection)),
            serverName, database, "Table volume metrics");

        foreach (TableVolumeRow row in volumeRows)
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

        List<IndexMetricRow> indexRows = await TryLoadAsync(
            async () => new List<IndexMetricRow>(await MsSqlCatalogReader.GetIndexMetricsAsync(connection)),
            serverName, database, "Index metrics (needs VIEW DATABASE STATE)");

        Dictionary<string, List<CatalogIndexMetric>> indexesByTable = new(StringComparer.OrdinalIgnoreCase);
        foreach (IndexMetricRow row in indexRows)
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

        List<OptimizerStatisticRow> statisticRows = await TryLoadAsync(
            async () => new List<OptimizerStatisticRow>(await MsSqlCatalogReader.GetOptimizerStatisticsAsync(connection)),
            serverName, database, "Optimizer statistics (needs VIEW DATABASE STATE)");

        Dictionary<string, List<CatalogStatMetric>> statsByTable = new(StringComparer.OrdinalIgnoreCase);
        foreach (OptimizerStatisticRow row in statisticRows)
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
