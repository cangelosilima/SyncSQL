using Microsoft.Extensions.DependencyInjection;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Cli.Composition;

/// <summary>
/// Resolves an <see cref="IDatabaseObjectExtractor"/>/<see cref="ILineageAnalyzer"/> at runtime from an
/// object's/server's <see cref="DatabaseEngine"/> via keyed DI - the only place in the whole solution
/// that knows both the abstraction (Core) and the concrete per-engine registrations (this composition
/// root), which is what lets SyncSql.Catalog dispatch per engine without referencing
/// SyncSql.Extraction.*/SyncSql.Lineage.* directly (see Directory.Build.props' layering notes / the
/// architecture section of README).
/// </summary>
internal sealed class DatabaseObjectExtractorResolver(IServiceProvider services) : IDatabaseObjectExtractorResolver
{
    public IDatabaseObjectExtractor Resolve(DatabaseEngine engine) =>
        services.GetKeyedService<IDatabaseObjectExtractor>(engine)
        ?? throw new InvalidOperationException($"No IDatabaseObjectExtractor registered for engine '{engine}'.");
}

internal sealed class LineageAnalyzerResolver(IServiceProvider services) : ILineageAnalyzerResolver
{
    public ILineageAnalyzer Resolve(DatabaseEngine engine) =>
        services.GetKeyedService<ILineageAnalyzer>(engine)
        ?? throw new InvalidOperationException($"No ILineageAnalyzer registered for engine '{engine}'.");
}
