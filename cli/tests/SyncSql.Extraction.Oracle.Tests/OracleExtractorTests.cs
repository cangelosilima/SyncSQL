using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Oracle.ManagedDataAccess.Client;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Extraction.Oracle.Tests;

public sealed class OracleExtractorTests
{
    private static readonly ServerConfig Server = new() { Name = "ORA", Host = "host", ServiceName = "APP", Type = DatabaseEngine.Oracle, CredentialsVariablePrefix = "TEST" };
    private static readonly DatabaseCredentials Credentials = new("user", "password");

    [Fact]
    public void Connection_ValidatesServiceAndQuotesCredentials()
    {
        using var connection = OracleConnectionFactory.Create(Server, new DatabaseCredentials("user", "pass;word"));
        var builder = new OracleConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal("host:1521/APP", builder.DataSource);
        Assert.Equal("pass;word", builder.Password);
        Assert.Throws<InvalidOperationException>(() => OracleConnectionFactory.Create(Server with { ServiceName = null }, Credentials));
        using var command = (OracleCommand)OracleCommandRunner.CreateCommand(connection, "SELECT :b, :a FROM DUAL", [("a", 1), ("b", "two")]);
        Assert.True(command.BindByName);
        Assert.Equal("two", command.Parameters["b"].Value);
    }

    [Theory]
    [InlineData(942)]
    [InlineData(1031)]
    [InlineData(41900)]
    public async Task DictionaryQuery_FallsBackOnlyForAccessErrors(int number)
    {
        using FakeOracleDatabase db = new()
        {
            Execute = (sql, _) => sql == "all" ? throw FakeOracleDatabase.Error(number) : FakeOracleDatabase.Rows(new { NAME = "visible" }),
        };
        var rows = await OracleCommandRunner.QueryDictionaryAsync(db, "all", "visible", r => r.GetStringOrEmpty("NAME"), CancellationToken.None);
        Assert.Equal(["visible"], rows);
        Assert.Equal(["all", "visible"], db.Queries);
        db.Execute = (_, _) => throw FakeOracleDatabase.Error(3113);
        await Assert.ThrowsAsync<OracleException>(() => OracleCommandRunner.QueryDictionaryAsync(db, "all", "visible", r => r.GetStringOrEmpty("NAME"), CancellationToken.None));
    }

    [Fact]
    public void Reader_PreservesNullsAndConvertsNumbers()
    {
        using var table = FakeOracleDatabase.Rows(new { S = (string?)null, N = (decimal?)null, D = (DateTime?)null }, new { S = (string?)"value", N = (decimal?)42, D = (DateTime?)DateTime.UnixEpoch });
        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        Assert.Equal("", reader.GetStringOrEmpty("S"));
        Assert.Null(reader.GetNullableString("S"));
        Assert.Null(reader.GetNullableInt64("N"));
        Assert.Null(reader.GetNullableDateTime("D"));
        Assert.True(reader.Read());
        Assert.Equal("value", reader.GetNullableString("S"));
        Assert.Equal(42, reader.GetNullableInt64("N"));
        Assert.Equal(DateTime.UnixEpoch, reader.GetNullableDateTime("D"));
    }

    private static object? Respond(string sql, IReadOnlyDictionary<string, object> parameters)
    {
        if (sql.StartsWith("BEGIN", StringComparison.Ordinal)) { return null; }
        if (sql == OracleQueries.Schemas) { return FakeOracleDatabase.Rows(new { OWNER = "APP" }, new { OWNER = "PRIVATE" }); }
        if (sql == OracleQueries.ObjectList) { return FakeOracleDatabase.Rows(new { ObjectName = "orders" }, new { ObjectName = "empty" }, new { ObjectName = "skip" }); }
        if (sql == OracleQueries.GetDdl) { return parameters["objName"].Equals("empty") ? null : "CREATE " + parameters["objType"] + " " + parameters["objName"]; }
        if (sql == OracleQueries.AllObjectGrants) { return FakeOracleDatabase.Rows(new { GRANTEE = "reader", TABLE_NAME = "orders", PRIVILEGE = "SELECT" }); }
        if (sql == OracleQueries.AllColumnGrants) { return FakeOracleDatabase.Rows(new { GRANTEE = "reader", TABLE_NAME = "orders", COLUMN_NAME = "id", PRIVILEGE = "UPDATE" }); }
        if (sql == OracleQueries.ColumnList) { return FakeOracleDatabase.Rows(new { TABLE_NAME = "orders", COLUMN_NAME = "id", DATA_TYPE = "NUMBER" }, new { TABLE_NAME = "orders", COLUMN_NAME = "code", DATA_TYPE = "VARCHAR2" }); }
        if (sql == OracleQueries.TableStatistics)
        {
            return FakeOracleDatabase.Rows(
            new { TABLE_NAME = "orders", NUM_ROWS = (long?)42, BLOCKS = (long?)2, SAMPLE_SIZE = (long?)40, LAST_ANALYZED = (DateTime?)DateTime.UnixEpoch },
            new { TABLE_NAME = "empty", NUM_ROWS = (long?)null, BLOCKS = (long?)null, SAMPLE_SIZE = (long?)null, LAST_ANALYZED = (DateTime?)null });
        }
        if (sql == OracleQueries.TableModifications)
        {
            return FakeOracleDatabase.Rows(
            new { TABLE_NAME = "orders", INSERTS = (long?)1, UPDATES = (long?)2, DELETES = (long?)3 },
            new { TABLE_NAME = "missing", INSERTS = (long?)null, UPDATES = (long?)null, DELETES = (long?)null });
        }
        if (sql == OracleQueries.IndexStatistics)
        {
            return FakeOracleDatabase.Rows(
            new { TABLE_NAME = "orders", INDEX_NAME = "ix1", NUM_ROWS = (long?)42, DISTINCT_KEYS = (long?)40, LEAF_BLOCKS = (long?)2, LAST_ANALYZED = (DateTime?)DateTime.UnixEpoch },
            new { TABLE_NAME = "orders", INDEX_NAME = "ix2", NUM_ROWS = (long?)null, DISTINCT_KEYS = (long?)null, LEAF_BLOCKS = (long?)null, LAST_ANALYZED = (DateTime?)null },
            new { TABLE_NAME = "missing", INDEX_NAME = "ix", NUM_ROWS = (long?)null, DISTINCT_KEYS = (long?)null, LEAF_BLOCKS = (long?)null, LAST_ANALYZED = (DateTime?)null });
        }
        if (sql == OracleQueries.AllDatabaseLinks) { return FakeOracleDatabase.Rows(new { OWNER = "APP", DB_LINK = "remote" }, new { OWNER = "APP", DB_LINK = "empty" }, new { OWNER = "APP", DB_LINK = "skip" }, new { OWNER = "PRIVATE", DB_LINK = "private" }); }
        throw new InvalidOperationException("Unexpected query: " + sql);
    }

    private static Task<ExtractionOutcome> Extract(FakeOracleDatabase db, bool metrics = true, string[]? types = null, ServerConfig? config = null, IProgress<ExtractionProgress>? progress = null)
    {
        var extractor = new OracleObjectExtractor(NullLogger<OracleObjectExtractor>.Instance, TimeProvider.System, (_, _) => db);
        var server = config ?? Server with
        {
            ObjectTypes = types ?? ["Schemas", "Tables", "Views", "StoredProcedures", "Functions", "Packages", "PackageBodies", "Triggers", "Synonyms", "DatabaseLinks"],
            Schemas = new NameFilter { Exclude = ["^PRIVATE$"] },
            ObjectNames = new NameFilter { Exclude = ["^skip$"] }
        };
        return extractor.ExtractAsync(server, EffectiveFilters.Resolve(null, server), new ExtractionOptions { Credentials = Credentials, CaptureMetrics = metrics, Progress = progress }, CancellationToken.None);
    }

    private sealed class ProgressRecorder : IProgress<ExtractionProgress>
    {
        public List<ExtractionProgress> Updates { get; } = [];
        public void Report(ExtractionProgress value) => Updates.Add(value);
    }

    [Fact]
    public async Task Progress_ReportsSchemaDefinitionsAndDatabaseLinks()
    {
        using FakeOracleDatabase db = new() { Execute = Respond };
        ProgressRecorder progress = new();
        ExtractionOutcome result = await Extract(db, progress: progress);
        Assert.Contains(progress.Updates, p => p.Activity == "APP: schema definitions");
        Assert.Contains(progress.Updates, p => p.Activity == "APP: database links");
        Assert.Contains(result.Objects, o => o.Type == "DatabaseLinks");
        Assert.Contains(result.Objects, o => o.Type == "Schemas");
    }

    [Fact]
    public async Task Progress_ReportsOnlySelectedObjectsWithAccurateCounts()
    {
        using FakeOracleDatabase db = new() { Execute = Respond };
        ProgressRecorder progress = new();
        ExtractionOutcome result = await Extract(db, types: ["Tables"], progress: progress);
        Assert.Contains(progress.Updates, p => p.Activity == "APP.orders: Tables");
        Assert.Equal(new ExtractionProgress("APP: Tables", result.Objects.Count, 2, 2), progress.Updates[^1]);
        Assert.DoesNotContain(progress.Updates, p => p.Activity.Contains("skip", StringComparison.Ordinal) || p.Activity.Contains("PRIVATE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Extract_MapsDdlGrantsColumnsAndMetrics(bool metrics)
    {
        using FakeOracleDatabase db = new() { Execute = Respond };
        var result = await Extract(db, metrics);
        Assert.DoesNotContain(result.Objects, o => o.Name == "skip" || o.Schema == "PRIVATE");
        var table = Assert.Single(result.Objects, o => o.Type == "Tables" && o.Name == "orders");
        Assert.Equal("CREATE TABLE orders", table.Ddl);
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("id", table.Grants[1].Column);
        Assert.Equal("", Assert.Single(result.Objects, o => o.Type == "Tables" && o.Name == "empty").Ddl);
        Assert.Equal(1, db.Queries.Count(q => q == OracleQueries.AllObjectGrants));
        Assert.Equal(1, db.Queries.Count(q => q == OracleQueries.ColumnList));
        if (metrics)
        {
            var snapshot = result.MetricsSnapshots["ORA/APP/Tables/APP/orders"];
            Assert.Equal(42, snapshot.RowCount);
            Assert.Equal(16, snapshot.ReservedKB);
            Assert.Equal(6, Assert.Single(snapshot.Statistics).ModificationCounter);
            Assert.Equal(2, snapshot.Indexes.Count);
            Assert.Null(snapshot.Indexes[1].LastAnalyzed);
        }
        else { Assert.Empty(result.MetricsSnapshots); }
        Assert.True(db.WasDisposed);
    }

    [Fact]
    public async Task Extract_OptionalFailuresDoNotDiscardObjects()
    {
        using FakeOracleDatabase db = new()
        {
            Execute = (sql, p) => sql == OracleQueries.AllObjectGrants || sql == OracleQueries.ColumnList || sql == OracleQueries.TableStatistics || sql == OracleQueries.TableModifications || sql == OracleQueries.IndexStatistics
                ? throw FakeOracleDatabase.Error(3113) : Respond(sql, p),
        };
        var result = await Extract(db);
        Assert.NotEmpty(result.Objects);
        Assert.All(result.Objects, o => { Assert.Empty(o.Grants); Assert.Empty(o.Columns); });
        Assert.Empty(result.MetricsSnapshots);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Extract_SchemaDdlFailureFallsBackAndObjectDdlFailureSkips(bool fail)
    {
        using FakeOracleDatabase db = new()
        {
            Execute = (sql, p) => sql == OracleQueries.GetDdl ? fail ? throw FakeOracleDatabase.Error(1031) : null : Respond(sql, p),
        };
        var result = await Extract(db, false);
        Assert.Equal("-- Oracle schema/user: APP", Assert.Single(result.Objects, o => o.Type == "Schemas").Ddl);
        if (fail) { Assert.Single(result.Objects); }
        else { Assert.All(result.Objects.Where(o => o.Type != "Schemas"), o => Assert.Equal("", o.Ddl)); }
    }

    [Fact]
    public async Task Extract_EmptySelectionAndMissingService()
    {
        using FakeOracleDatabase db = new() { Execute = Respond };
        Assert.Empty((await Extract(db, false, [])).Objects);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Extract(db, config: Server with { ServiceName = null }));
    }
}
