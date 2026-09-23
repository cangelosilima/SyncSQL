using System.Text.Json.Serialization;

namespace SyncSql.Core.Domain;

/// <summary>Observable connection facts. Credentials are never part of the export.</summary>
public sealed record LinkMetadata
{
    [JsonPropertyName("connectIdentifier")] public string? ConnectIdentifier { get; init; }
    [JsonPropertyName("targetEngine")] public DatabaseEngine? TargetEngine { get; init; }
    [JsonPropertyName("dataSource")] public string? DataSource { get; init; }
    [JsonPropertyName("database")] public string? Database { get; init; }
    [JsonPropertyName("defaultSchema")] public string? DefaultSchema { get; init; }
    [JsonPropertyName("gatewayHost")] public string? GatewayHost { get; init; }
    [JsonPropertyName("gatewaySid")] public string? GatewaySid { get; init; }
    [JsonPropertyName("logins")] public IReadOnlyList<LinkLogin> Logins { get; init; } = [];
    [JsonPropertyName("passwordStatus")] public string PasswordStatus { get; init; } = "not-extracted";
    [JsonPropertyName("evidence")] public IReadOnlyList<LinkEvidence> Evidence { get; init; } = [];
    [JsonPropertyName("diagnostics")] public IReadOnlyList<string> Diagnostics { get; init; } = [];
    // Filled by catalog assembly, independently of whether extraction could traverse the link.
    [JsonPropertyName("targetServer")] public string? TargetServer { get; init; }
    [JsonPropertyName("loginNodeIds")] public IReadOnlyList<string> LoginNodeIds { get; init; } = [];
}

public sealed record LinkLogin(
    [property: JsonPropertyName("remoteUser")] string? RemoteUser,
    [property: JsonPropertyName("localUser")] string? LocalUser = null,
    [property: JsonPropertyName("usesSelf")] bool UsesSelf = false);

public sealed record LinkEvidence(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("source")] string Source);

/// <summary>Login and database-user context, without passwords or password hashes.</summary>
public sealed record PrincipalMetadata
{
    [JsonPropertyName("login")] public string? Login { get; init; }
    [JsonPropertyName("defaultDatabase")] public string? DefaultDatabase { get; init; }
    [JsonPropertyName("defaultSchema")] public string? DefaultSchema { get; init; }
}
