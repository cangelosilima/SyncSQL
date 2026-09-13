using Microsoft.Extensions.Logging;
using SyncSql.Cli.Sync;

namespace SyncSql.Cli;

/// <summary>Synchronous log writes share the live display's lock, so refreshes cannot split log lines.</summary>
internal sealed class SyncSqlConsoleLoggerProvider(SyncSqlTerminal terminal) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(terminal);
    public void Dispose() { }

    private sealed class Logger(SyncSqlTerminal terminal) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            string message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null)
            {
                return;
            }
            (string label, string color) = logLevel switch
            {
                LogLevel.Critical or LogLevel.Error => ("[ERROR]", TerminalColors.Red),
                LogLevel.Warning => ("[WARN] ", TerminalColors.Yellow),
                LogLevel.Debug or LogLevel.Trace => ("[DEBUG]", TerminalColors.Gray),
                _ => ("[INFO] ", TerminalColors.Cyan),
            };
            string rendered = TerminalColors.Wrap(label, color, terminal.ColorEnabled);
            string details = exception is null ? string.Empty : $"{TerminalColors.Wrap(exception.ToString(), TerminalColors.Red, terminal.ColorEnabled)}{Environment.NewLine}";
            terminal.WriteLog($"{rendered} {message}{Environment.NewLine}{details}");
        }
    }
}
