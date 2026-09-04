using System.CommandLine;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using SyncSql.Lineage.MsSql.Linting;

namespace SyncSql.Cli.Commands;

/// <summary>
/// `syncsql lint` - parses T-SQL script(s) with the same ScriptDom parser `catalog build` uses for
/// lineage, reporting real syntax errors plus a small set of style/best-practice findings (SELECT *,
/// NOLOCK/READUNCOMMITTED table hints, cursor usage). Touches no database - just reads files off disk.
/// </summary>
internal static class LintCommand
{
    public static Command Build(IServiceProvider services)
    {
        Option<string> outputRootOption = SyncSqlPaths.OutputRootOption();
        Option<string[]> pathOption = new("--path")
        {
            Description = "A .sql file, or a directory searched recursively for *.sql files. Repeatable. Default: <output-root>, i.e. what `syncsql sync` just wrote.",
        };
        Option<string> failOnOption = new("--fail-on")
        {
            Description = "Minimum finding severity that makes the command exit non-zero: 'warning' or 'error'.",
            DefaultValueFactory = _ => "error",
        };

        Command command = new("lint", "Lint T-SQL script(s) for syntax errors and common style/best-practice issues.")
        {
            outputRootOption,
            pathOption,
            failOnOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(LintCommand));

            string outputRoot = parseResult.GetValue(outputRootOption) ?? SyncSqlPaths.DefaultOutputRoot;
            string[] paths = parseResult.GetValue(pathOption) is { Length: > 0 } explicitPaths
                ? [.. explicitPaths.Select(Path.GetFullPath)]
                : [SyncSqlPaths.Resolve(null, outputRoot, SyncSqlPaths.ObjectsRelativePath)];
            string failOnRaw = parseResult.GetValue(failOnOption) ?? "error";
            if (!Enum.TryParse(failOnRaw, ignoreCase: true, out TSqlLintSeverity failOn))
            {
                logger.LogError("Invalid --fail-on value '{Value}' - expected 'warning' or 'error'.", failOnRaw);
                return 1;
            }

            List<string> files = [];
            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    files.AddRange(Directory.EnumerateFiles(path, "*.sql", SearchOption.AllDirectories));
                }
                else if (File.Exists(path))
                {
                    files.Add(path);
                }
                else
                {
                    logger.LogError("Path not found: {Path}", path);
                    return 1;
                }
            }

            files.Sort(StringComparer.Ordinal);

            TSqlLinter linter = new();
            int errorCount = 0;
            int warningCount = 0;

            foreach (string file in files)
            {
                string sql = await File.ReadAllTextAsync(file, cancellationToken);

                foreach (TSqlLintFinding finding in linter.Lint(sql))
                {
                    if (finding.Severity == TSqlLintSeverity.Error)
                    {
                        errorCount++;
                        logger.LogError("{Path}:{Line}:{Column} [{RuleId}] {Message}", file, finding.Line, finding.Column, finding.RuleId, finding.Message);
                    }
                    else
                    {
                        warningCount++;
                        logger.LogWarning("{Path}:{Line}:{Column} [{RuleId}] {Message}", file, finding.Line, finding.Column, finding.RuleId, finding.Message);
                    }
                }
            }

            logger.LogInformation(
                "Linted {FileCount} file(s): {ErrorCount} error(s), {WarningCount} warning(s).",
                files.Count, errorCount, warningCount);

            bool failed = failOn == TSqlLintSeverity.Warning
                ? errorCount + warningCount > 0
                : errorCount > 0;

            return failed ? 1 : 0;
        });

        return command;
    }
}
