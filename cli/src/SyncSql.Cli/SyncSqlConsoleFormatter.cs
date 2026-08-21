using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

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
            LogLevel.Critical or LogLevel.Error => ("[ERROR]", "\e[31m"),
            LogLevel.Warning => ("[WARN] ", "\e[33m"),
            LogLevel.Debug or LogLevel.Trace => ("[DEBUG]", "\e[90m"),
            _ => ("[INFO] ", "\e[36m"),
        };

        textWriter.Write(color);
        textWriter.Write(label);
        textWriter.Write("\e[0m ");
        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine(logEntry.Exception);
        }
    }
}
