using System.CommandLine;
using SyncSql.Cli.Parsing;

namespace SyncSql.Cli.Commands;

internal static class ParserCommand
{
    public static Command Build(ParserTerminal? terminal = null)
    {
        terminal ??= new ParserTerminal();
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
                if (UsePlainOutput(result.GetValue(plain), terminal.InputRedirected, terminal.OutputRedirected, terminal.TerminalType))
                {
                    ParserDashboard.Print(inspection, terminal.Output);
                }
                else
                {
                    ParserDashboard.Run(inspection, terminal, cancellationToken);
                }

                return inspection.HasErrors ? 1 : 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                terminal.Error.WriteLine($"Cannot inspect SQL file: {ParserDashboard.Safe(exception.Message)}");
                return 1;
            }
        });
        return command;
    }

    internal static bool UsePlainOutput(bool requested, bool inputRedirected, bool outputRedirected, string? terminalType) =>
        requested || inputRedirected || outputRedirected || string.Equals(terminalType, "dumb", StringComparison.Ordinal);
}
