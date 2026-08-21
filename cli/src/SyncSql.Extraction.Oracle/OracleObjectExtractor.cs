using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Extraction.Oracle;

/// <summary>
/// Extracts every allowed object from one Oracle server via DBMS_METADATA.GET_DDL: schemas/users,
/// tables, views, procedures, functions, packages/package bodies, triggers, synonyms, database links,
/// object/column grants (ALL_TAB_PRIVS/ALL_COL_PRIVS - Oracle has no DENY concept, so state is always
/// GRANT), and a reduced-scope volatile metrics snapshot for tables. A direct port of
/// SyncSql.Oracle.psm1's Export-SyncSqlOracleServer. Oracle has no "database" concept equivalent to
/// MSSQL's, so the configured service name is used as the DatabaseName path segment.
/// </summary>
public sealed class OracleObjectExtractor(ILogger<OracleObjectExtractor> logger) : IDatabaseObjectExtractor
{
    public DatabaseEngine Engine => DatabaseEngine.Oracle;

    public async Task<ExtractionOutcome> ExtractAsync(ServerConfig server, EffectiveFilters filters, ExtractionOptions options, CancellationToken cancellationToken)
    {
        string serviceName = server.ServiceName
            ?? throw new InvalidOperationException($"Oracle server '{server.Name}' is missing required key 'serviceName'.");

        await using OracleConnection connection = await OracleConnectionFactory.OpenAsync(server, options.Credentials, cancellationToken);

        List<ExtractedObject> objects = [];
        Dictionary<string, MetricsSnapshot> metrics = [];

        List<string> allOwners = await OracleCommandRunner.QueryAsync(connection, OracleQueries.Schemas, r => r.GetStringOrEmpty("OWNER"), cancellationToken);
        List<string> allowedOwners = [.. allOwners.Where(filters.Schemas.IsAllowed)];

        if (filters.ObjectTypes.Contains("Schemas"))
        {
            await ExtractSchemasAsync(connection, server, serviceName, allowedOwners, objects, cancellationToken);
        }

        // One query pass per owner regardless of how many object types are being extracted for that
        // owner - each degrades to an empty index on failure, same "optional step never blocks the
        // rest" posture as every other best-effort extraction step.
        Dictionary<string, Dictionary<string, List<GrantEntry>>> grantsByOwner = [];
        Dictionary<string, Dictionary<string, List<ExtractedColumn>>> columnListByOwner = [];
        Dictionary<string, Dictionary<string, MetricsSnapshot>> metricsByOwner = [];

        foreach ((string configType, string oracleType) in OracleTypeMaps.ObjectTypeMap)
        {
            if (!filters.ObjectTypes.Contains(configType))
            {
                continue;
            }

            string ddlType = OracleTypeMaps.ToDdlType(oracleType);

            foreach (string owner in allowedOwners)
            {
                if (!grantsByOwner.ContainsKey(owner))
                {
                    grantsByOwner[owner] = await TryLoadAsync(() => LoadGrantsAsync(connection, owner, cancellationToken), server.Name, owner, "ALL_TAB_PRIVS/ALL_COL_PRIVS");
                }
                if (oracleType is "TABLE" or "VIEW" && !columnListByOwner.ContainsKey(owner))
                {
                    columnListByOwner[owner] = await TryLoadAsync(() => LoadColumnListAsync(connection, owner, cancellationToken), server.Name, owner, "ALL_TAB_COLUMNS");
                }
                if (options.CaptureMetrics && oracleType == "TABLE" && !metricsByOwner.ContainsKey(owner))
                {
                    metricsByOwner[owner] = await LoadMetricsSnapshotsAsync(connection, owner, server.Name, cancellationToken);
                }

                List<string> objectNames = await OracleCommandRunner.QueryAsync(
                    connection, OracleQueries.ObjectList, r => r.GetStringOrEmpty("ObjectName"), cancellationToken,
                    ("owner", owner), ("objType", oracleType));

                foreach (string objectName in objectNames)
                {
                    if (!filters.ObjectNames.IsAllowed(objectName))
                    {
                        continue;
                    }

                    string? ddl;
                    try
                    {
                        ddl = await OracleCommandRunner.ExecuteScalarStringAsync(
                            connection, OracleQueries.GetDdl, cancellationToken,
                            ("objType", ddlType), ("objName", objectName), ("owner", owner));
                    }
                    catch (OracleException ex)
                    {
                        logger.LogWarning("[{Server}/{ServiceName}] Failed to extract DDL for {Owner}.{ObjectName} ({ConfigType}): {Message}",
                            server.Name, serviceName, owner, objectName, configType, ex.Message);
                        continue;
                    }

                    string key = $"{owner}.{objectName}";
                    IReadOnlyList<ExtractedColumn> columns = oracleType is "TABLE" or "VIEW"
                        ? columnListByOwner.GetValueOrDefault(owner)?.GetValueOrDefault(key) ?? []
                        : [];
                    IReadOnlyList<GrantEntry> objectGrants = grantsByOwner.GetValueOrDefault(owner)?.GetValueOrDefault(key) ?? [];

                    objects.Add(new ExtractedObject
                    {
                        Server = server.Name,
                        Database = serviceName,
                        Schema = owner,
                        Type = configType,
                        Name = objectName,
                        Ddl = ddl ?? string.Empty,
                        Engine = DatabaseEngine.Oracle,
                        Columns = columns,
                        Grants = objectGrants,
                    });

                    if (options.CaptureMetrics && oracleType == "TABLE" &&
                        metricsByOwner.TryGetValue(owner, out Dictionary<string, MetricsSnapshot>? ownerMetrics) &&
                        ownerMetrics.TryGetValue(key, out MetricsSnapshot? snapshot))
                    {
                        string id = ExtractedObjectFile.ObjectId(server.Name, serviceName, owner, configType, objectName);
                        metrics[id] = snapshot;
                    }
                }
            }
        }

        if (filters.ObjectTypes.Contains("DatabaseLinks"))
        {
            await ExtractDatabaseLinksAsync(connection, server, serviceName, filters, objects, cancellationToken);
        }

        logger.LogInformation("[{Server}] Wrote {Count} object(s)", server.Name, objects.Count);
        return new ExtractionOutcome { Objects = objects, MetricsSnapshots = metrics };
    }

    private static async Task ExtractSchemasAsync(
        OracleConnection connection, ServerConfig server, string serviceName, IReadOnlyList<string> allowedOwners,
        List<ExtractedObject> objects, CancellationToken cancellationToken)
    {
        foreach (string owner in allowedOwners)
        {
            string definition;
            try
            {
                definition = await OracleCommandRunner.ExecuteScalarStringAsync(
                    connection, OracleQueries.GetDdl, cancellationToken, ("objType", "USER"), ("objName", owner), ("owner", owner))
                    ?? $"-- Oracle schema/user: {owner}";
            }
            catch (OracleException)
            {
                // Insufficient privileges to extract CREATE USER DDL - fall back to a bare comment
                // rather than skipping the schema entirely (matches Export-SyncSqlOracleServer).
                definition = $"-- Oracle schema/user: {owner}";
            }

            objects.Add(new ExtractedObject
            {
                Server = server.Name,
                Database = serviceName,
                Type = "Schemas",
                Name = owner,
                Ddl = definition,
                Engine = DatabaseEngine.Oracle,
            });
        }
    }

    private static async Task ExtractDatabaseLinksAsync(
        OracleConnection connection, ServerConfig server, string serviceName, EffectiveFilters filters,
        List<ExtractedObject> objects, CancellationToken cancellationToken)
    {
        List<(string Owner, string DbLink)> links = await OracleCommandRunner.QueryAsync(
            connection, OracleQueries.DatabaseLinks, r => (r.GetStringOrEmpty("OWNER"), r.GetStringOrEmpty("DB_LINK")), cancellationToken);

        foreach ((string owner, string dbLink) in links)
        {
            if (!filters.Schemas.IsAllowed(owner) || !filters.ObjectNames.IsAllowed(dbLink))
            {
                continue;
            }

            string? ddl;
            try
            {
                ddl = await OracleCommandRunner.ExecuteScalarStringAsync(
                    connection, OracleQueries.GetDdl, cancellationToken, ("objType", "DB_LINK"), ("objName", dbLink), ("owner", owner));
            }
            catch (OracleException)
            {
                continue;
            }

            objects.Add(new ExtractedObject
            {
                Server = server.Name,
                Database = serviceName,
                Schema = owner,
                Type = "DatabaseLinks",
                Name = dbLink,
                Ddl = ddl ?? string.Empty,
                Engine = DatabaseEngine.Oracle,
            });
        }
    }

    private static async Task<Dictionary<string, List<GrantEntry>>> LoadGrantsAsync(OracleConnection connection, string owner, CancellationToken cancellationToken)
    {
        Dictionary<string, List<GrantEntry>> index = new(StringComparer.OrdinalIgnoreCase);

        List<(string Grantee, string TableName, string Privilege)> objectGrants = await OracleCommandRunner.QueryAsync(
            connection, OracleQueries.ObjectGrants,
            r => (r.GetStringOrEmpty("GRANTEE"), r.GetStringOrEmpty("TABLE_NAME"), r.GetStringOrEmpty("PRIVILEGE")),
            cancellationToken, ("owner", owner));
        foreach ((string grantee, string tableName, string privilege) in objectGrants)
        {
            Add(index, $"{owner}.{tableName}", new GrantEntry(privilege, GrantState.Grant, grantee, null, null));
        }

        List<(string Grantee, string TableName, string ColumnName, string Privilege)> columnGrants = await OracleCommandRunner.QueryAsync(
            connection, OracleQueries.ColumnGrants,
            r => (r.GetStringOrEmpty("GRANTEE"), r.GetStringOrEmpty("TABLE_NAME"), r.GetStringOrEmpty("COLUMN_NAME"), r.GetStringOrEmpty("PRIVILEGE")),
            cancellationToken, ("owner", owner));
        foreach ((string grantee, string tableName, string columnName, string privilege) in columnGrants)
        {
            Add(index, $"{owner}.{tableName}", new GrantEntry(privilege, GrantState.Grant, grantee, null, columnName));
        }

        return index;

        static void Add(Dictionary<string, List<GrantEntry>> index, string key, GrantEntry entry)
        {
            if (!index.TryGetValue(key, out List<GrantEntry>? list))
            {
                list = [];
                index[key] = list;
            }
            list.Add(entry);
        }
    }

    private static async Task<Dictionary<string, List<ExtractedColumn>>> LoadColumnListAsync(OracleConnection connection, string owner, CancellationToken cancellationToken)
    {
        Dictionary<string, List<ExtractedColumn>> index = new(StringComparer.OrdinalIgnoreCase);
        List<(string TableName, string ColumnName, string DataType)> rows = await OracleCommandRunner.QueryAsync(
            connection, OracleQueries.ColumnList,
            r => (r.GetStringOrEmpty("TABLE_NAME"), r.GetStringOrEmpty("COLUMN_NAME"), r.GetStringOrEmpty("DATA_TYPE")),
            cancellationToken, ("owner", owner));

        foreach ((string tableName, string columnName, string dataType) in rows)
        {
            string key = $"{owner}.{tableName}";
            if (!index.TryGetValue(key, out List<ExtractedColumn>? list))
            {
                list = [];
                index[key] = list;
            }
            list.Add(new ExtractedColumn(columnName, dataType, null));
        }

        return index;
    }

    private async Task<Dictionary<string, MetricsSnapshot>> LoadMetricsSnapshotsAsync(OracleConnection connection, string owner, string serverName, CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        Dictionary<string, MetricsSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            var rows = await OracleCommandRunner.QueryAsync(connection, OracleQueries.TableStatistics, r => new
            {
                TableName = r.GetStringOrEmpty("TABLE_NAME"),
                NumRows = r.GetNullableInt64("NUM_ROWS"),
                Blocks = r.GetNullableInt64("BLOCKS"),
                SampleSize = r.GetNullableInt64("SAMPLE_SIZE"),
                LastAnalyzed = r.GetNullableDateTime("LAST_ANALYZED"),
            }, cancellationToken, ("owner", owner));

            foreach (var row in rows)
            {
                string key = $"{owner}.{row.TableName}";
                DateTimeOffset? lastAnalyzed = row.LastAnalyzed is { } d ? new DateTimeOffset(d, TimeSpan.Zero) : null;
                snapshots[key] = new MetricsSnapshot
                {
                    CapturedAt = capturedAt,
                    RowCount = row.NumRows,
                    ReservedKB = row.Blocks is { } b ? b * 8 : null,
                    Statistics =
                    [
                        new CatalogStatMetric
                        {
                            Name = row.TableName,
                            Rows = row.NumRows,
                            RowsSampled = row.SampleSize,
                            LastUpdated = lastAnalyzed,
                        },
                    ],
                };
            }
        }
        catch (OracleException ex)
        {
            logger.LogWarning("[{Server}/{Owner}] ALL_TAB_STATISTICS extraction failed (continuing without it): {Message}", serverName, owner, ex.Message);
        }

        try
        {
            var rows = await OracleCommandRunner.QueryAsync(connection, OracleQueries.TableModifications, r => new
            {
                TableName = r.GetStringOrEmpty("TABLE_NAME"),
                Inserts = r.GetNullableInt64("INSERTS") ?? 0,
                Updates = r.GetNullableInt64("UPDATES") ?? 0,
                Deletes = r.GetNullableInt64("DELETES") ?? 0,
            }, cancellationToken, ("owner", owner));

            foreach (var row in rows)
            {
                string key = $"{owner}.{row.TableName}";
                if (!snapshots.TryGetValue(key, out MetricsSnapshot? snapshot) || snapshot.Statistics.Count == 0)
                {
                    continue;
                }

                CatalogStatMetric stat = snapshot.Statistics[0] with { ModificationCounter = row.Inserts + row.Updates + row.Deletes };
                snapshots[key] = snapshot with { Statistics = [stat] };
            }
        }
        catch (OracleException ex)
        {
            logger.LogWarning("[{Server}/{Owner}] ALL_TAB_MODIFICATIONS extraction failed (continuing without it): {Message}", serverName, owner, ex.Message);
        }

        try
        {
            var rows = await OracleCommandRunner.QueryAsync(connection, OracleQueries.IndexStatistics, r => new
            {
                IndexName = r.GetStringOrEmpty("INDEX_NAME"),
                TableName = r.GetStringOrEmpty("TABLE_NAME"),
                NumRows = r.GetNullableInt64("NUM_ROWS"),
                DistinctKeys = r.GetNullableInt64("DISTINCT_KEYS"),
                LeafBlocks = r.GetNullableInt64("LEAF_BLOCKS"),
                LastAnalyzed = r.GetNullableDateTime("LAST_ANALYZED"),
            }, cancellationToken, ("owner", owner));

            Dictionary<string, List<CatalogIndexMetric>> indexesByTable = new(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                string key = $"{owner}.{row.TableName}";
                if (!indexesByTable.TryGetValue(key, out List<CatalogIndexMetric>? list))
                {
                    list = [];
                    indexesByTable[key] = list;
                }

                list.Add(new CatalogIndexMetric
                {
                    Name = row.IndexName,
                    RowCount = row.NumRows,
                    DistinctKeys = row.DistinctKeys,
                    LeafBlocks = row.LeafBlocks,
                    LastAnalyzed = row.LastAnalyzed is { } d ? new DateTimeOffset(d, TimeSpan.Zero) : null,
                });
            }

            foreach ((string key, List<CatalogIndexMetric> indexes) in indexesByTable)
            {
                if (snapshots.TryGetValue(key, out MetricsSnapshot? snapshot))
                {
                    snapshots[key] = snapshot with { Indexes = indexes };
                }
            }
        }
        catch (OracleException ex)
        {
            logger.LogWarning("[{Server}/{Owner}] ALL_IND_STATISTICS extraction failed (continuing without it): {Message}", serverName, owner, ex.Message);
        }

        return snapshots;
    }

    private async Task<T> TryLoadAsync<T>(Func<Task<T>> load, string serverName, string owner, string sectionName) where T : new()
    {
        try
        {
            return await load();
        }
        catch (OracleException ex)
        {
            logger.LogWarning("[{Server}/{Owner}] {Section} extraction failed (continuing without it): {Message}", serverName, owner, sectionName, ex.Message);
            return new T();
        }
    }
}
