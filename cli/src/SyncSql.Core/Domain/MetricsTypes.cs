using System.Text.Json.Serialization;

namespace SyncSql.Core.Domain;

/// <summary>
/// One index's metrics. MSSQL populates fragmentation/page count (sys.dm_db_index_physical_stats) and
/// seeks/scans/lookups/updates (sys.dm_db_index_usage_stats); Oracle populates row count/distinct keys/leaf
/// blocks (ALL_IND_STATISTICS). Whichever engine didn't capture a given field simply leaves it null.
/// </summary>
public sealed record CatalogIndexMetric
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("fragmentationPct")]
    public double? FragmentationPct { get; init; }

    [JsonPropertyName("pageCount")]
    public long? PageCount { get; init; }

    [JsonPropertyName("seeks")]
    public long? Seeks { get; init; }

    [JsonPropertyName("scans")]
    public long? Scans { get; init; }

    [JsonPropertyName("lookups")]
    public long? Lookups { get; init; }

    [JsonPropertyName("updates")]
    public long? Updates { get; init; }

    [JsonPropertyName("rowCount")]
    public long? RowCount { get; init; }

    [JsonPropertyName("distinctKeys")]
    public long? DistinctKeys { get; init; }

    [JsonPropertyName("leafBlocks")]
    public long? LeafBlocks { get; init; }

    [JsonPropertyName("lastAnalyzed")]
    public DateTimeOffset? LastAnalyzed { get; init; }
}

/// <summary>The optimizer statistics actually consulted for cardinality estimation - not the CREATE STATISTICS object definition.</summary>
public sealed record CatalogStatMetric
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("rows")]
    public long? Rows { get; init; }

    [JsonPropertyName("rowsSampled")]
    public long? RowsSampled { get; init; }

    /// <summary>Histogram step count - MSSQL only, null for Oracle.</summary>
    [JsonPropertyName("steps")]
    public int? Steps { get; init; }

    [JsonPropertyName("modificationCounter")]
    public long? ModificationCounter { get; init; }

    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset? LastUpdated { get; init; }
}

/// <summary>
/// One run's volatile operational snapshot for a table - row count/size, index metrics, optimizer
/// statistics. Kept entirely out of the object's own DDL/version history (see SyncSql.Catalog); a
/// growing array of these, oldest first, is what accumulates into a table's metrics history file and
/// ultimately node.metrics in catalog.json.
/// </summary>
public sealed record MetricsSnapshot
{
    [JsonPropertyName("capturedAt")]
    public required DateTimeOffset CapturedAt { get; init; }

    [JsonPropertyName("rowCount")]
    public long? RowCount { get; init; }

    [JsonPropertyName("reservedKB")]
    public long? ReservedKB { get; init; }

    [JsonPropertyName("dataKB")]
    public long? DataKB { get; init; }

    [JsonPropertyName("indexKB")]
    public long? IndexKB { get; init; }

    [JsonPropertyName("indexes")]
    public IReadOnlyList<CatalogIndexMetric> Indexes { get; init; } = [];

    [JsonPropertyName("statistics")]
    public IReadOnlyList<CatalogStatMetric> Statistics { get; init; } = [];
}
