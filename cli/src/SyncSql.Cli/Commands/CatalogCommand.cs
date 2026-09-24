using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Serialization;

namespace SyncSql.Cli.Commands;

/// <summary>`syncsql catalog build` - consolidated catalog assembly and static publication.</summary>
internal static class CatalogCommand
{
    public static Command Build(IServiceProvider services)
    {
        Option<string?> outputRootOption = SyncSqlPaths.OutputRootOption();
        Option<string[]> objectsRootOption = new("--objects-root")
        {
            Description = "Extracted tree roots to consolidate. May be repeated or supplied as a list. Default: --output-root, or all existing MSSQL/ORACLE roots.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        Option<string?> outputOption = new("--output")
        {
            Description = "Manifest path. Default: <selected-root>/catalog.json for one root; ./catalog/catalog.json for multiple roots. Payloads are written beside it in _catalog/.",
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
            Description = "Override the metrics history root for all inputs. Default: each input's metrics/ directory when present.",
        };
        Option<bool> noDynamicSqlOption = new("--no-dynamic-sql")
        {
            Description = "Skip recovering references from SQL built as a string at runtime (OPENQUERY, EXEC of a string, a variable assembled then executed). Those references are tagged `dynamic` in catalog.json rather than mixed in with the rest, so the default is to collect them.",
        };

        Option<bool> pruneOption = new("--prune") { Description = "Remove unreferenced hashed payloads after publishing the manifest. Use only in a dedicated catalog directory." };

        Command buildCommand = new("build", "Build one consolidated, partitioned catalog from extracted object trees.")
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
            pruneOption,
        };

        buildCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(CatalogCommand));
            ICatalogBuilder catalogBuilder = services.GetRequiredService<ICatalogBuilder>();

            try
            {
                string[] explicitRoots = parseResult.GetValue(objectsRootOption) ?? [];
                string[] roots = explicitRoots.Length > 0 ? [.. explicitRoots.Select(Path.GetFullPath).Distinct(FileSystemPaths.Comparer)]
                    : SyncSqlPaths.ReadOutputRoots(parseResult.GetValue(outputRootOption));
                string objectsRoot = CommonRoot(roots);
                string outputRoot = parseResult.GetValue(outputRootOption) ?? (roots.Length == 1 ? roots[0] : "catalog");
                string outputPath = SyncSqlPaths.Resolve(parseResult.GetValue(outputOption), outputRoot, SyncSqlPaths.CatalogFileName);

                CatalogBuildRequest request = new()
                {
                    ObjectsRoot = objectsRoot,
                    Inputs = [.. roots.Select(root => new CatalogInput
                        {
                            ObjectsRoot = root,
                            MetricsRoot = Directory.Exists(Path.Combine(root, SyncSqlPaths.MetricsHistoryDirectoryName))
                                ? Path.Combine(root, SyncSqlPaths.MetricsHistoryDirectoryName) : null,
                        })],
                    RepoRoot = ToFullPathOrNull(parseResult.GetValue(repoRootOption)),
                    PathPrefix = parseResult.GetValue(pathPrefixOption) ?? SyncSqlPaths.DefaultPathPrefix,
                    HistoryLimit = parseResult.GetValue(historyLimitOption),
                    MaxVersionsPerObject = parseResult.GetValue(maxVersionsOption),
                    MaxHistoryContentCalls = parseResult.GetValue(maxHistoryCallsOption),
                    MaxCoChangeCommitSize = parseResult.GetValue(maxCoChangeOption),
                    MetricsRoot = ToFullPathOrNull(parseResult.GetValue(metricsRootOption)),
                    DynamicSql = !parseResult.GetValue(noDynamicSqlOption),
                };

                Core.Domain.Catalog catalog = await catalogBuilder.BuildAsync(request, cancellationToken);

                if (Path.GetDirectoryName(outputPath) is { Length: > 0 } outputDirectory)
                {
                    Directory.CreateDirectory(outputDirectory);
                }
                await services.GetRequiredService<ICatalogPublisher>().PublishAsync(catalog, outputPath, parseResult.GetValue(pruneOption), cancellationToken);

                logger.LogInformation(
                    "Wrote consolidated catalog ({NodeCount} node(s), {EdgeCount} edge(s)) -> {Path}",
                    catalog.Nodes.Count, catalog.Edges.Count, outputPath);
                return 0;
            }
            catch (DirectoryNotFoundException ex)
            {
                logger.LogError("{Message}", ex.Message);
                return 1;
            }
            catch (InvalidDataException ex)
            {
                logger.LogError("{Message}", ex.Message);
                return 1;
            }
        });

        return new Command("catalog", "Catalog-related commands.") { buildCommand };
    }

    private static string? ToFullPathOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    internal static string CommonRoot(string[] roots)
    {
        string common = roots[0];
        foreach (string root in roots.Skip(1))
        {
            while (!string.Equals(root, common, FileSystemPaths.Comparison)
                && !root.StartsWith(Path.EndsInDirectorySeparator(common) ? common : common + Path.DirectorySeparatorChar, FileSystemPaths.Comparison))
            {
                common = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(common))
                    ?? throw new InvalidDataException("Catalog inputs must share a filesystem root. Stage exports on the same drive before consolidating.");
            }
        }
        return common;
    }
}
