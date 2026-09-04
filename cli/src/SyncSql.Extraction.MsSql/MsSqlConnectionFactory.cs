using Microsoft.Data.SqlClient;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Configuration;

namespace SyncSql.Extraction.MsSql;

internal static class MsSqlConnectionFactory
{
    public static async Task<SqlConnection> OpenAsync(ServerConfig server, string database, DatabaseCredentials credentials, CancellationToken cancellationToken)
    {
        // A named instance addresses itself ("HOST\INSTANCE"), and the SQL Browser picks the port -
        // appending the default 1433 to one would point at whatever else happens to listen there. An
        // explicitly configured port still wins, so "HOST\INSTANCE" with a fixed port keeps working.
        string dataSource = server.Port is null && server.Host.Contains('\\', StringComparison.Ordinal)
            ? server.Host
            : $"{server.Host},{server.EffectivePort}";

        SqlConnectionStringBuilder builder = new()
        {
            DataSource = dataSource,
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
