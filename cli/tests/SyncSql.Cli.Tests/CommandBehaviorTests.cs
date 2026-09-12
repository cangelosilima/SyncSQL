using System.CommandLine;
using System.Reflection;
using System.CommandLine.Invocation;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyncSql.Cli.Commands;
using SyncSql.Cli.Composition;
using SyncSql.Cli.Sync;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Cli.Tests;

public sealed class CommandBehaviorTests : IDisposable
{
    private readonly string _previous = Directory.GetCurrentDirectory();
    private readonly string _root = Directory.CreateTempSubdirectory("syncsql-command-").FullName;
    private readonly ServiceProvider _services;
    private readonly IDatabaseObjectExtractor _extractor = Substitute.For<IDatabaseObjectExtractor>();
    private readonly ICredentialProvider _credentials = Substitute.For<ICredentialProvider>();
    private readonly IDatabaseObjectExtractorResolver _resolver = Substitute.For<IDatabaseObjectExtractorResolver>();
    private static readonly ServerConfig Server = new()
    {
        Name = "SQL",
        Host = "unused",
        Type = DatabaseEngine.MsSql,
        CredentialsVariablePrefix = "TEST",
    };

    public CommandBehaviorTests()
    {
        Directory.SetCurrentDirectory(_root);
        ServiceCollection registrations = new();
        registrations.AddLogging();
        ServiceCollectionExtensions.AddSyncSqlServices(registrations);
        _credentials.Read("TEST").Returns(new PartialCredentials("user", "password"));
        registrations.AddSingleton(_credentials);
        _resolver.Resolve(Arg.Any<DatabaseEngine>()).Returns(_extractor);
        registrations.AddSingleton(_resolver);
        _extractor.ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(new ExtractionOutcome { Objects = [], MetricsSnapshots = new Dictionary<string, MetricsSnapshot>() });
        _services = registrations.BuildServiceProvider();
        WriteConfig();
    }

    private void WriteConfig(SyncSqlConfig? config = null)
    {
        Directory.CreateDirectory("config");
        File.WriteAllText("config/servers.json", JsonSerializer.Serialize(config ?? new SyncSqlConfig
        {
            Servers = [Server],
            Defaults = new ObjectFilterSet { ObjectTypes = ["Tables"] },
        }));
    }

    private Task<int> Run(params string[] args) => new RootCommand
    {
        SyncCommand.Build(_services), ValidateConfigCommand.Build(_services), CatalogCommand.Build(_services),
        MetricsCommand.Build(_services), LintCommand.Build(_services),
    }.Parse(args).InvokeAsync();

    [Fact]
    public async Task ValidateAndSync_UseDefaultConfigAndReportInvalidConfig()
    {
        Assert.Equal(0, await Run("validate-config"));
        Assert.Equal(0, await Run("sync"));
        Assert.Equal(1, await Run("validate-config", "--config", "missing.json"));
        Assert.Equal(1, await Run("sync", "--config", "missing.json"));
    }

    [Theory]
    [InlineData("--db-user", "malformed")]
    [InlineData("--credentials-file", "missing.json")]
    public async Task Sync_RejectsMalformedCredentials(string option, string value) =>
        Assert.Equal(1, await Run("sync", option, value));

    [Fact]
    public async Task Sync_CombinesExplicitAndFileCredentialsAndWritesMetrics()
    {
        File.WriteAllText("credentials.json", "{\"TEST\":{\"password\":\"file-password\"}}");
        var snapshot = new MetricsSnapshot { CapturedAt = DateTimeOffset.UnixEpoch, RowCount = 42 };
        _extractor.ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(new ExtractionOutcome { Objects = [], MetricsSnapshots = new Dictionary<string, MetricsSnapshot> { ["SQL/db/Tables/dbo/t"] = snapshot } });
        Assert.Equal(0, await Run("sync", "--db-user", "TEST=explicit-user", "--credentials-file", "credentials.json"));
        await _extractor.Received().ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(),
            Arg.Is<ExtractionOptions>(o => o.Credentials.Username == "explicit-user" && o.Credentials.Password == "file-password"), Arg.Any<CancellationToken>());
        string json = await File.ReadAllTextAsync("MSSQL/metrics-snapshot/SQL/db/Tables/dbo/t.json");
        Assert.Equal(42, JsonSerializer.Deserialize<MetricsSnapshot>(json)!.RowCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutputWriter_WritesObjectsAndSnapshotsWithAnOptionalObserver(bool reportProgress)
    {
        var observer = Substitute.For<IProgress<ExtractionProgress>>();
        var outcome = new ExtractionOutcome
        {
            Objects = [new ExtractedObject { Server = "SQL", Database = "db", Schema = "dbo", Type = "Tables", Name = "t", Ddl = "CREATE TABLE dbo.t (id int);", Engine = DatabaseEngine.MsSql }],
            MetricsSnapshots = new Dictionary<string, MetricsSnapshot>
            {
                ["SQL/db/Tables/dbo/t"] = new() { CapturedAt = DateTimeOffset.UnixEpoch, RowCount = 42 },
            },
        };
        ServerIdentity identity = ServerIdentity.FromConfig(Server with { Aliases = ["sql-alias"] });
        await ExtractionOutputWriter.WriteAsync(outcome, "objects", "metrics", CancellationToken.None, progress: reportProgress ? observer : null, serverIdentity: identity);
        var parsed = Core.Serialization.ExtractedObjectFile.Parse(await File.ReadAllLinesAsync("objects/SQL/db/dbo/Tables/t.sql"));
        Assert.Equal(identity.Endpoint, parsed.Identity?.ServerIdentity?.Endpoint);
        Assert.True(parsed.Identity?.ServerIdentity?.Matches("sql-alias"));
        Assert.Contains("CREATE TABLE", await File.ReadAllTextAsync("objects/SQL/db/dbo/Tables/t.sql"));
        Assert.Equal(42, JsonSerializer.Deserialize<MetricsSnapshot>(await File.ReadAllTextAsync("metrics/SQL/db/Tables/dbo/t.json"))!.RowCount);
        if (reportProgress)
        {
            Received.InOrder(() =>
            {
                observer.Report(new("Writing files", 1, 0, 2));
                observer.Report(new("Writing files", 1, 1, 2));
                observer.Report(new("Writing files", 1, 2, 2));
            });
        }
    }

    [Fact]
    public async Task Sync_MissingCredentialsAndExtractionFailureFailTheRun()
    {
        _credentials.Read("TEST").Returns(PartialCredentials.None);
        Assert.Equal(1, await Run("sync"));
        _credentials.Read("TEST").Returns(new PartialCredentials("user", "password"));
        _extractor.ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ExtractionOutcome>(new InvalidOperationException("unavailable")));
        Assert.Equal(1, await Run("sync"));
    }

    [Fact]
    public async Task Sync_SkipsExcludedServersAndEmptyObjectTypes()
    {
        Assert.Equal(0, await Run("sync", "--server-exclude", ".*"));
        Assert.Equal(0, await Run("sync", "--server-include", "^other$"));
        WriteConfig(new SyncSqlConfig { Servers = [Server], Defaults = new ObjectFilterSet { ObjectTypes = [] } });
        Assert.Equal(0, await Run("sync"));
        await _extractor.DidNotReceiveWithAnyArgs().ExtractAsync(default!, default!, default!, default);
    }

    [Theory]
    [InlineData(0)] // Default: four workers.
    [InlineData(1)]
    [InlineData(2)]
    public async Task Sync_BoundsConcurrentExtractionsAndWritesEveryResult(int limit)
    {
        int expectedConcurrency = limit == 0 ? 4 : limit;
        ServerConfig[] servers = [.. Enumerable.Range(0, 7).Select(i => Server with { Name = $"SQL{i}" })];
        WriteConfig(new SyncSqlConfig { Servers = servers, Defaults = new ObjectFilterSet { ObjectTypes = ["Tables"] } });
        TaskCompletionSource slotsFilled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0;
        int started = 0;
        int exceededLimit = 0;
        _extractor.ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                ServerConfig server = call.Arg<ServerConfig>();
                if (Interlocked.Increment(ref active) > expectedConcurrency)
                {
                    Interlocked.Exchange(ref exceededLimit, 1);
                }
                if (Interlocked.Increment(ref started) == expectedConcurrency)
                {
                    slotsFilled.TrySetResult();
                }
                try
                {
                    await release.Task.WaitAsync(call.Arg<CancellationToken>());
                    return new ExtractionOutcome
                    {
                        Objects = [new ExtractedObject { Server = server.Name, Database = "db", Schema = "dbo", Type = "Tables", Name = "t", Ddl = "CREATE TABLE dbo.t (id int);", Engine = server.Type }],
                        MetricsSnapshots = new Dictionary<string, MetricsSnapshot>
                        {
                            [$"{server.Name}/db/dbo/Tables/t"] = new() { CapturedAt = DateTimeOffset.UnixEpoch, RowCount = 42 },
                        },
                    };
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });

        Task<int> run = limit == 0 ? Run("sync") : Run("sync", "--max-parallelism", limit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            await slotsFilled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(expectedConcurrency, Volatile.Read(ref started));
        }
        finally
        {
            release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(0, await run);
        Assert.Equal(0, exceededLimit);
        Assert.Equal(servers.Length, started);
        foreach (ServerConfig server in servers)
        {
            Assert.Contains("CREATE TABLE", await File.ReadAllTextAsync($"MSSQL/{server.Name}/db/dbo/Tables/t.sql"));
            Assert.Equal(42, JsonSerializer.Deserialize<MetricsSnapshot>(await File.ReadAllTextAsync($"MSSQL/metrics-snapshot/{server.Name}/db/dbo/Tables/t.json"))!.RowCount);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid")]
    public async Task Sync_RejectsInvalidParallelism(string value)
    {
        Assert.Equal(1, await Run("sync", "--max-parallelism", value));
        await _extractor.DidNotReceiveWithAnyArgs().ExtractAsync(default!, default!, default!, default);
    }

    private sealed class WaitingExtractor : IDatabaseObjectExtractor
    {
        public DatabaseEngine Engine => DatabaseEngine.MsSql;
        public TaskCompletionSource SlotsFilled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started;
        public int Stopped;

        public async Task<ExtractionOutcome> ExtractAsync(ServerConfig server, EffectiveFilters filters, ExtractionOptions options, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref Started) == 2)
            {
                SlotsFilled.TrySetResult();
            }
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Expected cancellation");
            }
            finally
            {
                Interlocked.Increment(ref Stopped);
            }
        }
    }

    [Fact]
    public async Task Sync_CancellationStopsActiveWorkersAndLeavesQueuedServersUnstarted()
    {
        WriteConfig(new SyncSqlConfig
        {
            Servers = [Server, Server with { Name = "SECOND" }, Server with { Name = "QUEUED" }],
            Defaults = new ObjectFilterSet { ObjectTypes = ["Tables"] },
        });
        using CancellationTokenSource cancellation = new();
        WaitingExtractor extractor = new();
        _resolver.Resolve(Arg.Any<DatabaseEngine>()).Returns(extractor);

        // Invoke the action directly: the command-line process-termination wrapper may return
        // before its action has unwound cancellation, which races assertions and fixture cleanup.
        ParseResult parsed = new RootCommand { SyncCommand.Build(_services) }.Parse(["sync", "--max-parallelism", "2"]);
        Task<int> run = Assert.IsAssignableFrom<AsynchronousCommandLineAction>(parsed.Action).InvokeAsync(parsed, cancellation.Token);
        try
        {
            await extractor.SlotsFilled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.Equal(2, extractor.Started);
        Assert.Equal(2, extractor.Stopped);
    }

    [Fact]
    public async Task Sync_ConcurrentDiscoveryKeepsFirstParentAndContinuesAfterFailure()
    {
        WriteConfig(new SyncSqlConfig
        {
            Servers = [Server, Server with { Name = "SECOND", Host = "second" }, Server with { Name = "FAILED" }],
            Defaults = new ObjectFilterSet { ObjectTypes = ["Tables"] },
            Discovery = new DiscoveryConfig { LinkedServers = new LinkedServerDiscoveryConfig { Enabled = true, MaxDepth = 2 } },
        });
        TaskCompletionSource secondCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _extractor.ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                ServerConfig server = call.Arg<ServerConfig>();
                if (server.Name == "FAILED")
                {
                    throw new InvalidOperationException("unavailable");
                }
                if (server.Name == "SQL")
                {
                    await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                else if (server.Name == "SECOND")
                {
                    secondCompleted.TrySetResult();
                }
                return new ExtractionOutcome
                {
                    Objects = [],
                    MetricsSnapshots = new Dictionary<string, MetricsSnapshot>(),
                    DiscoveredLinkedServers = [new DiscoveredLinkedServer { Name = "remote", Product = "SQL Server", DataSource = "remote" }],
                };
            });

        Assert.Equal(1, await Run("sync"));
        await _extractor.Received(4).ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>());
        await _extractor.Received(1).ExtractAsync(
            Arg.Is<ServerConfig>(s => s.Host == "remote" && s.ExportPath != null && s.ExportPath[0] == "SQL"),
            Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sync_DiscoveredFailuresAreBestEffortAndLinksAreFiltered(bool restrictCatalog)
    {
        WriteConfig(new SyncSqlConfig
        {
            Servers = [Server],
            Defaults = new ObjectFilterSet { ObjectTypes = ["Tables"] },
            Discovery = new DiscoveryConfig { LinkedServers = new LinkedServerDiscoveryConfig { Enabled = true, RestrictToLinkedCatalog = restrictCatalog } },
        });
        _extractor.ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<ServerConfig>().Name == "SQL"
                ? new ExtractionOutcome
                {
                    Objects = [],
                    MetricsSnapshots = new Dictionary<string, MetricsSnapshot>(),
                    DiscoveredLinkedServers = [
                        new DiscoveredLinkedServer { Name = "remote", Product = "SQL Server", DataSource = "remote", Catalog = "db" },
                        new DiscoveredLinkedServer { Name = "oracle", Product = "Oracle", DataSource = "oracle" },
                    ],
                }
                : throw new InvalidOperationException("remote unavailable"));
        Assert.Equal(0, await Run("sync"));
        await _extractor.Received(2).ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncAndCatalog_MultipleLinksShareOneConfiguredTarget()
    {
        WriteConfig(new SyncSqlConfig
        {
            Servers = [Server with { Name = "A", Host = "a" }, Server with { Name = "B", Host = "b" },
                Server with { Name = "CENTRAL", Host = "central.example.com", Aliases = ["10.0.0.5"] }],
            Defaults = new ObjectFilterSet { ObjectTypes = ["Tables", "Views", "LinkedServers"] },
            Discovery = new DiscoveryConfig { LinkedServers = new LinkedServerDiscoveryConfig { Enabled = true } },
        });
        _extractor.ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var server = call.Arg<ServerConfig>();
                ExtractedObject obj = new() { Server = server.Name, Database = "db", Schema = "dbo", Type = "Tables", Name = "Orders", Ddl = "CREATE TABLE dbo.Orders (id int);", Engine = DatabaseEngine.MsSql };
                string address = server.Name == "A" ? "central.example.com" : "10.0.0.5";
                return new ExtractionOutcome
                {
                    Objects = server.Name == "CENTRAL" ? [obj] :
                    [obj with { Type = "Views", Name = "Report", Ddl = "CREATE VIEW dbo.Report AS SELECT id FROM SALES.db.dbo.Orders;" },
                        obj with { Database = "_ServerLevel", Schema = null, Type = "LinkedServers", Name = "SALES", Ddl = $"EXEC sp_addlinkedserver @server = N'SALES', @datasrc = N'{address}', @catalog = N'db';" }],
                    MetricsSnapshots = new Dictionary<string, MetricsSnapshot>(),
                    DiscoveredLinkedServers = server.Name == "CENTRAL" ? [] : [new DiscoveredLinkedServer { Name = "SALES", DataSource = address, Product = "SQL Server", Catalog = "db" }],
                };
            });
        Assert.Equal(0, await Run("sync"));
        await _extractor.Received(3).ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>());
        Assert.Equal(0, await Run("catalog", "build", "--output-root", "MSSQL"));
        var catalog = JsonSerializer.Deserialize<Core.Domain.Catalog>(await File.ReadAllTextAsync("MSSQL/catalog.json"))!;
        var target = Assert.Single(catalog.Nodes, n => n.Type == "Tables");
        Assert.Equal("CENTRAL", target.Server);
        Assert.Equal(2, catalog.LinkedServerReferences.Count);
        Assert.All(catalog.LinkedServerReferences, reference => Assert.Equal(target.Id, reference.To));
        Assert.Equal(2, catalog.Edges.Count(edge => edge.To == target.Id));
    }

    [Fact]
    public async Task Consumers_RejectMissingOrAmbiguousRootsAndAcceptExplicitRoots()
    {
        Assert.Equal(1, await Run("metrics", "update"));
        Assert.Equal(1, await Run("catalog", "build"));
        Directory.CreateDirectory("MSSQL");
        Directory.CreateDirectory("ORACLE");
        Assert.Equal(1, await Run("metrics", "update", "--history-root", "history"));
        Directory.CreateDirectory("snapshots");
        Assert.Equal(0, await Run("metrics", "update", "--snapshot-root", "snapshots", "--history-root", "history"));
        Assert.Equal(0, await Run("catalog", "build", "--output-root", "MSSQL", "--metrics-root", "history", "--repo-root", _root));
        Assert.True(File.Exists("MSSQL/catalog.json"));
        Assert.Equal([Path.GetFullPath("MSSQL")], SyncSqlPaths.ReadOutputRoots(null, "MSSQL"));
    }

    [Fact]
    public async Task Lint_ReportsMissingFilesAndSyntaxErrors()
    {
        Assert.Equal(1, await Run("lint", "--path", "missing.sql"));
        File.WriteAllText("invalid.sql", "SELECT FROM WHERE;");
        Assert.Equal(1, await Run("lint", "--path", "invalid.sql"));
    }

    [Fact]
    public void EntryPoint_ComposesCommandsAndRunsValidation()
    {
        MethodInfo entryPoint = typeof(SyncSqlConsoleFormatter).Assembly.EntryPoint!;
        var result = (int)entryPoint.Invoke(null, [new[] { "validate-config" }])!;
        Assert.Equal(0, result);
    }

    public void Dispose()
    {
        _services.Dispose();
        Directory.SetCurrentDirectory(_previous);
        Directory.Delete(_root, recursive: true);
    }
}
