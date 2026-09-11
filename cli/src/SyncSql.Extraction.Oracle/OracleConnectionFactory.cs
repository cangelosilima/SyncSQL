using Oracle.ManagedDataAccess.Client;
using System.Data.Common;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;

namespace SyncSql.Extraction.Oracle;

internal static class OracleConnectionFactory
{
    public static OracleConnection Create(ServerConfig server, DatabaseCredentials credentials)
    {
        if (string.IsNullOrWhiteSpace(server.ServiceName))
        {
            throw new InvalidOperationException($"Oracle server '{server.Name}' is missing required key 'serviceName'.");
        }

        OracleConnectionStringBuilder builder = new()
        {
            UserID = credentials.Username,
            Password = credentials.Password,
            DataSource = $"{server.Host}:{server.EffectivePort}/{server.ServiceName}",
        };
        return new OracleConnection(builder.ConnectionString);
    }

    // Trim noisy, environment-specific clauses so re-runs produce clean diffs.
    public static Task InitializeAsync(DbConnection connection, CancellationToken cancellationToken) =>
        OracleCommandRunner.ExecuteNonQueryAsync(connection, """
            BEGIN
              DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'STORAGE', FALSE);
              DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'SEGMENT_ATTRIBUTES', FALSE);
              DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'SQLTERMINATOR', TRUE);
              DBMS_METADATA.SET_TRANSFORM_PARAM(DBMS_METADATA.SESSION_TRANSFORM, 'PRETTY', TRUE);
            END;
            """, cancellationToken);
}
