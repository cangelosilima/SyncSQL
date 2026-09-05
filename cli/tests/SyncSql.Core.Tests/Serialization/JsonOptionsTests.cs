using System.Text.Json;
using SyncSql.Core.Domain;
using SyncSql.Core.Json;

namespace SyncSql.Core.Tests.Serialization;

public sealed class JsonOptionsTests
{
    [Theory]
    [InlineData("\"GRANT\"", GrantState.Grant)]
    [InlineData("\"grant\"", GrantState.Grant)]
    [InlineData("\"DENY\"", GrantState.Deny)]
    [InlineData("\"deny\"", GrantState.Deny)]
    public void GrantState_AcceptsCaseInsensitiveLiterals(string json, GrantState expected)
    {
        Assert.Equal(expected, JsonSerializer.Deserialize<GrantState>(json));
        Assert.Equal(expected == GrantState.Grant ? "\"GRANT\"" : "\"DENY\"", JsonSerializer.Serialize(expected));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"REVOKE\"")]
    [InlineData("\"\"")]
    public void GrantState_RejectsUnknownLiterals(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GrantState>(json));
    }

    [Fact]
    public void SharedOptions_OmitNullsAndReadCaseInsensitiveProperties()
    {
        CatalogEdge value = new() { From = "a", To = "b", Columns = [] };
        string compact = JsonSerializer.Serialize(value, SyncSqlJsonOptions.Default);
        string indented = JsonSerializer.Serialize(value, SyncSqlJsonOptions.Indented);
        Assert.DoesNotContain('\n', compact);
        Assert.Contains('\n', indented);
        Assert.Equal("a", JsonSerializer.Deserialize<CatalogEdge>("{\"FROM\":\"a\",\"TO\":\"b\"}", SyncSqlJsonOptions.Default)!.From);
        Assert.Equal("{}", JsonSerializer.Serialize(new { absent = (string?)null }, SyncSqlJsonOptions.Default));
        Assert.Equal("{}", JsonSerializer.Serialize(new { absent = (string?)null }, SyncSqlJsonOptions.Indented));
    }
}
