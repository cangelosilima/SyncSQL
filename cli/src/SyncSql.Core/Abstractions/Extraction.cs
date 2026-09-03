using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Abstractions;

/// <summary>Resolved DB credentials for one server - supplied as CLI parameters, a credentials file, or environment variables, never stored in config.</summary>
public sealed record DatabaseCredentials(string Username, string Password);

/// <summary>
/// Whatever credential halves one source knows for a server's credentialsVariablePrefix - neither, one,
/// or both. Halves are tracked separately so sources can be layered: a username passed as a CLI
/// parameter can be completed by a password that only the environment (or a credentials file) has.
/// </summary>
public readonly record struct PartialCredentials(string? Username, string? Password)
{
    /// <summary>Nothing known about this prefix.</summary>
    public static PartialCredentials None => default;

    public bool IsComplete => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);
}

/// <summary>
/// One place credentials can come from, given a server's credentialsVariablePrefix. Implementations live
/// in <c>SyncSql.Core.Credentials</c>: CLI parameters, a credentials file, the process environment, and
/// the layered provider that stacks them in precedence order.
/// </summary>
public interface ICredentialProvider
{
    /// <summary>Reads whichever halves this source has for the prefix, without failing when it has none.</summary>
    public PartialCredentials Read(string credentialsVariablePrefix);

    /// <summary>Where this source looks, phrased for the "missing credentials" error message.</summary>
    public string Describe(string credentialsVariablePrefix);
}

public static class CredentialProviderExtensions
{
    /// <summary>Reads both halves, throwing an <see cref="InvalidOperationException"/> naming every place looked when either is missing.</summary>
    public static DatabaseCredentials Resolve(this ICredentialProvider provider, string credentialsVariablePrefix)
    {
        ArgumentNullException.ThrowIfNull(provider);

        PartialCredentials credentials = provider.Read(credentialsVariablePrefix);
        return credentials.IsComplete
            ? new DatabaseCredentials(credentials.Username!, credentials.Password!)
            : throw new InvalidOperationException(
                $"Missing credentials for '{credentialsVariablePrefix}': a username and a password are both required. Looked in {provider.Describe(credentialsVariablePrefix)}.");
    }
}

public sealed record ExtractionOptions
{
    public required DatabaseCredentials Credentials { get; init; }

    /// <summary>Whether to also capture a volatile metrics snapshot per table (row counts, index fragmentation/usage, optimizer statistics) - see MetricsSnapshot.</summary>
    public bool CaptureMetrics { get; init; } = true;
}

/// <summary>
/// Extracts every allowed object from one server into <see cref="ExtractedObject"/>s, one implementation
/// per <see cref="DatabaseEngine"/>. Implementations own connecting, running the engine-specific catalog
/// queries, and assembling ready-to-diff DDL text - nothing about filtering, lineage, or catalog assembly.
/// </summary>
public interface IDatabaseObjectExtractor
{
    public DatabaseEngine Engine { get; }

    public Task<ExtractionOutcome> ExtractAsync(
        ServerConfig server,
        EffectiveFilters filters,
        ExtractionOptions options,
        CancellationToken cancellationToken);
}

/// <summary>Runtime dispatch from a server's configured engine to the matching <see cref="IDatabaseObjectExtractor"/> - implemented in SyncSql.Cli via keyed DI, keeping Core free of a DI container reference.</summary>
public interface IDatabaseObjectExtractorResolver
{
    public IDatabaseObjectExtractor Resolve(DatabaseEngine engine);
}
