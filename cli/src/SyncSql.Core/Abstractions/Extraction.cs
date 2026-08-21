using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Abstractions;

/// <summary>Resolved DB credentials for one server, read from "&lt;prefix&gt;_DB_USER"/"&lt;prefix&gt;_DB_PASSWORD" - never stored in config.</summary>
public sealed record DatabaseCredentials(string Username, string Password);

/// <summary>Resolves <see cref="DatabaseCredentials"/> from the process environment given a server's credentialsVariablePrefix.</summary>
public interface ICredentialProvider
{
    DatabaseCredentials Resolve(string credentialsVariablePrefix);
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
    DatabaseEngine Engine { get; }

    Task<ExtractionOutcome> ExtractAsync(
        ServerConfig server,
        EffectiveFilters filters,
        ExtractionOptions options,
        CancellationToken cancellationToken);
}

/// <summary>Runtime dispatch from a server's configured engine to the matching <see cref="IDatabaseObjectExtractor"/> - implemented in SyncSql.Cli via keyed DI, keeping Core free of a DI container reference.</summary>
public interface IDatabaseObjectExtractorResolver
{
    IDatabaseObjectExtractor Resolve(DatabaseEngine engine);
}
