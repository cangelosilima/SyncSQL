using Oracle.ManagedDataAccess.Client;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;

namespace SyncSql.Extraction.Oracle;

internal static class OracleConnectionFactory
{
    public static async Task<OracleConnection> OpenAsync(ServerConfig server, DatabaseCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(server.ServiceName))
        {
            throw new InvalidOperationException($"Oracle server '{server.Name}' is missing required key 'serviceName'.");
        }

        string connectionString =
            $"User Id={credentials.Username};Password={credentials.Password};Data Source={server.Host}:{server.EffectivePort}/{server.ServiceName};";

        OracleConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Trim noisy, environment-specific clauses so re-runs produce clean diffs.
        await using OracleCommand prep = connection.CreateCommand();
        prep.CommandText = """
            BEGIN
              DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'STORAGE', FALSE);
              DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'SEGMENT_ATTRIBUTES', FALSE);
              DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'SQLTERMINATOR', TRUE);
              DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'PRETTY', TRUE);
            END;
            """;
        await prep.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }
}
