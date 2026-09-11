using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyncSql.Cli.Commands;
using SyncSql.Cli.Composition;
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
        var resolver = Substitute.For<IDatabaseObjectExtractorResolver>();
        resolver.Resolve(Arg.Any<DatabaseEngine>()).Returns(_extractor);
        registrations.AddSingleton(resolver);
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
