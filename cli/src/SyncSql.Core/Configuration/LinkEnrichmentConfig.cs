using System.Text.Json.Serialization;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Configuration;

/// <summary>Files copied from the source Oracle/gateway hosts; paths are relative to servers.json.</summary>
public sealed record OracleNetworkConfig
{
    [JsonPropertyName("tnsNamesFile")] public string? TnsNamesFile { get; init; }
    [JsonPropertyName("gateways")] public IReadOnlyList<GatewayConfig> Gateways { get; init; } = [];
}

public sealed record GatewayConfig
{
    [JsonPropertyName("sid")] public required string Sid { get; init; }
    [JsonPropertyName("host")] public string? Host { get; init; }
    [JsonPropertyName("initFile")] public required string InitFile { get; init; }
    [JsonPropertyName("odbcIniFile")] public string? OdbcIniFile { get; init; }
    [JsonPropertyName("targetEngine")] public DatabaseEngine TargetEngine { get; init; } = DatabaseEngine.MsSql;
}

/// <summary>An explicit destination when network configuration is unavailable. Scoped to the source server and link owner.</summary>
public sealed record LinkTargetConfig
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("owner")] public string? Owner { get; init; }
    [JsonPropertyName("targetEngine")] public DatabaseEngine? TargetEngine { get; init; }
    [JsonPropertyName("dataSource")] public string? DataSource { get; init; }
    [JsonPropertyName("database")] public string? Database { get; init; }
    [JsonPropertyName("defaultSchema")] public string? DefaultSchema { get; init; }
}
