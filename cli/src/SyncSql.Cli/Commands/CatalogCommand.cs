using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Json;

namespace SyncSql.Cli.Commands;

/// <summary>`syncsql catalog build` - standalone catalog.json builder, mirrors Build-Catalog.ps1.</summary>
internal static class CatalogCommand
{
    public static Command Build(IServiceProvider services)
    {
        Option<string> outputRootOption = SyncSqlPaths.OutputRootOption();
        Option<string?> objectsRootOption = new("--objects-root")
        {
            Description = "Root of the extracted tree (server/database/[schema/]type/object.sql). Default: <output-root>, i.e. what `syncsql sync` just wrote.",
        };
        Option<string?> outputOption = new("--output")
        {
            Description = $"File path the catalog JSON is written to. Default: <output-root>/{SyncSqlPaths.CatalogFileName}.",
        };
        Option<string?> repoRootOption = new("--repo-root")
        {
            Description = "Git checkout containing --path-prefix, mined for history/heatmap/point-in-time data. Omit to skip all of that.",
        };
        Option<string> pathPrefixOption = new("--path-prefix")
        {
            Description = "Folder inside --repo-root holding the extracted tree. Default: empty, i.e. the tree starts at the repository root with the server name.",
            DefaultValueFactory = _ => SyncSqlPaths.DefaultPathPrefix,
        };
        Option<int> historyLimitOption = new("--history-limit")
        {
            Description = "Maximum number of commits (touching --path-prefix) to mine.",
            DefaultValueFactory = _ => 250,
        };
        Option<int> maxVersionsOption = new("--max-versions-per-object")
        {
            Description = "Maximum number of historical versions kept (and content-fetched) per object, most recent first.",
            DefaultValueFactory = _ => 15,
        };
        Option<int> maxHistoryCallsOption = new("--max-history-content-calls")
        {
            Description = "Hard cap on total `git show` invocations across the whole mining pass.",
            DefaultValueFactory = _ => 1500,
        };
        Option<int> maxCoChangeOption = new("--max-co-change-commit-size")
        {
            Description = "Commits touching more files than this are excluded from co-change pair counting.",
            DefaultValueFactory = _ => 40,
        };
        Option<string?> metricsRootOption = new("--metrics-root")
        {
            Description = $"Root of the accumulating metrics history tree (`metrics update`'s --history-root, e.g. <output-root>/{SyncSqlPaths.MetricsHistoryDirectoryName}). Omit to skip - node.metrics is left empty.",
        };
        Option<bool> noDynamicSqlOption = new("--no-dynamic-sql")
        {
            Description = "Skip recovering references from SQL built as a string at runtime (OPENQUERY, EXEC of a string, a variable assembled then executed). Those references are tagged `dynamic` in catalog.json rather than mixed in with the rest, so the default is to collect them.",
        };

        Command buildCommand = new("build", "Build catalog.json from an extracted-objects tree.")
        {
            outputRootOption,
            objectsRootOption,
            outputOption,
            repoRootOption,
            pathPrefixOption,
            historyLimitOption,
            maxVersionsOption,
            maxHistoryCallsOption,
            maxCoChangeOption,
            metricsRootOption,
            noDynamicSqlOption,
        };

        buildCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(CatalogCommand));
            ICatalogBuilder catalogBuilder = services.GetRequiredService<ICatalogBuilder>();

            string outputRoot = parseResult.GetValue(outputRootOption) ?? SyncSqlPaths.DefaultOutputRoot;
            string objectsRoot = SyncSqlPaths.Resolve(parseResult.GetValue(objectsRootOption), outputRoot, SyncSqlPaths.ObjectsRelativePath);
            string outputPath = SyncSqlPaths.Resolve(parseResult.GetValue(outputOption), outputRoot, SyncSqlPaths.CatalogFileName);

            CatalogBuildRequest request = new()
            {
                ObjectsRoot = objectsRoot,
                RepoRoot = ToFullPathOrNull(parseResult.GetValue(repoRootOption)),
                PathPrefix = parseResult.GetValue(pathPrefixOption) ?? SyncSqlPaths.DefaultPathPrefix,
                HistoryLimit = parseResult.GetValue(historyLimitOption),
                MaxVersionsPerObject = parseResult.GetValue(maxVersionsOption),
                MaxHistoryContentCalls = parseResult.GetValue(maxHistoryCallsOption),
                MaxCoChangeCommitSize = parseResult.GetValue(maxCoChangeOption),
                MetricsRoot = ToFullPathOrNull(parseResult.GetValue(metricsRootOption)),
                DynamicSql = !parseResult.GetValue(noDynamicSqlOption),
            };

            try
            {
                Core.Domain.Catalog catalog = await catalogBuilder.BuildAsync(request, cancellationToken);

                if (Path.GetDirectoryName(outputPath) is { Length: > 0 } outputDirectory)
                {
                    Directory.CreateDirectory(outputDirectory);
                }
                await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(catalog, SyncSqlJsonOptions.Default), cancellationToken);

                logger.LogInformation(
                    "Wrote catalog.json ({NodeCount} node(s), {EdgeCount} edge(s)) -> {Path}",
                    catalog.Nodes.Count, catalog.Edges.Count, outputPath);
                return 0;
            }
            catch (DirectoryNotFoundException ex)
            {
                logger.LogError("{Message}", ex.Message);
                return 1;
            }
        });

        return new Command("catalog", "Catalog-related commands.") { buildCommand };
    }

    private static string? ToFullPathOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
