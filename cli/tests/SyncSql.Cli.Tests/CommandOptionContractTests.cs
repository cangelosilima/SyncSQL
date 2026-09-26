using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyncSql.Cli.Commands;
using SyncSql.Core.Abstractions;
using CatalogModel = SyncSql.Core.Domain.Catalog;

namespace SyncSql.Cli.Tests;

public sealed class CommandOptionContractTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("syncsql-options-").FullName;

    [Fact]
    public async Task CatalogForwardsExplicitOptionsAndSupportsMultipleInputRoots()
    {
        string first = Directory.CreateDirectory(Path.Combine(_root, "first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(_root, "second")).FullName;
        string firstMetrics = Directory.CreateDirectory(Path.Combine(first, "metrics")).FullName;
        string outputRoot = Path.Combine(_root, "published");
        string repo = Path.Combine(_root, "repo");
        string metrics = Path.Combine(_root, "metrics-override");
        ICatalogBuilder builder = Substitute.For<ICatalogBuilder>();
        ICatalogPublisher publisher = Substitute.For<ICatalogPublisher>();
        CatalogModel catalog = new() { GeneratedAt = DateTimeOffset.UnixEpoch, Servers = [], TypeCounts = new Dictionary<string, int>(), Nodes = [], Edges = [] };
        builder.BuildAsync(Arg.Any<CatalogBuildRequest>(), Arg.Any<CancellationToken>()).Returns(catalog);
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddSingleton(builder);
        registrations.AddSingleton(publisher);
        using ServiceProvider services = registrations.BuildServiceProvider();

        List<string> arguments = ["catalog", "build", "--objects-root", first, second,
            "--output-root", outputRoot, "--repo-root", repo, "--metrics-root", metrics,
            "--path-prefix", "extracted", "--prune"];
        var parsed = new RootCommand { CatalogCommand.Build(services) }.Parse([.. arguments]);
        Assert.Empty(parsed.Errors);
        Assert.Equal(0, await parsed.InvokeAsync());
        await builder.Received(1).BuildAsync(Arg.Is<CatalogBuildRequest>(request =>
            request.ObjectsRoot == _root && request.Inputs.Count == 2 &&
            request.Inputs[0].ObjectsRoot == first && request.Inputs[0].MetricsRoot == firstMetrics &&
            request.Inputs[1].ObjectsRoot == second && request.Inputs[1].MetricsRoot == null &&
            request.RepoRoot == repo && request.MetricsRoot == metrics &&
            request.PathPrefix == "extracted"), Arg.Any<CancellationToken>());
        await publisher.Received(1).PublishAsync(catalog, Path.Combine(outputRoot, "catalog.json"), true,
            Arg.Any<CancellationToken>(), Arg.Any<IProgress<CatalogProgress>>());
    }

    [Fact]
    public async Task CatalogDefaultsPublicationToItsSingleInputRoot()
    {
        ICatalogBuilder builder = Substitute.For<ICatalogBuilder>();
        ICatalogPublisher publisher = Substitute.For<ICatalogPublisher>();
        CatalogModel catalog = new() { GeneratedAt = DateTimeOffset.UnixEpoch, Servers = [], TypeCounts = new Dictionary<string, int>(), Nodes = [], Edges = [] };
        builder.BuildAsync(Arg.Any<CatalogBuildRequest>(), Arg.Any<CancellationToken>()).Returns(catalog);
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddSingleton(builder);
        registrations.AddSingleton(publisher);
        using ServiceProvider services = registrations.BuildServiceProvider();

        Assert.Equal(0, await new RootCommand { CatalogCommand.Build(services) }
            .Parse(["catalog", "build", "--objects-root", _root]).InvokeAsync());
        await publisher.Received(1).PublishAsync(catalog, Path.Combine(_root, "catalog.json"), false,
            Arg.Any<CancellationToken>(), Arg.Any<IProgress<CatalogProgress>>());
    }

    [Theory]
    [InlineData("--path")]
    [InlineData("--output-root")]
    public async Task LintFindsSyntaxErrorsInNestedSqlFiles(string option)
    {
        string nested = Directory.CreateDirectory(Path.Combine(_root, "nested")).FullName;
        await File.WriteAllTextAsync(Path.Combine(nested, "invalid.sql"), "SELECT FROM WHERE;");
        ServiceCollection registrations = new();
        registrations.AddLogging();
        using ServiceProvider services = registrations.BuildServiceProvider();

        Assert.Equal(1, await new RootCommand { LintCommand.Build(services) }
            .Parse(["lint", option, _root]).InvokeAsync());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
