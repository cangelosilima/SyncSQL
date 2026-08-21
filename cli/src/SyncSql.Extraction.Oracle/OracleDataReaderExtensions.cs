using Oracle.ManagedDataAccess.Client;

namespace SyncSql.Extraction.Oracle;

/// <summary>
/// Ordinal-based (not name-overload-based, to avoid assuming OracleDataReader has string-keyed
/// convenience accessors beyond the base IDataRecord contract) nullable column readers.
/// </summary>
internal static class OracleDataReaderExtensions
{
    public static string GetStringOrEmpty(this OracleDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    public static string? GetNullableString(this OracleDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static long? GetNullableInt64(this OracleDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal));
    }

    public static DateTime? GetNullableDateTime(this OracleDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
