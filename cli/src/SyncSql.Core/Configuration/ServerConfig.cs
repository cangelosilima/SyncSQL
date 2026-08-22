using System.Text.Json.Serialization;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Configuration;

/// <summary>config.defaults - the fallback databases/schemas/objectNames/objectTypes filters a server inherits unless it overrides a given key.</summary>
public sealed record ObjectFilterSet
{
    [JsonPropertyName("databases")]
    public NameFilter? Databases { get; init; }

    [JsonPropertyName("schemas")]
    public NameFilter? Schemas { get; init; }

    [JsonPropertyName("objectNames")]
    public NameFilter? ObjectNames { get; init; }

    [JsonPropertyName("objectTypes")]
    public IReadOnlyList<string>? ObjectTypes { get; init; }
}

/// <summary>One entry in config.servers[]. A key present here fully replaces (not merges with) the corresponding config.defaults key - see <see cref="EffectiveFilters.Resolve"/>.</summary>
public sealed record ServerConfig
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public required DatabaseEngine Type { get; init; }

    [JsonPropertyName("host")]
    public required string Host { get; init; }

    [JsonPropertyName("port")]
    public int? Port { get; init; }

    /// <summary>MSSQL only.</summary>
    [JsonPropertyName("encrypt")]
    public bool? Encrypt { get; init; }

    /// <summary>MSSQL only.</summary>
    [JsonPropertyName("trustServerCertificate")]
    public bool? TrustServerCertificate { get; init; }

    /// <summary>Oracle only - the service name (DBMS_METADATA connect string target).</summary>
    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; init; }

    /// <summary>Credentials are read from "&lt;prefix&gt;_DB_USER"/"&lt;prefix&gt;_DB_PASSWORD" environment variables - never stored in config.</summary>
    [JsonPropertyName("credentialsVariablePrefix")]
    public required string CredentialsVariablePrefix { get; init; }

    [JsonPropertyName("databases")]
    public NameFilter? Databases { get; init; }

    [JsonPropertyName("schemas")]
    public NameFilter? Schemas { get; init; }

    [JsonPropertyName("objectNames")]
    public NameFilter? ObjectNames { get; init; }

    [JsonPropertyName("objectTypes")]
    public IReadOnlyList<string>? ObjectTypes { get; init; }

    public int EffectivePort => Port ?? Type switch
    {
        DatabaseEngine.MsSql => 1433,
        DatabaseEngine.Oracle => 1521,
        _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, "Unknown database engine."),
    };
}
