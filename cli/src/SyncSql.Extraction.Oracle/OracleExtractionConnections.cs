using System.Data.Common;

namespace SyncSql.Extraction.Oracle;

/// <summary>Each scheduled job owns a connection lease and releases it before returning its scheduler slot.</summary>
internal sealed class OracleExtractionConnections(Func<DbConnection> create)
{
    public async Task<T> UseAsync<T>(Func<DbConnection, Task<T>> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // ODP.NET owns physical-session pooling. Keeping open connections here would retain
        // pool leases while server/schema parents wait for queued children, potentially leaving
        // every scheduler slot blocked in OpenAsync with no slot available to drain those children.
        await using DbConnection connection = create();
        await connection.OpenAsync(cancellationToken);
        await OracleConnectionFactory.InitializeAsync(connection, cancellationToken);
        return await work(connection);
    }
}
