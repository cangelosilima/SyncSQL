using Oracle.ManagedDataAccess.Client;

namespace SyncSql.Extraction.Oracle;

/// <summary>
/// Runs a query/scalar with BindByName explicitly set. ODP.NET's OracleCommand.BindByName defaults to
/// false - parameters then bind by ORDINAL POSITION in the order added, not by the ":name" placeholders
/// in the SQL text - a well-known Oracle ADO.NET footgun. Always setting it true here means every query
/// below can freely pass named parameters in any order without silently mis-binding them.
/// </summary>
internal static class OracleCommandRunner
{
    public static async Task<List<T>> QueryAsync<T>(
        OracleConnection connection,
        string sql,
        Func<OracleDataReader, T> map,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using OracleCommand command = CreateCommand(connection, sql, parameters);
        List<T> results = [];
        await using OracleDataReader reader = (OracleDataReader)await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(map(reader));
        }

        return results;
    }

    public static async Task<string?> ExecuteScalarStringAsync(
        OracleConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using OracleCommand command = CreateCommand(connection, sql, parameters);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public static async Task ExecuteNonQueryAsync(
        OracleConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using OracleCommand command = CreateCommand(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static OracleCommand CreateCommand(OracleConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        OracleCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.BindByName = true;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.Add(name, value);
        }

        return command;
    }
}
