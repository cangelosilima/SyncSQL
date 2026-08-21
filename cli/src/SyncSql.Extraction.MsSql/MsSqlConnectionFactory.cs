using Microsoft.Data.SqlClient;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;

namespace SyncSql.Extraction.MsSql;

internal static class MsSqlConnectionFactory
{
    public static async Task<SqlConnection> OpenAsync(ServerConfig server, string database, DatabaseCredentials credentials, CancellationToken cancellationToken)
    {
        SqlConnectionStringBuilder builder = new()
        {
            DataSource = $"{server.Host},{server.EffectivePort}",
            InitialCatalog = database,
            UserID = credentials.Username,
            Password = credentials.Password,
            Encrypt = server.Encrypt ?? true,
            TrustServerCertificate = server.TrustServerCertificate ?? false,
            ConnectTimeout = 30,
        };

        SqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
