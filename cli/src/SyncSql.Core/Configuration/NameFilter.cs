using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SyncSql.Core.Configuration;

/// <summary>
/// A regex include/exclude filter, as used for config.defaults/server overrides' databases, schemas,
/// and objectNames blocks. Exclude always wins; a missing or empty include list means "include everything" -
/// see <see cref="IsAllowed"/>, a direct port of SyncSql.Common.psm1's Test-SyncSqlNameAllowed.
/// </summary>
public sealed record NameFilter
{
    [JsonPropertyName("include")]
    public IReadOnlyList<string> Include { get; init; } = [];

    [JsonPropertyName("exclude")]
    public IReadOnlyList<string> Exclude { get; init; } = [];

    public bool IsAllowed(string name)
    {
        foreach (string pattern in Exclude)
        {
            if (Regex.IsMatch(name, pattern))
            {
                return false;
            }
        }

        if (Include.Count == 0)
        {
            return true;
        }

        foreach (string pattern in Include)
        {
            if (Regex.IsMatch(name, pattern))
            {
                return true;
            }
        }

        return false;
    }
}
