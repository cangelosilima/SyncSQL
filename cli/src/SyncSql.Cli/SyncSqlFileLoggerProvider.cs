using System.Text;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Sync;

namespace SyncSql.Cli;

/// <summary>Appends plain log messages to a file for one command invocation.</summary>
internal sealed class SyncSqlFileLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private readonly SyncSqlConsoleLoggerProvider _provider;
    private bool _disposed;

    public SyncSqlFileLoggerProvider(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _writer = new StreamWriter(fullPath, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        _provider = new(new SyncSqlTerminal(_writer, animated: false, colorEnabled: false));
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, _provider.CreateLogger(categoryName));

    private sealed class Logger(SyncSqlFileLoggerProvider owner, ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._gate)
            {
                if (!owner._disposed)
                {
                    inner.Log(logLevel, eventId, state, exception, formatter);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _provider.Dispose();
            _writer.Dispose();
        }
    }
}
