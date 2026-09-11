using System.Text.Json;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Credentials;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Core.Tests.Serialization;

public sealed class BoundarySerializationTests
{
    [Fact]
    public async Task NullCredentialDocument_IsRejectedAndNullEntryFallsThrough()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "null");
            await Assert.ThrowsAsync<CredentialParseException>(() => CredentialsFileProvider.LoadAsync(path));
            await File.WriteAllTextAsync(path, "{\"TEST\":null}");
            var provider = await CredentialsFileProvider.LoadAsync(path);
            Assert.Equal(PartialCredentials.None, provider.Read("test"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DefaultEnvironmentProvider_ReadsTheProcessEnvironment()
    {
        string prefix = "SYNCSQL_TEST_" + Guid.NewGuid().ToString("N");
        string variable = EnvironmentCredentialProvider.UserVariableName(prefix);
        try
        {
            Environment.SetEnvironmentVariable(variable, "reader");
            Assert.Equal(new PartialCredentials("reader", null), new EnvironmentCredentialProvider().Read(prefix));
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    [Fact]
    public void ColumnProperties_IgnoreNonDescriptionProperties()
    {
        var parsed = ExtractedObjectFile.Parse([
            "CREATE TABLE t (id int);", "-- === Extended Properties ===",
            "-- [column: id] Custom = ignored", "-- [column: id] MS_Description = identifier",
        ]);
        Assert.Equal(new ExtractedColumn("id", null, "identifier"), Assert.Single(parsed.Columns));
    }

    [Fact]
    public void GrantMetadata_PreservesKnownTypesAndLeavesMissingFieldsNull()
    {
        var parsed = ExtractedObjectFile.Parse(["SELECT 1;", "-- === Grants ===", "-- [grant] SELECT|GRANT|reader||", "-- [grant] UPDATE|DENY|writer|ROLE|id"]);
        Assert.Equal(new GrantEntry("SELECT", GrantState.Grant, "reader", null, null), parsed.Grants[0]);
        Assert.Equal(new GrantEntry("UPDATE", GrantState.Deny, "writer", "ROLE", "id"), parsed.Grants[1]);
    }

    [Fact]
    public void Metrics_RoundTripAllEngineFields()
    {
        CatalogIndexMetric index = new()
        {
            Name = "ix",
            FragmentationPct = 12.5,
            PageCount = 2,
            Seeks = 3,
            Scans = 4,
            Lookups = 5,
            Updates = 6,
            RowCount = 7,
            DistinctKeys = 8,
            LeafBlocks = 9,
            LastAnalyzed = DateTimeOffset.UnixEpoch,
        };
        CatalogStatMetric stat = new()
        {
            Name = "stat",
            Rows = 1,
            RowsSampled = 2,
            Steps = 3,
            ModificationCounter = 4,
            LastUpdated = DateTimeOffset.UnixEpoch,
        };
        Assert.Equal(index, JsonSerializer.Deserialize<CatalogIndexMetric>(JsonSerializer.Serialize(index)));
        Assert.Equal(stat, JsonSerializer.Deserialize<CatalogStatMetric>(JsonSerializer.Serialize(stat)));
        Assert.Null(JsonSerializer.Deserialize<CatalogIndexMetric>("{\"name\":\"ix\"}")!.RowCount);
    }
}
