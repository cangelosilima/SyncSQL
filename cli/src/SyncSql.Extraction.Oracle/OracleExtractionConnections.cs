using System.Collections.Concurrent;
using System.Data.Common;

namespace SyncSql.Extraction.Oracle;

/// <summary>Exclusive sessions reused by scheduled jobs, with metadata transforms initialized once.</summary>
internal sealed class OracleExtractionConnections(Func<DbConnection> create) : IAsyncDisposable
{
    private readonly ConcurrentBag<DbConnection> _idle = [];

    public async Task<T> UseAsync<T>(Func<DbConnection, Task<T>> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_idle.TryTake(out DbConnection? connection))
        {
            connection = create();
            try
            {
                await connection.OpenAsync(cancellationToken);
                await OracleConnectionFactory.InitializeAsync(connection, cancellationToken);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
        try { return await work(connection); }
        finally { _idle.Add(connection); }
    }

    public async ValueTask DisposeAsync()
    {
        while (_idle.TryTake(out DbConnection? connection)) { await connection.DisposeAsync(); }
    }
}
