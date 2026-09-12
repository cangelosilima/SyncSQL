using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;
using SyncSql.Extraction.MsSql.Sql;

namespace SyncSql.Extraction.MsSql.Tests;

public sealed class ExtractorTests
{
    private static readonly ServerConfig Server = new() { Name = "SQL", Host = "host", Type = DatabaseEngine.MsSql, CredentialsVariablePrefix = "TEST" };
    private static readonly DatabaseCredentials Credentials = new("user", "password");
    private static readonly string[] AllTypes = ["Schemas", "Tables", "Types", "Views", "StoredProcedures", "Functions", "Triggers", "Synonyms", "Replication", "LinkedServers", "Queues", "Services", "Contracts", "MessageTypes"];
    private static readonly Guid BrokerGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Theory]
    [InlineData("host", null, "host,1433")]
    [InlineData("host\\instance", null, "host\\instance")]
    [InlineData("host\\instance", 1444, "host\\instance,1444")]
    public void Connection_UsesConfiguredEndpointAndEscapesCredentials(string host, int? port, string expected)
    {
        using var connection = MsSqlConnectionFactory.Create(Server with { Host = host, Port = port, Encrypt = false, TrustServerCertificate = true }, "db",
            new DatabaseCredentials("user;name", "pass;word"));
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal(expected, builder.DataSource);
        Assert.Equal("pass;word", builder.Password);
        Assert.Equal("user;name", builder.UserID);
        Assert.True(builder.TrustServerCertificate);
        using var defaults = MsSqlConnectionFactory.Create(Server, "db", Credentials);
        Assert.False(new SqlConnectionStringBuilder(defaults.ConnectionString).TrustServerCertificate);
    }

    private static FakeDatabase Database()
    {
        FakeDatabase db = new();
        db.Rows(MsSqlQueries.Databases, new { Name = "db" }, new { Name = "excluded" });
        db.Rows(MsSqlQueries.ServiceBrokerGuid, new { Id = BrokerGuid });
        db.Rows(MsSqlQueries.Schemas, new SchemaRow { SchemaName = "dbo", OwnerName = "dbo" }, new SchemaRow { SchemaName = "private", OwnerName = "dbo" });
        db.Rows<ModuleObjectRow>(MsSqlQueries.ModuleObjects);
        db.Rows<TypeRow>(MsSqlQueries.Types);
        db.Rows<TableRow>(MsSqlQueries.Tables);
        db.Rows<ColumnDefinitionRow>(MsSqlQueries.ColumnDefinitions);
        db.Rows<SynonymRow>(MsSqlQueries.Synonyms);
        db.Rows<ExtendedPropertyRow>(MsSqlQueries.ExtendedProperties);
        db.Rows<GrantRow>(MsSqlQueries.Grants);
        db.Rows<ColumnListRow>(MsSqlQueries.ColumnList);
        db.Rows<TableVolumeRow>(MsSqlQueries.TableVolume);
        db.Rows<IndexMetricRow>(MsSqlQueries.IndexMetrics);
        db.Rows<OptimizerStatisticRow>(MsSqlQueries.OptimizerStatistics);
        db.Rows<TableSectionRow>(MsSqlQueries.ForeignKeys);
        db.Rows<TableSectionRow>(MsSqlQueries.UniqueConstraints);
        db.Rows<TableSectionRow>(MsSqlQueries.CheckConstraints);
        db.Rows<IndexRow>(MsSqlQueries.Indexes);
        db.Rows<ReplicationRow>(MsSqlQueries.Replication);
        db.Rows<LinkedServerRow>(MsSqlQueries.LinkedServers);
        db.Rows<ConfigurationRow>(MsSqlQueries.Ownership);
        db.Rows<ConfigurationRow>(MsSqlQueries.Permissions);
        db.Rows<ConfigurationRow>(MsSqlQueries.PropertyDefinitions);
        db.Rows<BrokerRow>(ServiceBrokerReader.Query);
        return db;
    }

    private static Task<ExtractionOutcome> Extract(FakeDatabase db, string[]? types = null, bool metrics = true, bool discover = true, IProgress<ExtractionProgress>? progress = null)
    {
        var extractor = new MsSqlObjectExtractor(NullLogger<MsSqlObjectExtractor>.Instance, TimeProvider.System, (_, _, _) => db);
        var server = Server with
        {
            ObjectTypes = types ?? AllTypes,
            Schemas = new NameFilter { Exclude = ["^private$"] },
            Databases = new NameFilter { Exclude = ["^excluded$"] },
            ObjectNames = new NameFilter { Exclude = ["^skip$"] }
        };
        return extractor.ExtractAsync(server, EffectiveFilters.Resolve(null, server),
            new ExtractionOptions { Credentials = Credentials, CaptureMetrics = metrics, DiscoverLinkedServers = discover, Progress = progress }, CancellationToken.None);
    }

    private sealed class ProgressRecorder : IProgress<ExtractionProgress>
    {
        public List<ExtractionProgress> Updates { get; } = [];
        public void Report(ExtractionProgress value) => Updates.Add(value);
    }

    [Fact]
    public async Task Progress_ReportsFilteredDatabaseTotalsAndExtractedObjects()
    {
        using var db = Database();
        db.Rows(MsSqlQueries.Tables, new TableRow { ObjectId = 1, SchemaName = "dbo", TableName = "orders" });
        db.Rows(MsSqlQueries.ModuleObjects, new ModuleObjectRow { TypeCode = "V", SchemaName = "dbo", ObjectName = "orders_view", Definition = "CREATE VIEW dbo.orders_view AS SELECT 1 AS id;" });
        ProgressRecorder progress = new();
        ExtractionOutcome result = await Extract(db, progress: progress);
        Assert.Contains(progress.Updates, p => p.Activity == "db: Tables dbo.orders");
        Assert.Contains(progress.Updates, p => p.Activity == "db: Views dbo.orders_view");
        Assert.Equal(new ExtractionProgress("Databases extracted", result.Objects.Count, 1, 1), progress.Updates[^1]);
        Assert.DoesNotContain(progress.Updates, p => p.Activity.Contains("excluded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Extract_MapsDefinitionsGrantsDescriptionsMetricsAndConfiguration()
    {
        using var db = Database();
        db.Rows(MsSqlQueries.Tables, new TableRow { ObjectId = 1, SchemaName = "dbo", TableName = "orders" }, new TableRow { ObjectId = 2, SchemaName = "dbo", TableName = "empty" });
        db.Rows(MsSqlQueries.ColumnDefinitions, new ColumnDefinitionRow { ObjectId = 1, ColumnName = "id", TypeName = "int" });
        db.Rows(MsSqlQueries.ColumnList, new ColumnListRow { SchemaName = "dbo", TableName = "orders", ColumnName = "id", DataType = "int", OrdinalPosition = 1 },
            new ColumnListRow { SchemaName = "dbo", TableName = "orders", ColumnName = "code", DataType = "int", OrdinalPosition = 2 });
        db.Rows(MsSqlQueries.ExtendedProperties,
            new ExtendedPropertyRow { SchemaName = "dbo", ObjectName = "orders", PropertyName = "MS_Description", PropertyValue = "Orders" },
            new ExtendedPropertyRow { SchemaName = "dbo", ObjectName = "orders", ColumnName = "id", PropertyName = "MS_Description", PropertyValue = "Identifier" },
            new ExtendedPropertyRow { SchemaName = "dbo", ObjectName = "orders", ColumnName = "other", PropertyName = "MS_Description" },
            new ExtendedPropertyRow { SchemaName = "dbo", ObjectName = "unexported", ColumnName = "id", PropertyName = "MS_Description" },
            new ExtendedPropertyRow { SchemaName = "dbo", ObjectName = "empty", PropertyName = "MS_Description" },
            new ExtendedPropertyRow { PropertyName = "Custom", PropertyValue = "ignored" });
        db.Rows(MsSqlQueries.Grants,
            new GrantRow { SchemaName = "dbo", ObjectName = "orders", PermissionName = "SELECT", GranteeName = "reader", StateDesc = "GRANT" },
            new GrantRow { SchemaName = "dbo", ObjectName = "orders", PermissionName = "UPDATE", GranteeName = "reader", StateDesc = "DENY", ColumnName = "id" });
        foreach (string query in new[] { MsSqlQueries.ForeignKeys, MsSqlQueries.UniqueConstraints, MsSqlQueries.CheckConstraints })
        {
            db.Rows(query, new TableSectionRow { SchemaName = "dbo", TableName = "orders", Definition = "constraint one" }, new TableSectionRow { SchemaName = "dbo", TableName = "orders", Definition = "constraint two" });
        }
        db.Rows(MsSqlQueries.Indexes, new IndexRow { SchemaName = "dbo", TableName = "orders", IndexName = "ix1", TypeDesc = "NONCLUSTERED", KeyColumns = "[id]" }, new IndexRow { SchemaName = "dbo", TableName = "orders", IndexName = "ix2", TypeDesc = "NONCLUSTERED", KeyColumns = "[id]" });
        db.Rows(MsSqlQueries.TableVolume, new TableVolumeRow { SchemaName = "dbo", TableName = "orders", RowCount = 42, DataKB = 8, ReservedKB = 24, IndexKB = 16 }, new TableVolumeRow { SchemaName = "dbo", TableName = "empty" });
        db.Rows(MsSqlQueries.IndexMetrics, new IndexMetricRow { SchemaName = "dbo", TableName = "orders", IndexName = "ix1", FragmentationPct = 1.234, PageCount = 2, Seeks = 3, Scans = 4, Lookups = 5, Updates = 6 }, new IndexMetricRow { SchemaName = "dbo", TableName = "orders", IndexName = "ix2" });
        db.Rows(MsSqlQueries.OptimizerStatistics, new OptimizerStatisticRow { SchemaName = "dbo", TableName = "orders", StatName = "s1", Rows = 42, RowsSampled = 40, Steps = 2, ModificationCounter = 1, LastUpdated = DateTime.UnixEpoch }, new OptimizerStatisticRow { SchemaName = "dbo", TableName = "orders", StatName = "s2" });
        foreach (string query in new[] { MsSqlQueries.Ownership, MsSqlQueries.Permissions, MsSqlQueries.PropertyDefinitions })
        {
            db.Rows(query, new ConfigurationRow { Scope = "OBJECT", SchemaName = "dbo", ObjectName = "orders", Definition = "GRANT SELECT ON dbo.orders TO reader;" });
        }
        var result = await Extract(db);
        var table = Assert.Single(result.Objects, o => o.Name == "orders");
        Assert.Contains("[id] INT", table.Ddl, StringComparison.Ordinal);
        Assert.Equal("Orders", table.Description);
        Assert.Equal("Identifier", table.Columns[0].Description);
        Assert.Null(table.Columns[1].Description);
        Assert.Equal(GrantState.Deny, table.Grants[1].State);
        Assert.Equal(7, table.Sections.Count);
        Assert.All(result.Objects, o => Assert.Equal(BrokerGuid, o.ServiceBrokerGuid));
        var snapshot = result.MetricsSnapshots["SQL/db/Tables/dbo/orders"];
        Assert.Equal(42, snapshot.RowCount);
        Assert.Equal(1.23, snapshot.Indexes[0].FragmentationPct);
        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.Statistics[0].LastUpdated);
        Assert.True(db.WasDisposed);
    }

    [Fact]
    public async Task Extract_FiltersObjectsAndAllowsOptionalQueriesToFail()
    {
        using var db = Database();
        db.Rows(MsSqlQueries.Tables, new TableRow { SchemaName = "missing", TableName = "t" }, new TableRow { SchemaName = "private", TableName = "t" }, new TableRow { SchemaName = "dbo", TableName = "skip" }, new TableRow { SchemaName = "dbo", TableName = "kept" });
        db.Rows(MsSqlQueries.ModuleObjects, new ModuleObjectRow { TypeCode = "other" }, new ModuleObjectRow { TypeCode = "P", SchemaName = "missing" }, new ModuleObjectRow { TypeCode = "V", SchemaName = "private" }, new ModuleObjectRow { TypeCode = "FN", SchemaName = "dbo", ObjectName = "skip" }, new ModuleObjectRow { TypeCode = "V", SchemaName = "dbo", ObjectName = "view", Definition = "CREATE VIEW dbo.view AS SELECT 1 AS id;" });
        db.Rows(MsSqlQueries.Synonyms, new SynonymRow { SchemaName = "missing" }, new SynonymRow { SchemaName = "private" }, new SynonymRow { SchemaName = "dbo", SynonymName = "skip" }, new SynonymRow { SchemaName = "dbo", SynonymName = "alias", BaseObjectName = "dbo.kept" });
        db.Rows(MsSqlQueries.Types, new TypeRow { SchemaName = "private" }, new TypeRow { SchemaName = "dbo", TypeName = "skip" }, new TypeRow { SchemaName = "dbo", TypeName = "value", BaseTypeName = "int" });
        foreach (string sql in new[] { MsSqlQueries.ExtendedProperties, MsSqlQueries.Grants, MsSqlQueries.ColumnList, MsSqlQueries.ForeignKeys, MsSqlQueries.UniqueConstraints, MsSqlQueries.CheckConstraints, MsSqlQueries.Indexes, MsSqlQueries.TableVolume, MsSqlQueries.IndexMetrics, MsSqlQueries.OptimizerStatistics, MsSqlQueries.Replication, MsSqlQueries.Ownership }) { db.FailQueries.Add(sql); }
        var result = await Extract(db);
        Assert.Equal(["dbo", "value", "view", "kept", "alias"], result.Objects.Select(o => o.Name));
        Assert.Empty(result.MetricsSnapshots);
        Assert.All(result.Objects, o => Assert.Empty(o.Grants));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Extract_LinkedServersBrokerAndReplication(bool exportLinks, bool discover)
    {
        using var db = Database();
        db.Rows(MsSqlQueries.LinkedServers,
            new LinkedServerRow { LinkedServerName = "remote", Product = "SQL Server", Provider = "MSOLEDBSQL", DataSource = "remote", Catalog = "db", RemoteLoginName = "reader", UsesSelfCredential = false },
            new LinkedServerRow { LinkedServerName = "remote", RemoteLoginName = "READER", UsesSelfCredential = true },
            new LinkedServerRow { LinkedServerName = "skip", UsesSelfCredential = false });
        db.Rows(MsSqlQueries.Replication, new ReplicationRow(), new ReplicationRow { PublicationName = "skip" },
            new ReplicationRow { PublicationName = "publication", Description = "description", Articles = "dbo.orders", SourceDefinitions = "SELECT * FROM dbo.orders;" });
        db.Rows(ServiceBrokerReader.Query,
            new BrokerRow { Type = "Queues", SchemaName = "dbo", Name = "queue", Ddl = "CREATE QUEUE dbo.queue;" },
            new BrokerRow { Type = "Services", Name = "service", Ddl = "CREATE SERVICE service ON QUEUE dbo.queue;" },
            new BrokerRow { Type = "Queues", SchemaName = "private", Name = "hidden" },
            new BrokerRow { Type = "Services", Name = "skip" }, new BrokerRow { Type = "Other", Name = "unknown" });
        string[] types = ["Queues", "Services", "Replication", .. exportLinks ? new[] { "LinkedServers" } : Array.Empty<string>()];
        var result = await Extract(db, types, false, discover);
        Assert.Equal(exportLinks ? 1 : 0, result.Objects.Count(o => o.Type == "LinkedServers"));
        Assert.Equal(2, result.Objects.Count(o => o.Type is "Queues" or "Services"));
        Assert.Contains("SELECT * FROM dbo.orders;", Assert.Single(result.Objects, o => o.Type == "Replication").Ddl, StringComparison.Ordinal);
        if (discover)
        {
            var link = Assert.Single(result.DiscoveredLinkedServers, l => l.Name == "remote");
            Assert.Equal(["reader"], link.RemoteLoginNames);
            Assert.True(link.UsesLocalLogin);
        }
        else { Assert.Empty(result.DiscoveredLinkedServers); }
    }

    [Fact]
    public async Task Extract_ModuleMetadataAndTableWithoutMetrics()
    {
        using var db = Database();
        db.Rows(MsSqlQueries.Tables, new TableRow { SchemaName = "dbo", TableName = "orders" });
        db.Rows(MsSqlQueries.ModuleObjects, new ModuleObjectRow { TypeCode = "FN", SchemaName = "dbo", ObjectName = "ignored" },
            new ModuleObjectRow { TypeCode = "P", SchemaName = "dbo", ObjectName = "procedure", Definition = "CREATE PROCEDURE dbo.procedure AS SELECT 1;" },
            new ModuleObjectRow { TypeCode = "V", SchemaName = "dbo", ObjectName = "view", Definition = "SELECT 1;" });
        db.Rows(MsSqlQueries.ColumnList, new ColumnListRow { SchemaName = "dbo", TableName = "view", ColumnName = "id", DataType = "int" },
            new ColumnListRow { SchemaName = "dbo", TableName = "orders", ColumnName = "id", DataType = "int" });
        db.Rows(MsSqlQueries.ExtendedProperties, new ExtendedPropertyRow { SchemaName = "dbo", ObjectName = "view", PropertyName = "Description", PropertyValue = "View" });
        db.Rows(MsSqlQueries.Grants, new GrantRow { SchemaName = "dbo", ObjectName = "view", PermissionName = "SELECT", StateDesc = "GRANT" });
        var result = await Extract(db, ["Views", "Tables", "StoredProcedures"], false, false);
        Assert.Empty(Assert.Single(result.Objects, o => o.Type == "StoredProcedures").Columns);
        Assert.Equal("View", Assert.Single(result.Objects, o => o.Type == "Views").Description);
        Assert.Empty(result.MetricsSnapshots);
        Assert.DoesNotContain(MsSqlQueries.TableVolume, db.Queries);
    }

    [Fact]
    public async Task Extract_EmptySelectionAndConnectionFailure()
    {
        using var db = Database();
        Assert.Empty((await Extract(db, [], false, false)).Objects);
        db.FailOpen = true;
        await Assert.ThrowsAsync<FakeDatabaseException>(() => Extract(db));
        Assert.True(db.WasDisposed);
    }

}
