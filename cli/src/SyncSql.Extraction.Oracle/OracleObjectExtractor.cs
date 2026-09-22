using System.Data.Common;
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
public sealed class OracleObjectExtractor : IDatabaseObjectExtractor
{
    private readonly ILogger<OracleObjectExtractor> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Func<ServerConfig, DatabaseCredentials, DbConnection> _createConnection;

    public OracleObjectExtractor(ILogger<OracleObjectExtractor> logger, TimeProvider timeProvider)
        : this(logger, timeProvider, OracleConnectionFactory.Create) { }

    internal OracleObjectExtractor(ILogger<OracleObjectExtractor> logger, TimeProvider timeProvider,
        Func<ServerConfig, DatabaseCredentials, DbConnection> createConnection)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _createConnection = createConnection;
    }

    public DatabaseEngine Engine => DatabaseEngine.Oracle;

    public async Task<ExtractionOutcome> ExtractAsync(ServerConfig server, EffectiveFilters filters, ExtractionOptions options, CancellationToken cancellationToken)
    {
        if (options.WorkContext is null)
        {
            ExtractionWorkScheduler scheduler = new(options.MaxParallelism);
            return (await scheduler.RunAsync<ExtractionOutcome>([(Engine, context =>
                ExtractAsync(server, filters, options with { WorkContext = context }, context.CancellationToken))], cancellationToken))[0];
        }
        string serviceName = server.ServiceName
            ?? throw new InvalidOperationException($"Oracle server '{server.Name}' is missing required key 'serviceName'.");
        ExtractionProgressAggregator progress = new(options.Progress);
        OracleExtractionConnections connections = new(() => _createConnection(server, options.Credentials));
        progress.Report(new($"Connecting to {serviceName}"));
        var (owners, links) = await connections.UseAsync(async connection =>
        {
            progress.Report(new($"{serviceName}: listing schemas"));
            List<string> allOwners = await OracleCommandRunner.QueryAsync(connection, OracleQueries.Schemas,
                r => r.GetStringOrEmpty("OWNER"), cancellationToken);
            List<(string Owner, string Name)> databaseLinks = [];
            if (filters.ObjectTypes.Contains("DatabaseLinks"))
            {
                progress.Report(new($"{serviceName}: database links"));
                databaseLinks = await OracleCommandRunner.QueryDictionaryAsync(connection, OracleQueries.AllDatabaseLinks,
                    OracleQueries.DatabaseLinks, r => (r.GetStringOrEmpty("OWNER"), r.GetStringOrEmpty("DB_LINK")), cancellationToken);
            }
            return (allOwners.Where(filters.Schemas.IsAllowed).ToArray(), databaseLinks);
        }, cancellationToken);

        List<Func<ExtractionWorkContext, Task<ExtractionOutcome>>> jobs = [];
        if (filters.ObjectTypes.Contains("Schemas") || OracleTypeMaps.ObjectTypeMap.Any(type => filters.ObjectTypes.Contains(type.ConfigType)))
        {
            jobs.AddRange(owners.Select(owner => (Func<ExtractionWorkContext, Task<ExtractionOutcome>>)(context =>
                ExtractOwnerAsync(connections, server, serviceName, owner, filters, options, progress, context))));
        }
        foreach ((string owner, string name) in links)
        {
            if (filters.Schemas.IsAllowed(owner) && filters.ObjectNames.IsAllowed(name))
            {
                ObjectWork work = new(owner, "DatabaseLinks", "DB_LINK", name, [], [], null);
                jobs.Add(context => ExtractObjectAsync(connections, server, serviceName, work, progress.CreateChild(), context.CancellationToken));
            }
        }
        ExtractionOutcome result = Merge(await options.WorkContext.RunChildrenAsync(jobs));
        _logger.LogInformation("[{Server}] Wrote {Count} object(s)", server.Name, result.Objects.Count);
        return result;
    }

    private async Task<ExtractionOutcome> ExtractOwnerAsync(
        OracleExtractionConnections connections, ServerConfig server, string serviceName, string owner,
        EffectiveFilters filters, ExtractionOptions options, ExtractionProgressAggregator progress, ExtractionWorkContext context)
    {
        CancellationToken token = context.CancellationToken;
        List<ObjectWork> objects = await connections.UseAsync(async connection =>
        {
            List<ObjectWork> work = [];
            if (filters.ObjectTypes.Contains("Schemas")) { work.Add(new(owner, "Schemas", "USER", owner, [], [], null)); }
            Dictionary<string, List<GrantEntry>>? grants = null;
            Dictionary<string, List<ExtractedColumn>>? columns = null;
            Dictionary<string, MetricsSnapshot>? metrics = null;
            foreach ((string configType, string oracleType) in OracleTypeMaps.ObjectTypeMap)
            {
                if (!filters.ObjectTypes.Contains(configType)) { continue; }
                progress.Report(new($"{serviceName}/{owner}: {configType}"));
                grants ??= await TryLoadAsync(() => LoadGrantsAsync(connection, owner, token), server.Name, owner, "ALL_TAB_PRIVS/ALL_COL_PRIVS");
                if (oracleType is "TABLE" or "VIEW" && columns is null)
                {
                    columns = await TryLoadAsync(() => LoadColumnListAsync(connection, owner, token), server.Name, owner, "ALL_TAB_COLUMNS");
                }
                if (options.CaptureMetrics && oracleType == "TABLE" && metrics is null)
                {
                    metrics = await LoadMetricsSnapshotsAsync(connection, owner, server.Name, token);
                }
                List<string> names = await OracleCommandRunner.QueryAsync(connection, OracleQueries.ObjectList,
                    r => r.GetStringOrEmpty("ObjectName"), token, ("owner", owner), ("objType", oracleType));
                foreach (string name in names.Where(filters.ObjectNames.IsAllowed))
                {
                    string key = $"{owner}.{name}";
                    work.Add(new(owner, configType, OracleTypeMaps.ToDdlType(oracleType), name,
                        oracleType is "TABLE" or "VIEW" ? columns!.GetValueOrDefault(key) ?? [] : [],
                        grants.GetValueOrDefault(key) ?? [], oracleType == "TABLE" ? metrics?.GetValueOrDefault(key) : null));
                }
            }
            return work;
        }, token);

        // Metadata is read once per schema. Release its session and slot before scheduling DDL,
        // allowing even one schema (including packages and package bodies) to use the whole budget.
        ExtractionProgressAggregator ownerProgress = new(progress.CreateChild());
        int completed = 0;
        object completionLock = new();
        ExtractionOutcome[] results = await context.RunChildrenAsync(objects.Select(work =>
            (Func<ExtractionWorkContext, Task<ExtractionOutcome>>)(async child =>
            {
                ExtractionOutcome result = await ExtractObjectAsync(connections, server, serviceName, work,
                    ownerProgress.CreateChild(), child.CancellationToken);
                lock (completionLock)
                {
                    ownerProgress.Report(new($"{owner}: objects", Completed: ++completed, Total: objects.Count));
                }
                return result;
            })));
        return Merge(results);
    }

    private sealed record ObjectWork(string Owner, string ConfigType, string DdlType, string Name,
        IReadOnlyList<ExtractedColumn> Columns, IReadOnlyList<GrantEntry> Grants, MetricsSnapshot? Metrics);

    private async Task<ExtractionOutcome> ExtractObjectAsync(OracleExtractionConnections connections, ServerConfig server,
        string serviceName, ObjectWork work, IProgress<ExtractionProgress> progress, CancellationToken token)
    {
        progress.Report(new(work.ConfigType == "Schemas" ? $"{serviceName}/{work.Owner}: schema definitions"
            : $"{work.Owner}.{work.Name}: {work.ConfigType}"));
        return await connections.UseAsync(async connection =>
        {
            string? ddl;
            try
            {
                ddl = await OracleCommandRunner.ExecuteScalarStringAsync(connection, OracleQueries.GetDdl, token,
                    ("objType", work.DdlType), ("objName", work.Name), ("owner", work.Owner));
            }
            catch (OracleException ex) when (!token.IsCancellationRequested)
            {
                if (work.ConfigType == "Schemas") { ddl = null; }
                else
                {
                    _logger.LogWarning("[{Server}/{ServiceName}] Failed to extract DDL for {Owner}.{ObjectName} ({ConfigType}): {Message}",
                        server.Name, serviceName, work.Owner, work.Name, work.ConfigType, ex.Message);
                    return Merge([]);
                }
            }
            ExtractedObject extracted = new()
            {
                Server = server.Name,
                Database = serviceName,
                Schema = work.ConfigType == "Schemas" ? null : work.Owner,
                Type = work.ConfigType,
                Name = work.Name,
                Ddl = ddl ?? (work.ConfigType == "Schemas" ? $"-- Oracle schema/user: {work.Owner}" : string.Empty),
                Engine = DatabaseEngine.Oracle,
                Columns = work.Columns,
                Grants = work.Grants,
            };
            Dictionary<string, MetricsSnapshot> metrics = [];
            if (work.Metrics is { } snapshot)
            {
                metrics[ExtractedObjectFile.ObjectId(server.Name, serviceName, work.Owner, work.ConfigType, work.Name)] = snapshot;
            }
            progress.Report(new($"{work.Owner}: {work.ConfigType}", 1));
            return new ExtractionOutcome { Objects = [extracted], MetricsSnapshots = metrics };
        }, token);
    }

    private static ExtractionOutcome Merge(IEnumerable<ExtractionOutcome> results)
    {
        List<ExtractedObject> objects = [];
        Dictionary<string, MetricsSnapshot> metrics = [];
        foreach (ExtractionOutcome result in results)
        {
            objects.AddRange(result.Objects);
            foreach ((string id, MetricsSnapshot snapshot) in result.MetricsSnapshots) { metrics[id] = snapshot; }
        }
        return new ExtractionOutcome { Objects = objects, MetricsSnapshots = metrics };
    }
    private static async Task<Dictionary<string, List<GrantEntry>>> LoadGrantsAsync(DbConnection connection, string owner, CancellationToken cancellationToken)
    {
        Dictionary<string, List<GrantEntry>> index = new(StringComparer.OrdinalIgnoreCase);

        List<(string Grantee, string TableName, string Privilege)> objectGrants = await OracleCommandRunner.QueryDictionaryAsync(
            connection, OracleQueries.AllObjectGrants, OracleQueries.ObjectGrants,
            r => (r.GetStringOrEmpty("GRANTEE"), r.GetStringOrEmpty("TABLE_NAME"), r.GetStringOrEmpty("PRIVILEGE")),
            cancellationToken, ("owner", owner));
        foreach ((string grantee, string tableName, string privilege) in objectGrants)
        {
            Add(index, $"{owner}.{tableName}", new GrantEntry(privilege, GrantState.Grant, grantee, null, null));
        }

        List<(string Grantee, string TableName, string ColumnName, string Privilege)> columnGrants = await OracleCommandRunner.QueryDictionaryAsync(
            connection, OracleQueries.AllColumnGrants, OracleQueries.ColumnGrants,
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

    private static async Task<Dictionary<string, List<ExtractedColumn>>> LoadColumnListAsync(DbConnection connection, string owner, CancellationToken cancellationToken)
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

    private async Task<Dictionary<string, MetricsSnapshot>> LoadMetricsSnapshotsAsync(DbConnection connection, string owner, string serverName, CancellationToken cancellationToken)
    {
        DateTimeOffset capturedAt = _timeProvider.GetUtcNow();
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
            _logger.LogWarning("[{Server}/{Owner}] ALL_TAB_STATISTICS extraction failed (continuing without it): {Message}", serverName, owner, ex.Message);
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
            _logger.LogWarning("[{Server}/{Owner}] ALL_TAB_MODIFICATIONS extraction failed (continuing without it): {Message}", serverName, owner, ex.Message);
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
            _logger.LogWarning("[{Server}/{Owner}] ALL_IND_STATISTICS extraction failed (continuing without it): {Message}", serverName, owner, ex.Message);
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
            _logger.LogWarning("[{Server}/{Owner}] {Section} extraction failed (continuing without it): {Message}", serverName, owner, sectionName, ex.Message);
            return new T();
        }
    }
}
