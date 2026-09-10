using System.Text.Json;
using System.Text.Json.Serialization;

namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// samples/expected/expectations.json - what the sample fleet must contain, written by hand from the
/// upstream DDL rather than recorded from a run. Recorded numbers live in
/// <see cref="SampleBaseline"/> instead; the split matters, because a hand-written expectation
/// catches a regression while a recorded one only notices a change.
/// </summary>
public sealed record SampleExpectations
{
    [JsonPropertyName("servers")]
    public IReadOnlyList<string> Servers { get; init; } = [];

    [JsonPropertyName("groups")]
    public IReadOnlyList<ExpectedGroup> Groups { get; init; } = [];

    [JsonPropertyName("edges")]
    public IReadOnlyList<ExpectedEdge> Edges { get; init; } = [];

    public static SampleExpectations Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Expectations file not found: {path}", path);
        }

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<SampleExpectations>(stream, Options)
            ?? throw new InvalidOperationException($"Expectations file '{path}' deserialized to null.");
    }

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>One database (or one Oracle schema) and everything the benchmark expects to find in it.</summary>
public sealed record ExpectedGroup
{
    /// <summary>The samples.json id this group came from, so a failure names the sample to re-provision.</summary>
    [JsonPropertyName("sampleId")]
    public required string SampleId { get; init; }

    [JsonPropertyName("server")]
    public required string Server { get; init; }

    [JsonPropertyName("database")]
    public required string Database { get; init; }

    /// <summary>Default schema for this group's objects - Oracle groups set it once instead of on every entry.</summary>
    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    /// <summary>Object type -> the exact number of objects of that type this group must contain.</summary>
    [JsonPropertyName("exactCounts")]
    public IReadOnlyDictionary<string, int> ExactCounts { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Object type -> a floor, for types whose exact count depends on the engine release.</summary>
    [JsonPropertyName("minimumCounts")]
    public IReadOnlyDictionary<string, int> MinimumCounts { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>A floor on the whole database, used for the restored backups nobody can enumerate from source.</summary>
    [JsonPropertyName("minimumNodeCount")]
    public int? MinimumNodeCount { get; init; }

    [JsonPropertyName("objects")]
    public IReadOnlyList<ExpectedObject> Objects { get; init; } = [];

    public string Describe() => $"{SampleId} -> {Server}/{Database}{(Schema is null ? string.Empty : "/" + Schema)}";
}

/// <summary>One object the extraction must have produced.</summary>
public sealed record ExpectedObject
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Overrides the group's schema. Null on Schemas objects, which have none.</summary>
    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Fragments the extracted DDL must contain, compared case-insensitively.</summary>
    [JsonPropertyName("ddlContains")]
    public IReadOnlyList<string> DdlContains { get; init; } = [];

    /// <summary>Column names the extracted file's Columns section must list.</summary>
    [JsonPropertyName("columns")]
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>The schema this object actually lives in: its own if it has one, otherwise the group's.</summary>
    public string? ResolveSchema(ExpectedGroup group) =>
        string.Equals(Type, "Schemas", StringComparison.Ordinal) ? null : Schema ?? group.Schema;
}

/// <summary>One lineage edge the catalog must have inferred.</summary>
public sealed record ExpectedEdge
{
    [JsonPropertyName("sampleId")]
    public required string SampleId { get; init; }

    [JsonPropertyName("from")]
    public required string From { get; init; }

    [JsonPropertyName("to")]
    public required string To { get; init; }
}
