using System.Text.Json.Serialization;

namespace SyncSql.Core.Configuration;

/// <summary>The full config/servers.json document.</summary>
public sealed record SyncSqlConfig
{
    [JsonPropertyName("git")]
    public GitConfig Git { get; init; } = new();

    [JsonPropertyName("defaults")]
    public ObjectFilterSet? Defaults { get; init; }

    [JsonPropertyName("serverSelection")]
    public NameFilter ServerSelection { get; init; } = new();

    [JsonPropertyName("servers")]
    public required IReadOnlyList<ServerConfig> Servers { get; init; }
}
