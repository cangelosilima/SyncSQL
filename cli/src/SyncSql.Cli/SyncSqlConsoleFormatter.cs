using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using SyncSql.Cli.Sync;

namespace SyncSql.Cli;

/// <summary>
/// A console log formatter matching the bracketed "[INFO] "/"[WARN] "/"[ERROR]"/"[DEBUG]" style the
/// PowerShell pipeline's CI logs already use (SyncSql.Common.psm1's Write-SyncSqlLog), so existing CI
/// log output/tooling built around that format keeps working after the switch to this CLI.
/// </summary>
public sealed class SyncSqlConsoleFormatter() : ConsoleFormatter("syncsql")
{
    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        string? message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        (string label, string color) = logEntry.LogLevel switch
        {
            LogLevel.Critical or LogLevel.Error => ("[ERROR]", TerminalColors.Red),
            LogLevel.Warning => ("[WARN] ", TerminalColors.Yellow),
            LogLevel.Debug or LogLevel.Trace => ("[DEBUG]", TerminalColors.Gray),
            _ => ("[INFO] ", TerminalColors.Cyan),
        };

        textWriter.Write(color);
        textWriter.Write(label);
        textWriter.Write(TerminalColors.Reset);
        textWriter.Write(' ');
        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine(logEntry.Exception);
        }
    }
}
