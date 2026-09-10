using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SyncSql.Core.Domain;

namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// samples/expected/baseline.json - the recorded side of the benchmark: totals nobody can derive from
/// the upstream sources (a restored backup is a binary), so they are captured from a known-good run
/// and compared on every run after that.
/// </summary>
public sealed record SampleBaseline
{
    [JsonPropertyName("nodeCount")]
    public required int NodeCount { get; init; }

    [JsonPropertyName("edgeCount")]
    public required int EdgeCount { get; init; }

    [JsonPropertyName("typeCounts")]
    public required IReadOnlyDictionary<string, int> TypeCounts { get; init; }

    /// <summary>"&lt;server&gt;/&lt;database&gt;" -> how many objects were catalogued there.</summary>
    [JsonPropertyName("objectsPerDatabase")]
    public required IReadOnlyDictionary<string, int> ObjectsPerDatabase { get; init; }

    public static SampleBaseline Capture(Catalog catalog) => new()
    {
        NodeCount = catalog.Nodes.Count,
        EdgeCount = catalog.Edges.Count,
        TypeCounts = catalog.Nodes
            .GroupBy(node => node.Type, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
        ObjectsPerDatabase = catalog.Nodes
            .GroupBy(node => $"{node.Server}/{node.Database}", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
    };

    public static SampleBaseline? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<SampleBaseline>(stream, SerializerOptions);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions) + Environment.NewLine);
    }

    /// <summary>
    /// Differences against <paramref name="other"/>, worded from the point of view of "what changed
    /// since the baseline was recorded". Empty means identical.
    /// </summary>
    public IReadOnlyList<string> DiffAgainst(SampleBaseline other)
    {
        List<string> differences = [];

        if (NodeCount != other.NodeCount)
        {
            differences.Add($"total objects: baseline {other.NodeCount}, now {NodeCount}");
        }
        if (EdgeCount != other.EdgeCount)
        {
            differences.Add($"total lineage edges: baseline {other.EdgeCount}, now {EdgeCount}");
        }

        CompareCounts("type", TypeCounts, other.TypeCounts, differences);
        CompareCounts("database", ObjectsPerDatabase, other.ObjectsPerDatabase, differences);

        return differences;
    }

    private static void CompareCounts(
        string label,
        IReadOnlyDictionary<string, int> current,
        IReadOnlyDictionary<string, int> baseline,
        List<string> differences)
    {
        foreach (string key in current.Keys.Union(baseline.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            baseline.TryGetValue(key, out int was);
            current.TryGetValue(key, out int now);
            if (was != now)
            {
                differences.Add($"{label} '{key}': baseline {was}, now {now}");
            }
        }
    }

    public string Describe()
    {
        StringBuilder builder = new();
        builder.AppendLine($"  objects : {NodeCount}");
        builder.AppendLine($"  edges   : {EdgeCount}");
        foreach ((string database, int count) in ObjectsPerDatabase)
        {
            builder.AppendLine($"  {database}: {count}");
        }
        return builder.ToString();
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
