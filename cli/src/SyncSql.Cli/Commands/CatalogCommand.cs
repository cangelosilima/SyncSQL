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
        Option<DirectoryInfo> objectsRootOption = new("--objects-root")
        {
            Description = "Root of the extracted tree (server/database/type/[schema/]object.sql).",
            Required = true,
        };
        Option<FileInfo> outputOption = new("--output")
        {
            Description = "File path the catalog JSON is written to.",
            Required = true,
        };
        Option<DirectoryInfo?> repoRootOption = new("--repo-root")
        {
            Description = "Git checkout containing --path-prefix, mined for history/heatmap/point-in-time data. Omit to skip all of that.",
        };
        Option<string> pathPrefixOption = new("--path-prefix")
        {
            Description = "Folder inside --repo-root holding the extracted tree.",
            DefaultValueFactory = _ => "objects",
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
        Option<DirectoryInfo?> metricsRootOption = new("--metrics-root")
        {
            Description = "Root of the accumulating metrics history tree. Omit to skip - node.metrics is left empty.",
        };

        Command buildCommand = new("build", "Build catalog.json from an extracted-objects tree.")
        {
            objectsRootOption,
            outputOption,
            repoRootOption,
            pathPrefixOption,
            historyLimitOption,
            maxVersionsOption,
            maxHistoryCallsOption,
            maxCoChangeOption,
            metricsRootOption,
        };

        buildCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(CatalogCommand));
            ICatalogBuilder catalogBuilder = services.GetRequiredService<ICatalogBuilder>();

            CatalogBuildRequest request = new()
            {
                ObjectsRoot = parseResult.GetRequiredValue(objectsRootOption).FullName,
                RepoRoot = parseResult.GetValue(repoRootOption)?.FullName,
                PathPrefix = parseResult.GetValue(pathPrefixOption) ?? "objects",
                HistoryLimit = parseResult.GetValue(historyLimitOption),
                MaxVersionsPerObject = parseResult.GetValue(maxVersionsOption),
                MaxHistoryContentCalls = parseResult.GetValue(maxHistoryCallsOption),
                MaxCoChangeCommitSize = parseResult.GetValue(maxCoChangeOption),
                MetricsRoot = parseResult.GetValue(metricsRootOption)?.FullName,
            };

            try
            {
                Core.Domain.Catalog catalog = await catalogBuilder.BuildAsync(request, cancellationToken);

                FileInfo outputFile = parseResult.GetRequiredValue(outputOption);
                if (outputFile.DirectoryName is { Length: > 0 } outputDirectory)
                {
                    Directory.CreateDirectory(outputDirectory);
                }
                await File.WriteAllTextAsync(outputFile.FullName, JsonSerializer.Serialize(catalog, SyncSqlJsonOptions.Default), cancellationToken);

                logger.LogInformation(
                    "Wrote catalog.json ({NodeCount} node(s), {EdgeCount} edge(s)) -> {Path}",
                    catalog.Nodes.Count, catalog.Edges.Count, outputFile.FullName);
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
}
