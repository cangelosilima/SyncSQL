namespace SyncSql.Core.Configuration;

/// <summary>
/// A server's fully-resolved filters (config.defaults merged with that server's own overrides, key by
/// key - a server that specifies a key fully replaces the default for that key). A direct port of
/// SyncSql.Common.psm1's Get-SyncSqlEffectiveFilters + Get-SyncSqlAllowedObjectTypes.
/// </summary>
public sealed record EffectiveFilters
{
    private static readonly NameFilter AllowAll = new();

    public required NameFilter Databases { get; init; }
    public required NameFilter Schemas { get; init; }
    public required NameFilter ObjectNames { get; init; }
    public required IReadOnlyList<string> ObjectTypes { get; init; }

    public static EffectiveFilters Resolve(ObjectFilterSet? defaults, ServerConfig server) => new()
    {
        Databases = server.Databases ?? defaults?.Databases ?? AllowAll,
        Schemas = server.Schemas ?? defaults?.Schemas ?? AllowAll,
        ObjectNames = server.ObjectNames ?? defaults?.ObjectNames ?? AllowAll,
        ObjectTypes = server.ObjectTypes ?? defaults?.ObjectTypes ?? [],
    };
}
