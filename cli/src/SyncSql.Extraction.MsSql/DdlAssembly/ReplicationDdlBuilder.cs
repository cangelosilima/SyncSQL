namespace SyncSql.Extraction.MsSql.DdlAssembly;

/// <summary>
/// Best-effort, informational-only publication/article listing - a direct port of
/// Get-SyncSqlMsSqlReplication's foreach body. Subscriber enumeration is deliberately out of scope
/// (subscription table shapes vary too much across SQL Server versions/topologies).
/// </summary>
internal static class ReplicationDdlBuilder
{
    public static string Build(string publicationName, string? description, string? articlesCsv)
    {
        List<string> lines = [$"-- Publication: {publicationName}"];

        if (!string.IsNullOrWhiteSpace(description))
        {
            lines.Add($"-- Description: {description}");
        }

        lines.Add("-- Articles (replicated objects):");
        if (string.IsNullOrWhiteSpace(articlesCsv))
        {
            lines.Add("--   (none)");
        }
        else
        {
            foreach (string article in articlesCsv.Split(", "))
            {
                lines.Add($"--   - {article}");
            }
        }

        lines.Add("-- Subscribers are not enumerated - see Replication Monitor for current subscription state.");
        return string.Join('\n', lines);
    }
}
