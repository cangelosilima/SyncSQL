using System.CommandLine;
using SyncSql.Cli.Parsing;

namespace SyncSql.Cli.Commands;

internal static class ParserCommand
{
    public static Command Build()
    {
        Option<string> file = new("--file") { Required = true, Description = "SQL file to inspect (read only)." };
        Option<string> engine = new("--engine") { Description = "SQL dialect: mssql (default) or oracle." };
        engine.AcceptOnlyFromAmong("mssql", "oracle");
        Option<bool> plain = new("--plain") { Description = "Print all pieces as text instead of opening the dashboard." };
        Command command = new("parser", "Explore a SQL file's syntax tree, tokens, lineage, and diagnostics.") { file, engine, plain };
        command.SetAction(async (result, cancellationToken) =>
        {
            try
            {
                string path = Path.GetFullPath(result.GetValue(file)!);
                string sql = await File.ReadAllTextAsync(path, cancellationToken);
                string dialect = result.GetValue(engine) ?? "mssql";
                SqlInspection inspection = SqlInspection.Parse(path, sql, dialect);
                if (result.GetValue(plain) || Console.IsInputRedirected || Console.IsOutputRedirected ||
                    string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.Ordinal))
                {
                    ParserDashboard.Print(inspection, Console.Out);
                }
                else
                {
                    ParserDashboard.Run(inspection, cancellationToken);
                }

                return inspection.HasErrors ? 1 : 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Console.Error.WriteLine($"Cannot inspect SQL file: {ParserDashboard.Safe(exception.Message)}");
                return 1;
            }
        });
        return command;
    }
}
