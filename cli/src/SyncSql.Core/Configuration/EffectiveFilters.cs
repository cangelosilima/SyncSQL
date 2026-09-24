namespace SyncSql.Core.Configuration;

using SyncSql.Core.Domain;

/// <summary>
/// A server's fully-resolved filters (config.defaults merged with that server's own overrides, key by
/// key - a server that specifies a key fully replaces the default for that key). Managed engine
/// exclusions are added after those overrides when UseDefaultExclusions is enabled.
/// </summary>
public sealed record EffectiveFilters
{
    private static readonly NameFilter AllowAll = new();

    public bool UseDefaultExclusions { get; init; } = true;

    public required NameFilter Databases { get; init; }
    public required NameFilter Schemas { get; init; }
    public required NameFilter ObjectNames { get; init; }
    public required IReadOnlyList<string> ObjectTypes { get; init; }

    public static EffectiveFilters Resolve(ObjectFilterSet? defaults, ServerConfig server)
    {
        bool useDefaults = server.UseDefaultExclusions ?? defaults?.UseDefaultExclusions ?? true;
        EffectiveFilters filters = new()
        {
            UseDefaultExclusions = useDefaults,
            Databases = server.Databases ?? defaults?.Databases ?? AllowAll,
            Schemas = server.Schemas ?? defaults?.Schemas ?? AllowAll,
            ObjectNames = server.ObjectNames ?? defaults?.ObjectNames ?? AllowAll,
            ObjectTypes = server.ObjectTypes ?? defaults?.ObjectTypes ?? [],
        };
        if (!useDefaults) { return filters; }

        return server.Type == DatabaseEngine.MsSql
            ? filters with
            {
                Databases = Excluding(filters.Databases, "(?i)^(master|model|msdb|tempdb)$"),
                Schemas = Excluding(filters.Schemas, "(?i)^(sys|INFORMATION_SCHEMA)$"),
            }
            : filters with
            {
                // Also protects older Oracle releases without ORACLE_MAINTAINED metadata.
                // PUBLIC and application prefixes such as SYS_ and TMP_ are deliberately allowed.
                Schemas = Excluding(filters.Schemas,
                    "(?i)^(SYS|SYSTEM|OUTLN|DBSNMP|AUDSYS|XDB|CTXSYS|MDSYS|WMSYS|ORDSYS|ORDDATA|ORDPLUGINS|OLAPSYS|OJVMSYS|EXFSYS|SI_INFORMTN_SCHEMA|APPQOSSYS|DBSFWUSER|DVSYS|DVF|LBACSYS|GSMADMIN_INTERNAL|ANONYMOUS|APEX_[0-9]+|FLOWS_[0-9]+|FLOWS_FILES)$"),
                ObjectNames = Excluding(filters.ObjectNames, @"(?i)^BIN\$"),
            };
    }

    private static NameFilter Excluding(NameFilter filter, string pattern) =>
        filter with { Exclude = [.. filter.Exclude, pattern] };
}
