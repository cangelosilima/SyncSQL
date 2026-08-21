using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Core.Json;

namespace SyncSql.Catalog.Tests;

public sealed class MetricsHistoryStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("syncsql-metrics-").FullName;
    private readonly MetricsHistoryStore _store = new(NullLogger<MetricsHistoryStore>.Instance);

    private string SnapshotRoot => Path.Combine(_root, "snapshot");
    private string HistoryRoot => Path.Combine(_root, "history");

    private static MetricsSnapshot Snapshot(DateTimeOffset capturedAt) => new()
    {
        CapturedAt = capturedAt,
        RowCount = 1,
    };

    private void WriteSnapshot(string objectId, MetricsSnapshot snapshot)
    {
        string path = Path.Combine(SnapshotRoot, objectId.Replace('/', Path.DirectorySeparatorChar) + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, SyncSqlJsonOptions.Default));
    }

    [Fact]
    public async Task UpdateAsync_NoSnapshotRoot_ReturnsZeroWithoutThrowing()
    {
        int updated = await _store.UpdateAsync(new MetricsHistoryUpdateRequest
        {
            SnapshotRoot = SnapshotRoot,
            HistoryRoot = HistoryRoot,
        }, CancellationToken.None);

        Assert.Equal(0, updated);
    }

    [Fact]
    public async Task UpdateAsync_NewObject_CreatesHistoryFileWithOneEntry()
    {
        WriteSnapshot("srv/db/Tables/dbo/Orders", Snapshot(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));

        int updated = await _store.UpdateAsync(new MetricsHistoryUpdateRequest
        {
            SnapshotRoot = SnapshotRoot,
            HistoryRoot = HistoryRoot,
        }, CancellationToken.None);

        Assert.Equal(1, updated);
        IReadOnlyList<MetricsSnapshot> history = await _store.LoadHistoryAsync(HistoryRoot, "srv/db/Tables/dbo/Orders", CancellationToken.None);
        Assert.Single(history);
    }

    [Fact]
    public async Task UpdateAsync_ExistingHistory_AppendsRatherThanOverwrites()
    {
        WriteSnapshot("srv/db/Tables/dbo/Orders", Snapshot(DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        await _store.UpdateAsync(new MetricsHistoryUpdateRequest { SnapshotRoot = SnapshotRoot, HistoryRoot = HistoryRoot }, CancellationToken.None);

        WriteSnapshot("srv/db/Tables/dbo/Orders", Snapshot(DateTimeOffset.Parse("2026-01-02T00:00:00Z")));
        await _store.UpdateAsync(new MetricsHistoryUpdateRequest { SnapshotRoot = SnapshotRoot, HistoryRoot = HistoryRoot }, CancellationToken.None);

        IReadOnlyList<MetricsSnapshot> history = await _store.LoadHistoryAsync(HistoryRoot, "srv/db/Tables/dbo/Orders", CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), history[0].CapturedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-01-02T00:00:00Z"), history[1].CapturedAt);
    }

    [Fact]
    public async Task UpdateAsync_HistoryLimitExceeded_TrimsOldestFirst()
    {
        for (int day = 1; day <= 3; day++)
        {
            WriteSnapshot("srv/db/Tables/dbo/Orders", Snapshot(new DateTimeOffset(2026, 1, day, 0, 0, 0, TimeSpan.Zero)));
            await _store.UpdateAsync(new MetricsHistoryUpdateRequest
            {
                SnapshotRoot = SnapshotRoot,
                HistoryRoot = HistoryRoot,
                HistoryLimit = 2,
            }, CancellationToken.None);
        }

        IReadOnlyList<MetricsSnapshot> history = await _store.LoadHistoryAsync(HistoryRoot, "srv/db/Tables/dbo/Orders", CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), history[0].CapturedAt);
        Assert.Equal(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero), history[1].CapturedAt);
    }

    [Fact]
    public async Task LoadHistoryAsync_UnknownObject_ReturnsEmpty()
    {
        IReadOnlyList<MetricsSnapshot> history = await _store.LoadHistoryAsync(HistoryRoot, "no/such/object", CancellationToken.None);

        Assert.Empty(history);
    }

    [Fact]
    public async Task LoadHistoryAsync_CorruptHistoryFile_ReturnsEmptyRatherThanThrowing()
    {
        string path = Path.Combine(HistoryRoot, "srv/db/Tables/dbo/Orders.json".Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not valid json");

        IReadOnlyList<MetricsSnapshot> history = await _store.LoadHistoryAsync(HistoryRoot, "srv/db/Tables/dbo/Orders", CancellationToken.None);

        Assert.Empty(history);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
