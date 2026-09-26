using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using NSubstitute;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Catalog.Tests;

public sealed class CatalogPublisherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("syncsql-publisher-").FullName;

    [Fact]
    public async Task PublishesBoundedPayloads_RoutesBothEdgeEndpoints_AndPrunesOnlyObsoleteHashes()
    {
        var nodes = Enumerable.Range(0, 1025).Select(i => Node($"SQL/db/Tables/dbo/T{i}", "SQL", DatabaseEngine.MsSql)).ToList();
        nodes.Add(Node("ORA/db/Tables/APP/Orders", "ORA", DatabaseEngine.Oracle));
        var catalog = new Core.Domain.Catalog
        {
            GeneratedAt = DateTimeOffset.UnixEpoch,
            Servers = ["SQL", "ORA"],
            TypeCounts = new Dictionary<string, int> { ["Tables"] = nodes.Count },
            Nodes = nodes,
            Edges = [new CatalogEdge { From = nodes[0].Id, To = nodes[^1].Id, Dynamic = true, Columns = ["Id"] }],
            OrphanedReferences = [new CatalogOrphanedReference { From = nodes[0].Id, Name = "missing" }],
        };
        CatalogPublisher publisher = new();
        string manifestPath = Path.Combine(_root, "catalog.json");
        List<CatalogProgress> updates = [];
        var progress = Substitute.For<IProgress<CatalogProgress>>();
        progress.When(observer => observer.Report(Arg.Any<CatalogProgress>()))
            .Do(call => updates.Add(call.Arg<CatalogProgress>()));
        await publisher.PublishAsync(catalog, manifestPath, false, CancellationToken.None, progress);
        Assert.Contains(updates, update => update.Activity == "Publishing payloads" && update.Completed == 0 && update.Total == 3);
        Assert.Contains(updates, update => update.Activity == "Publishing payloads" && update.Completed == 3 && update.Total == 3);
        Assert.Equal("Published", updates[^1].Activity);
        Assert.Equal(manifestPath, updates[^1].Current);
        JsonNode manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        var partitions = manifest["partitions"]!.AsArray();
        Assert.Equal(3, partitions.Count);
        Assert.Equal(1026, manifest["nodeCount"]!.GetValue<int>());
        Assert.Equal(1, manifest["edgeCount"]!.GetValue<int>());
        Assert.Null(manifest["nodes"]);
        Assert.Equal(1024, partitions[0]!["count"]!.GetValue<int>());
        foreach (int part in new[] { 0, 2 })
        {
            var payload = await ReadPayload(partitions[part]!["edges"]!.GetValue<string>());
            var edge = Assert.Single(payload["edges"]!.AsArray())!;
            Assert.Equal(nodes[^1].Id, edge["to"]!.GetValue<string>());
            Assert.True(edge["dynamic"]!.GetValue<bool>());
        }
        var detail = Assert.Single((await ReadPayload(partitions[2]!["details"]!.GetValue<string>())).AsArray())!;
        Assert.Equal("oracle", detail["engine"]!.GetValue<string>());
        Assert.Null(detail["ddl"]);
        Assert.NotNull(detail["history"]);
        var summaryPages = manifest["summaries"]!.AsArray();
        Assert.Equal(2, summaryPages.Count);
        var summary = (await ReadPayload(summaryPages[0]!["file"]!.GetValue<string>()))[0]!["nodes"]![0]!;
        Assert.Equal(1, summary["dependsOnCount"]!.GetValue<int>());
        Assert.Null(summary["history"]);

        string stale = Path.Combine(_root, "_catalog", new string('0', 64) + ".json.gz");
        string notes = Path.Combine(_root, "_catalog", "notes.txt");
        await File.WriteAllTextAsync(stale, "stale");
        await File.WriteAllTextAsync(notes, "keep");
        await publisher.PublishAsync(catalog, manifestPath, false, CancellationToken.None);
        Assert.True(File.Exists(stale));
        string before = await File.ReadAllTextAsync(manifestPath);
        await publisher.PublishAsync(catalog, manifestPath, true, CancellationToken.None, progress);
        Assert.Contains(updates, update => update.Activity == "Pruning old payloads");
        Assert.Equal(before, await File.ReadAllTextAsync(manifestPath));
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(notes));

        await Assert.ThrowsAsync<InvalidDataException>(() => publisher.PublishAsync(catalog with
        {
            Edges = [new CatalogEdge { From = nodes[0].Id, To = "unknown" }],
        }, manifestPath, false, CancellationToken.None));
        Assert.Equal(before, await File.ReadAllTextAsync(manifestPath));
    }

    [Fact]
    public async Task RejectsDuplicateIdsAndUnknownSourcesWithoutPublishing()
    {
        CatalogNode node = Node("SQL/db/Tables/dbo/Orders", "SQL", DatabaseEngine.MsSql);
        var catalog = new Core.Domain.Catalog
        {
            GeneratedAt = DateTimeOffset.UnixEpoch,
            Servers = ["SQL"],
            TypeCounts = new Dictionary<string, int>(),
            Nodes = [node, node],
            Edges = [],
        };
        string output = Path.Combine(_root, "catalog.json");
        await Assert.ThrowsAsync<InvalidDataException>(() => new CatalogPublisher().PublishAsync(catalog, output, false, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => new CatalogPublisher().PublishAsync(catalog with
        {
            Nodes = [node],
            Edges = [new CatalogEdge { From = "unknown", To = node.Id }],
        }, output, false, CancellationToken.None));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task BoundsSummaryPagesByBytesAndKeepsRecentIndexMetrics()
    {
        CatalogNode node = Node("SQL/db/Tables/dbo/Orders", "SQL", DatabaseEngine.MsSql) with
        {
            Name = new string('x', 1_050_000),
            Metrics = [new MetricsSnapshot { CapturedAt = DateTimeOffset.UnixEpoch },
                new MetricsSnapshot { CapturedAt = DateTimeOffset.UnixEpoch.AddDays(1), RowCount = 500,
                    Indexes = [new CatalogIndexMetric { Name = "PK", FragmentationPct = 70 }] }],
        };
        var catalog = new Core.Domain.Catalog
        {
            GeneratedAt = DateTimeOffset.UnixEpoch,
            Servers = ["SQL"],
            TypeCounts = new Dictionary<string, int>(),
            Nodes = [node, node with { Id = "SQL/db/Tables/dbo/Second" }, node with { Id = "SQL/archive/Tables/dbo/Orders", Database = "archive", Name = "Orders" }],
            Edges = [],
        };
        string output = Path.Combine(_root, "catalog.json");
        await new CatalogPublisher().PublishAsync(catalog, output, false, CancellationToken.None);
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(output))!;
        Assert.Equal(3, manifest["partitions"]!.AsArray().Count);
        Assert.Equal(3, manifest["summaries"]!.AsArray().Count);
        var summary = (await ReadPayload(manifest["summaries"]![0]!["file"]!.GetValue<string>()))[0]!["nodes"]![0]!;
        Assert.Equal(70, summary["metrics"]![1]!["indexes"]![0]!["fragmentationPct"]!.GetValue<double>());
        string unrelated = Path.Combine(_root, "_catalog", "keep.txt");
        await File.WriteAllTextAsync(unrelated, "keep");
        await new CatalogPublisher().PublishAsync(catalog, output, true, CancellationToken.None);
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task EmptyCatalogHasNoPayloads()
    {
        await new CatalogPublisher().PublishAsync(new Core.Domain.Catalog
        {
            GeneratedAt = DateTimeOffset.UnixEpoch,
            Servers = [],
            TypeCounts = new Dictionary<string, int>(),
            Nodes = [],
            Edges = [],
        }, Path.Combine(_root, "catalog.json"), false, CancellationToken.None);
        JsonNode manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "catalog.json")))!;
        Assert.Empty(manifest["partitions"]!.AsArray());
        Assert.Empty(manifest["summaries"]!.AsArray());
    }

    private async Task<JsonNode> ReadPayload(string relative)
    {
        await using var file = File.OpenRead(Path.Combine(_root, relative));
        await using GZipStream gzip = new(file, CompressionMode.Decompress);
        using MemoryStream content = new();
        await gzip.CopyToAsync(content);
        byte[] bytes = content.ToArray();
        Assert.Equal($"_catalog/{Convert.ToHexStringLower(SHA256.HashData(bytes))}.json.gz", relative);
        return JsonNode.Parse(bytes)!;
    }

    private static CatalogNode Node(string id, string server, DatabaseEngine engine) => new()
    {
        Id = id,
        Server = server,
        Database = "db",
        Type = "Tables",
        Name = id.Split('/')[^1],
        QualifiedName = "Orders",
        Path = id + ".sql",
        Ddl = "CREATE TABLE Orders (Id int);",
        SizeBytes = 29,
        Engine = engine,
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
