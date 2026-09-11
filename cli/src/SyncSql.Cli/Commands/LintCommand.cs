using System.CommandLine;
using System.Text.Json;
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
        Option<string?> outputRootOption = SyncSqlPaths.OutputRootOption();
        Option<string[]> pathOption = new("--path")
        {
            Description = "A .sql file, or a directory searched recursively for *.sql files. Repeatable. Default: ./MSSQL, or --output-root when supplied. Oracle SQL is not T-SQL.",
        };
        Option<string?> configOption = new("--config")
        {
            Description = "SQL lint/format JSON configuration. Default: ./config/sql-style.json when present, otherwise the packaged defaults.",
        };
        Option<string?> failOnOption = new("--fail-on")
        {
            Description = "Minimum finding severity that makes the command exit non-zero: 'warning' or 'error'.",
        };

        Command command = new("lint", "Lint T-SQL script(s) for syntax errors and common style/best-practice issues.")
        {
            outputRootOption,
            pathOption,
            failOnOption,
            configOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ILogger logger = services.GetLogger(nameof(LintCommand));

            string outputRoot = parseResult.GetValue(outputRootOption) ?? SyncSqlPaths.DefaultOutputRoot(Core.Domain.DatabaseEngine.MsSql);
            string[] paths = parseResult.GetValue(pathOption) is { Length: > 0 } explicitPaths
                ? [.. explicitPaths.Select(Path.GetFullPath)]
                : [SyncSqlPaths.Resolve(null, outputRoot, SyncSqlPaths.ObjectsRelativePath)];
            TSqlLintConfiguration configuration;
            try
            {
                string? configPath = parseResult.GetValue(configOption);
                configPath ??= File.Exists("config/sql-style.json") ? "config/sql-style.json" : null;
                configuration = configPath is null ? TSqlLintConfiguration.Default : TSqlLintConfiguration.Load(configPath);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                logger.LogError("Invalid SQL style configuration: {Message}", exception.Message);
                return 1;
            }

            string failOnRaw = parseResult.GetValue(failOnOption) ?? configuration.FailOn.ToString();
            if (!(failOnRaw.Equals("warning", StringComparison.OrdinalIgnoreCase) || failOnRaw.Equals("error", StringComparison.OrdinalIgnoreCase)) ||
                !Enum.TryParse(failOnRaw, ignoreCase: true, out TSqlLintSeverity failOn))
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

            TSqlLinter linter = new(configuration);
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
