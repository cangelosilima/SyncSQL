using System.Data.Common;
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
    /// <summary>Privileged fleet extraction sees all owners; ordinary schema credentials retain their visible scope.</summary>
    public static async Task<List<T>> QueryDictionaryAsync<T>(
        DbConnection connection, string allOwnersSql, string visibleSql,
        Func<DbDataReader, T> map, CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        try
        {
            return await QueryAsync(connection, allOwnersSql, map, cancellationToken, parameters);
        }
        catch (OracleException ex) when (ex.Number is 942 or 1031 or 41900)
        {
            return await QueryAsync(connection, visibleSql, map, cancellationToken, parameters);
        }
    }

    public static async Task<List<T>> QueryAsync<T>(
        DbConnection connection,
        string sql,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = CreateCommand(connection, sql, parameters);
        List<T> results = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(map(reader));
        }

        return results;
    }

    public static async Task<string?> ExecuteScalarStringAsync(
        DbConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = CreateCommand(connection, sql, parameters);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    public static async Task ExecuteNonQueryAsync(
        DbConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using DbCommand command = CreateCommand(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static DbCommand CreateCommand(DbConnection connection, string sql, (string Name, object Value)[] parameters)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        if (command is OracleCommand oracleCommand) { oracleCommand.BindByName = true; }
        foreach ((string name, object value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }
}
