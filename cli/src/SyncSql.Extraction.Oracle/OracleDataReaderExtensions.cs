using System.Data.Common;
using System.Globalization;


namespace SyncSql.Extraction.Oracle;

/// <summary>
/// Ordinal-based (not name-overload-based, to avoid assuming OracleDataReader has string-keyed
/// convenience accessors beyond the base IDataRecord contract) nullable column readers.
/// </summary>
internal static class OracleDataReaderExtensions
{
    public static string GetStringOrEmpty(this DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    public static string? GetNullableString(this DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    /// Numeric columns come back as whatever Oracle's provider decided to box (OracleDecimal, decimal,
    /// long, ...), so the conversion goes through Convert - explicitly invariant, because the value is a
    /// machine number from a catalog view, not something formatted for a human in the runner's locale.
    /// </summary>
    public static long? GetNullableInt64(this DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    public static DateTime? GetNullableDateTime(this DbDataReader reader, string column)
    {
        int ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
