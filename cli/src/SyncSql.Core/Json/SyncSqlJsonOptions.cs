using System.Text.Json;
using System.Text.Json.Serialization;

namespace SyncSql.Core.Json;

/// <summary>
/// Shared System.Text.Json options for everything this solution reads/writes as JSON (catalog.json,
/// config/servers.json, metrics snapshot/history files). Every domain type already carries explicit
/// [JsonPropertyName] attributes matching site/src/types.ts exactly, so this doesn't need (and
/// deliberately doesn't set) a naming policy - explicit beats inferred for a wire format another
/// project (the React site) depends on byte-for-byte.
/// </summary>
public static class SyncSqlJsonOptions
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public static JsonSerializerOptions Indented { get; } = new(Default) { WriteIndented = true };
}
