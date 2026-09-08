using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyncSql.Cli.Commands;
using SyncSql.Cli.Composition;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SyncSql.Cli.Tests;

public sealed class OutputRootTests : IDisposable
{
    private readonly string _previousDirectory = Directory.GetCurrentDirectory();
    private readonly string _directory = Directory.CreateTempSubdirectory("syncsql-roots-").FullName;
    private readonly ServiceProvider _services;
    private readonly IMetricsHistoryStore _metrics = Substitute.For<IMetricsHistoryStore>();

    public OutputRootTests()
    {
        Directory.SetCurrentDirectory(_directory);
        ServiceCollection services = new();
        services.AddLogging();
        ServiceCollectionExtensions.AddSyncSqlServices(services);
        var credentials = Substitute.For<ICredentialProvider>();
        credentials.Read("TEST").Returns(new PartialCredentials("user", "test-password"));
        services.AddSingleton(credentials);
        var resolver = Substitute.For<IDatabaseObjectExtractorResolver>();
        var extractor = Substitute.For<IDatabaseObjectExtractor>();
        resolver.Resolve(Arg.Any<DatabaseEngine>()).Returns(extractor);
        extractor.ExtractAsync(Arg.Any<ServerConfig>(), Arg.Any<EffectiveFilters>(), Arg.Any<ExtractionOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExtractionOutcome
            {
                Objects = [new ExtractedObject
                {
                    Server = call.Arg<ServerConfig>().Name, Database = "AppDb", Schema = "dbo", Type = "Tables", Name = "Orders",
                    Engine = call.Arg<ServerConfig>().Type, Ddl = "CREATE TABLE dbo.Orders (Id int);",
                }],
                MetricsSnapshots = new Dictionary<string, MetricsSnapshot>(),
            });
        services.AddSingleton(resolver);
        services.AddSingleton(_metrics);
        _services = services.BuildServiceProvider();
        File.WriteAllText("servers.json", JsonSerializer.Serialize(new SyncSqlConfig
        {
            Defaults = new ObjectFilterSet { ObjectTypes = ["Tables"] },
            Servers =
            [
                new ServerConfig { Name = "SQL01", Type = DatabaseEngine.MsSql, Host = "unused", CredentialsVariablePrefix = "TEST" },
                new ServerConfig { Name = "ORA01", Type = DatabaseEngine.Oracle, Host = "unused", ServiceName = "APP", CredentialsVariablePrefix = "TEST" },
            ],
        }));
    }

    private Task<int> Run(params string[] args) => new RootCommand
    {
        SyncCommand.Build(_services), CatalogCommand.Build(_services), MetricsCommand.Build(_services), LintCommand.Build(_services),
    }.Parse(args).InvokeAsync();

    [Fact]
    public async Task Sync_MixedEngines_UsesUppercaseTypeRootsAndSeparateSnapshots()
    {
        Assert.Equal(0, await Run("sync", "--config", "servers.json"));
        Assert.True(File.Exists("MSSQL/SQL01/AppDb/dbo/Tables/Orders.sql"));
        Assert.True(File.Exists("ORACLE/ORA01/AppDb/dbo/Tables/Orders.sql"));
        Assert.True(Directory.Exists("MSSQL/metrics-snapshot"));
        Assert.True(Directory.Exists("ORACLE/metrics-snapshot"));
        Assert.False(Directory.Exists("syncsql-output"));
    }

    [Fact]
    public async Task Sync_ExplicitOutputRoot_IsUsedExactly()
    {
        Assert.Equal(0, await Run("sync", "--config", "servers.json", "--output-root", "custom"));
        Assert.True(File.Exists("custom/SQL01/AppDb/dbo/Tables/Orders.sql"));
        Assert.True(File.Exists("custom/ORA01/AppDb/dbo/Tables/Orders.sql"));
        Assert.False(Directory.Exists("MSSQL"));
        Assert.False(Directory.Exists("ORACLE"));
    }

    [Fact]
    public async Task Sync_SpecificPathOverrides_WinOverEngineDefaults()
    {
        Assert.Equal(0, await Run("sync", "--config", "servers.json", "--staging-root", "objects", "--metrics-snapshot-root", "snapshots"));
        Assert.True(File.Exists("objects/SQL01/AppDb/dbo/Tables/Orders.sql"));
        Assert.True(File.Exists("objects/ORA01/AppDb/dbo/Tables/Orders.sql"));
        Assert.True(Directory.Exists("snapshots"));
        Assert.False(Directory.Exists("MSSQL"));
    }

    [Fact]
    public async Task CatalogAndMetrics_DefaultsVisitBothEngineRoots()
    {
        Assert.Equal(0, await Run("sync", "--config", "servers.json"));
        Assert.Equal(0, await Run("catalog", "build"));
        Assert.True(File.Exists("MSSQL/catalog.json"));
        Assert.True(File.Exists("ORACLE/catalog.json"));
        Assert.Equal(0, await Run("metrics", "update"));
        foreach (string engine in new[] { "MSSQL", "ORACLE" })
        {
            await _metrics.Received(1).UpdateAsync(Arg.Is<MetricsHistoryUpdateRequest>(request =>
                request.SnapshotRoot == Path.Combine(_directory, engine, "metrics-snapshot") &&
                request.HistoryRoot == Path.Combine(_directory, engine, "metrics")), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Catalog_OneExplicitOutputCannotOverwriteAcrossTwoEngines()
    {
        Directory.CreateDirectory("MSSQL");
        Directory.CreateDirectory("ORACLE");
        Assert.Equal(1, await Run("catalog", "build", "--output", "catalog.json"));
        Assert.False(File.Exists("catalog.json"));
    }

    [Fact]
    public async Task Lint_DefaultOnlyVisitsMssql()
    {
        Directory.CreateDirectory("MSSQL");
        Directory.CreateDirectory("ORACLE");
        File.WriteAllText("MSSQL/valid.sql", "SELECT 1;");
        File.WriteAllText("ORACLE/oracle.sql", "this is deliberately not T-SQL");
        Assert.Equal(0, await Run("lint"));
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_previousDirectory);
        _services.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
