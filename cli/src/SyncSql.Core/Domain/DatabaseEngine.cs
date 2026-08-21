using System.Text.Json.Serialization;
using SyncSql.Core.Json;

namespace SyncSql.Core.Domain;

/// <summary>Which database engine produced an extracted object, config entry, or lineage result.</summary>
[JsonConverter(typeof(DatabaseEngineJsonConverter))]
public enum DatabaseEngine
{
    MsSql,
    Oracle,
}

/// <summary>
/// Converts between <see cref="DatabaseEngine"/> and the lowercase "mssql"/"oracle" tokens used in
/// config/servers.json's server.type field and the extracted object header's "-- Engine:" line.
/// </summary>
public static class DatabaseEngineNames
{
    public const string MsSql = "mssql";
    public const string Oracle = "oracle";

    public static string ToConfigString(this DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.MsSql => MsSql,
        DatabaseEngine.Oracle => Oracle,
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, "Unknown database engine."),
    };

    public static bool TryParse(string? value, out DatabaseEngine engine)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case MsSql:
                engine = DatabaseEngine.MsSql;
                return true;
            case Oracle:
                engine = DatabaseEngine.Oracle;
                return true;
            default:
                engine = default;
                return false;
        }
    }

    public static DatabaseEngine Parse(string value)
    {
        if (TryParse(value, out DatabaseEngine engine))
        {
            return engine;
        }

        throw new FormatException($"Unknown database engine '{value}' - expected '{MsSql}' or '{Oracle}'.");
    }
}
