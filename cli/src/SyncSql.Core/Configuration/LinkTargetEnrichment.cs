using SyncSql.Core.Domain;

namespace SyncSql.Core.Configuration;

public static class LinkTargetEnrichment
{
    public static LinkMetadata Apply(ServerConfig server, string name, string? owner, LinkMetadata metadata)
    {
        LinkTargetConfig[] exact = [.. server.LinkTargets.Where(target => target.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Owner, owner, StringComparison.OrdinalIgnoreCase))];
        LinkTargetConfig[] matches = exact.Length > 0 ? exact : [.. server.LinkTargets.Where(target =>
            target.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && target.Owner is null)];
        if (matches.Length != 1) { return metadata; }
        LinkTargetConfig target = matches[0];
        string? dataSource = NonBlank(target.DataSource);
        string? database = NonBlank(target.Database);
        string? defaultSchema = NonBlank(target.DefaultSchema);
        List<LinkEvidence> evidence = [.. metadata.Evidence];
        string source = $"config:linkTargets/{owner}/{name}";
        foreach ((string field, string? value) in new[] { ("dataSource", dataSource), ("database", database),
            ("defaultSchema", defaultSchema), ("targetEngine", target.TargetEngine?.ToConfigString()) })
        {
            if (!string.IsNullOrWhiteSpace(value)) { evidence.Add(new(field, value, source)); }
        }
        return metadata with
        {
            TargetEngine = target.TargetEngine ?? metadata.TargetEngine,
            DataSource = dataSource ?? metadata.DataSource,
            Database = database ?? metadata.Database,
            DefaultSchema = defaultSchema ?? metadata.DefaultSchema,
            Evidence = evidence,
        };
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
