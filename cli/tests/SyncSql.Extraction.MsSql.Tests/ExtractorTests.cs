using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;
using SyncSql.Extraction.MsSql.Sql;

namespace SyncSql.Extraction.MsSql.Tests;

public sealed class ExtractorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DefaultExclusions_ControlDatabaseAndSchemaScope_KeepServerMetadata(bool enabled)
    {
        using FakeDatabase db = Database();
        db.Rows(MsSqlQueries.Databases, new { Name = "master" }, new { Name = "model" }, new { Name = "msdb" }, new { Name = "tempdb" }, new { Name = "App" });
        db.Rows(MsSqlQueries.Schemas, new SchemaRow { SchemaName = "sys", OwnerName = "dbo" },
            new SchemaRow { SchemaName = "INFORMATION_SCHEMA", OwnerName = "dbo" }, new SchemaRow { SchemaName = "dbo", OwnerName = "dbo" });
        db.Rows(MsSqlQueries.Logins, new PrincipalRow { Name = "app_login", LoginName = "app_login" });
        db.Rows(MsSqlQueries.LinkedServers, new LinkedServerRow { LinkedServerName = "remote" });
        List<string> connections = [];
        var extractor = new MsSqlObjectExtractor(NullLogger<MsSqlObjectExtractor>.Instance, TimeProvider.System,
            (_, database, _) => { connections.Add(database); return db; });
        ServerConfig server = Server with { UseDefaultExclusions = enabled, ObjectTypes = ["Schemas", "Logins", "LinkedServers"] };
        ExtractionOutcome result = await extractor.ExtractAsync(server, EffectiveFilters.Resolve(null, server),
            new ExtractionOptions { Credentials = Credentials, MaxParallelism = 1 }, default);
        Assert.Empty(result.FailedDatabases);
        Assert.Single(result.Objects, o => o.Type == "Logins");
        Assert.Single(result.Objects, o => o.Type == "LinkedServers");
        Assert.Equal(enabled ? 1 : 15, result.Objects.Count(o => o.Type == "Schemas"));
        Assert.Contains(result.Objects, o => o.Database == "App" && o.Name == "dbo");
        Assert.Equal(enabled ? 2 : 3, connections.Count(database => database == "master"));
        Assert.Equal(!enabled, connections.Contains("tempdb"));
    }

    [Fact]
    public async Task Extracts_login_and_user_context_without_passwords()
    {
        FakeDatabase db = Database();
        db.Rows(MsSqlQueries.Logins, new PrincipalRow { Name = "entitlements", LoginName = "entitlements", DefaultDatabase = "Orders" });
        db.Rows(MsSqlQueries.Users, new PrincipalRow { Name = "order_reader", LoginName = "entitlements", DefaultSchema = "sales" });
        ExtractionOutcome result = await Extract(db, ["Logins", "Users"], false, false);
        ExtractedObject login = Assert.Single(result.Objects, obj => obj.Type == "Logins");
        ExtractedObject user = Assert.Single(result.Objects, obj => obj.Type == "Users");
        Assert.Equal("Orders", login.Principal?.DefaultDatabase);
        Assert.Equal("entitlements", user.Principal?.Login);
        Assert.Equal("sales", user.Principal?.DefaultSchema);
        Assert.DoesNotContain("password_hash", MsSqlQueries.Logins);
    }
    private static readonly ServerConfig Server = new() { Name = "SQL", Host = "host", Type = DatabaseEngine.MsSql, CredentialsVariablePrefix = "TEST" };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_principals_do_not_discard_other_objects(bool logins)
    {
        using FakeDatabase db = Database();
        db.FailQueries.Add(logins ? MsSqlQueries.Logins : MsSqlQueries.Users);
        ExtractionOutcome result = await Extract(db, [logins ? "Logins" : "Users", "Schemas"], false, false);
        Assert.Contains(result.Objects, obj => obj.Type == "Schemas");
        Assert.DoesNotContain(result.Objects, obj => obj.Type is "Logins" or "Users");
    }

    [Fact]
    public async Task Principal_names_obey_object_filters()
    {
        using FakeDatabase db = Database();
        db.Rows(MsSqlQueries.Logins, new PrincipalRow { Name = "skip" }, new PrincipalRow { Name = "visible" });
        ExtractionOutcome result = await Extract(db, ["Logins"], false, false);
        Assert.Equal("visible", Assert.Single(result.Objects).Name);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("other", null)]
    [InlineData("OraOLEDB.Oracle", DatabaseEngine.Oracle)]
    [InlineData("SQLNCLI11", DatabaseEngine.MsSql)]
    [InlineData("MSOLEDBSQL", DatabaseEngine.MsSql)]
    [InlineData("SQLOLEDB", DatabaseEngine.MsSql)]
    public async Task Linked_server_provider_identifies_the_destination_engine(string? provider, DatabaseEngine? engine)
    {
        using FakeDatabase db = Database();
        db.Rows(MsSqlQueries.LinkedServers, new LinkedServerRow { LinkedServerName = "remote", Provider = provider, Catalog = null, DataSource = null, UsesSelfCredential = false, RemoteLoginName = "reader" });
        LinkMetadata metadata = Assert.Single((await Extract(db, ["LinkedServers"], false, false)).Objects).Link!;
        Assert.Equal(engine, metadata.TargetEngine);
        Assert.Null(metadata.Database);
        Assert.Empty(metadata.Evidence);
        Assert.Equal("reader", Assert.Single(metadata.Logins).RemoteUser);
    }
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

    [Theory]
    [InlineData("host", null, "host,1433")]
    [InlineData("host\\instance", null, "host\\instance")]
    [InlineData("host\\instance", 1444, "host\\instance,1444")]
    public void Connection_IntegratedSecurity_OmitsCredentials(string host, int? port, string expected)
    {
        using var connection = MsSqlConnectionFactory.Create(Server with
        {
            Host = host,
            Port = port,
            IntegratedSecurity = true,
            Encrypt = false,
            TrustServerCertificate = true,
        }, "db", Credentials);
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
        Assert.True(builder.IntegratedSecurity);
        Assert.Equal(expected, builder.DataSource);
        Assert.Equal("db", builder.InitialCatalog);
        Assert.False(builder.ShouldSerialize("User ID"));
        Assert.False(builder.ShouldSerialize("Password"));
        Assert.Equal(SqlConnectionEncryptOption.Optional, builder.Encrypt);
        Assert.True(builder.TrustServerCertificate);
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

        using var setupFailureDb = Database();
        setupFailureDb.FailQueries.Add("SET NUMERIC_ROUNDABORT OFF;");
        await Assert.ThrowsAsync<FakeDatabaseException>(() => Extract(setupFailureDb));
        Assert.True(setupFailureDb.WasDisposed);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("setup")]
    [InlineData("catalog")]
    [InlineData("late")]
    public async Task Extract_DatabaseFailureRetainsResultsAndContinues(string failure)
    {
        using var master = Database();
        master.Rows(MsSqlQueries.Databases, new { Name = "before" }, new { Name = "broken" }, new { Name = "after" });
        master.Rows(MsSqlQueries.LinkedServers, new LinkedServerRow { LinkedServerName = "remote", DataSource = "remote", UsesSelfCredential = true });
        using var before = Database();
        using var broken = Database();
        using var after = Database();
        foreach (FakeDatabase db in new[] { before, broken, after })
        {
            db.Rows(MsSqlQueries.Tables, new TableRow { ObjectId = 1, SchemaName = "dbo", TableName = "orders" });
            db.Rows(MsSqlQueries.TableVolume, new TableVolumeRow { SchemaName = "dbo", TableName = "orders", RowCount = 42 });
        }
        broken.FailOpen = failure == "open";
        if (failure != "open")
        {
            broken.FailQueries.Add(failure switch
            {
                "setup" => "SET NUMERIC_ROUNDABORT OFF;",
                "catalog" => MsSqlQueries.Schemas,
                _ => ServiceBrokerReader.Query,
            });
        }
        System.Collections.Concurrent.ConcurrentQueue<string> opened = new();
        var extractor = new MsSqlObjectExtractor(NullLogger<MsSqlObjectExtractor>.Instance, TimeProvider.System, (_, database, _) =>
        {
            opened.Enqueue(database);
            return database switch { "before" => before, "broken" => broken, "after" => after, _ => master };
        });
        var server = Server with { ObjectTypes = AllTypes };
        ProgressRecorder progress = new();
        ExtractionOutcome result = await extractor.ExtractAsync(server, EffectiveFilters.Resolve(null, server),
            new ExtractionOptions { Credentials = Credentials, DiscoverLinkedServers = true, Progress = progress }, CancellationToken.None);

        Assert.Equal(new[] { "master", "master", "before", "broken", "after" }.Order(), opened.Order());
        Assert.True(result.IsPartial);
        Assert.Equal(new DatabaseExtractionFailure("broken", "Database unavailable"), Assert.Single(result.FailedDatabases));
        Assert.Contains(result.Objects, o => o.Database == "before" && o.Name == "orders");
        Assert.Contains(result.Objects, o => o.Database == "after" && o.Name == "orders");
        Assert.Equal(42, result.MetricsSnapshots["SQL/before/Tables/dbo/orders"].RowCount);
        Assert.Equal(42, result.MetricsSnapshots["SQL/after/Tables/dbo/orders"].RowCount);
        Assert.Equal(failure == "late", result.MetricsSnapshots.ContainsKey("SQL/broken/Tables/dbo/orders"));
        Assert.Single(result.DiscoveredLinkedServers);
        Assert.True(broken.WasDisposed);
        Assert.True(after.WasDisposed);
        Assert.Equal(new ExtractionProgress("Databases processed (partially complete)", result.Objects.Count, 3, 3), progress.Updates[^1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Extract_OneServerUsesBudgetAcrossDatabases(int limit)
    {
        using FakeDatabase master = Database();
        string[] names = [.. Enumerable.Range(0, 7).Select(i => $"db{i}")];
        master.Rows(MsSqlQueries.Databases, names.Select(name => new { Name = name }).ToArray());
        TaskCompletionSource full = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        System.Collections.Concurrent.ConcurrentBag<FakeDatabase> sessions = [];
        int started = 0;
        MsSqlObjectExtractor extractor = new(NullLogger<MsSqlObjectExtractor>.Instance, TimeProvider.System, (_, database, _) =>
        {
            if (database == "master") { return master; }
            FakeDatabase session = Database();
            session.Rows(MsSqlQueries.Tables, new TableRow { ObjectId = 1, SchemaName = "dbo", TableName = "orders" });
            session.BeforeOpenAsync = async token =>
            {
                if (Interlocked.Increment(ref started) == limit) { full.TrySetResult(); }
                await release.Task.WaitAsync(token);
            };
            sessions.Add(session);
            return session;
        });
        ServerConfig server = Server with { ObjectTypes = ["Tables"] };
        ProgressRecorder progress = new();
        Task<ExtractionOutcome> run = extractor.ExtractAsync(server, EffectiveFilters.Resolve(null, server),
            new ExtractionOptions { Credentials = Credentials, MaxParallelism = limit, Progress = progress }, CancellationToken.None);
        try
        {
            await full.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(limit, Volatile.Read(ref started));
        }
        finally
        {
            release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        ExtractionOutcome result = await run;
        Assert.False(result.IsPartial);
        Assert.Equal(names, result.Objects.Select(o => o.Database));
        Assert.All(sessions, session => Assert.True(session.WasDisposed));
        Assert.Equal(result.Objects.Count, progress.Updates[^1].ObjectsExtracted);
        Assert.Equal(progress.Updates.Select(p => p.ObjectsExtracted).Order(), progress.Updates.Select(p => p.ObjectsExtracted));
    }

    [Fact]
    public async Task Extract_AllDatabasesFail_ReturnsPartialOutcome()
    {
        using var master = Database();
        using var broken = Database();
        broken.FailOpen = true;
        var extractor = new MsSqlObjectExtractor(NullLogger<MsSqlObjectExtractor>.Instance, TimeProvider.System,
            (_, database, _) => database == "master" ? master : broken);
        ExtractionOutcome result = await extractor.ExtractAsync(Server, EffectiveFilters.Resolve(null, Server),
            new ExtractionOptions { Credentials = Credentials }, CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.Equal(["db", "excluded"], result.FailedDatabases.Select(f => f.Database));
        Assert.Empty(result.Objects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extract_CancellationDoesNotBecomeADatabaseFailure(bool databaseException)
    {
        using var master = Database();
        using CancellationTokenSource cancellation = new();
        System.Collections.Concurrent.ConcurrentQueue<string> opened = new();
        var extractor = new MsSqlObjectExtractor(NullLogger<MsSqlObjectExtractor>.Instance, TimeProvider.System, (_, database, _) =>
        {
            opened.Enqueue(database);
            if (database != "master")
            {
                if (databaseException)
                {
                    cancellation.Cancel();
                    throw new FakeDatabaseException();
                }
                throw new OperationCanceledException();
            }
            return master;
        });
        Task<ExtractionOutcome> extraction = extractor.ExtractAsync(Server, EffectiveFilters.Resolve(null, Server),
            new ExtractionOptions { Credentials = Credentials, MaxParallelism = 1 }, cancellation.Token);

        if (databaseException)
        {
            await Assert.ThrowsAsync<FakeDatabaseException>(() => extraction);
        }
        else
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => extraction);
        }
        Assert.Equal(["master", "db"], opened);
    }

}
