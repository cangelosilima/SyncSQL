using System.Text.Json;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Domain;

/// <summary>
/// Locks in the exact JSON field names catalog.json must use - site/src/types.ts is the source of
/// truth (the React site is not touched by this port), so a drift here would silently break the site
/// with no compile-time signal from this solution alone.
/// </summary>
public class CatalogJsonTests
{
    [Fact]
    public void Catalog_SerializesWithExactFieldNamesTheSiteExpects()
    {
        Catalog catalog = new()
        {
            GeneratedAt = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero),
            Servers = ["SQLPROD01"],
            TypeCounts = new Dictionary<string, int> { ["Tables"] = 1 },
            Nodes =
            [
                new CatalogNode
                {
                    Id = "SQLPROD01/AppDb/Tables/dbo/Orders",
                    Server = "SQLPROD01",
                    Database = "AppDb",
                    Schema = "dbo",
                    Type = "Tables",
                    Name = "Orders",
                    QualifiedName = "dbo.Orders",
                    Path = "SQLPROD01/AppDb/Tables/dbo/Orders.sql",
                    Ddl = "CREATE TABLE [dbo].[Orders] ([OrderId] INT NOT NULL)",
                    SizeBytes = 42,
                    Engine = DatabaseEngine.MsSql,
                    Columns = [new ExtractedColumn("OrderId", "int", null)],
                    Grants = [new GrantEntry("SELECT", GrantState.Grant, "app_reader", "DATABASE_ROLE", null)],
                    Metrics =
                    [
                        new MetricsSnapshot
                        {
                            CapturedAt = new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero),
                            RowCount = 100,
                            ReservedKB = 64,
                        },
                    ],
                },
            ],
            Edges = [new CatalogEdge { From = "a", To = "b", Columns = ["OrderId"] }],
        };

        string json = JsonSerializer.Serialize(catalog);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Top level
        Assert.True(root.TryGetProperty("generatedAt", out _));
        Assert.True(root.TryGetProperty("servers", out _));
        Assert.True(root.TryGetProperty("typeCounts", out _));
        Assert.True(root.TryGetProperty("nodes", out _));
        Assert.True(root.TryGetProperty("edges", out _));
        Assert.True(root.TryGetProperty("recentChanges", out _));
        Assert.True(root.TryGetProperty("coChangePairs", out _));

        JsonElement node = root.GetProperty("nodes")[0];
        foreach (string field in new[]
        {
            "id", "server", "database", "schema", "type", "name", "qualifiedName", "path", "ddl",
            "description", "columns", "grants", "sections", "sizeBytes", "changeCount", "lastChangedAt",
            "history", "metrics",
        })
        {
            Assert.True(node.TryGetProperty(field, out _), $"catalog node is missing expected field '{field}'");
        }

        JsonElement column = node.GetProperty("columns")[0];
        Assert.Equal("OrderId", column.GetProperty("name").GetString());
        Assert.Equal("int", column.GetProperty("dataType").GetString());

        JsonElement grant = node.GetProperty("grants")[0];
        Assert.Equal("GRANT", grant.GetProperty("state").GetString());

        JsonElement metric = node.GetProperty("metrics")[0];
        Assert.True(metric.TryGetProperty("capturedAt", out _));
        Assert.True(metric.TryGetProperty("reservedKB", out _));
        Assert.True(metric.TryGetProperty("indexes", out _));
        Assert.True(metric.TryGetProperty("statistics", out _));

        JsonElement edge = root.GetProperty("edges")[0];
        Assert.Equal("a", edge.GetProperty("from").GetString());
        Assert.Equal("b", edge.GetProperty("to").GetString());
    }

    [Fact]
    public void GrantState_RoundTripsAsUppercaseLiterals()
    {
        GrantEntry grant = new("SELECT", GrantState.Deny, "public", null, null);

        string json = JsonSerializer.Serialize(grant);
        Assert.Contains("\"DENY\"", json);

        GrantEntry? roundTripped = JsonSerializer.Deserialize<GrantEntry>(json);
        Assert.Equal(GrantState.Deny, roundTripped!.State);
    }

    [Fact]
    public void DatabaseEngine_RoundTripsAsLowercaseTokens()
    {
        string json = JsonSerializer.Serialize(DatabaseEngine.MsSql);
        Assert.Equal("\"mssql\"", json);

        DatabaseEngine parsed = JsonSerializer.Deserialize<DatabaseEngine>("\"oracle\"");
        Assert.Equal(DatabaseEngine.Oracle, parsed);
    }
}
