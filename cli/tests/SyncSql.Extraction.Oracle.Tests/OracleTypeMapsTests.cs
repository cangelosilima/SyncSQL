using SyncSql.Extraction.Oracle;

namespace SyncSql.Extraction.Oracle.Tests;

public class OracleTypeMapsTests
{
    [Fact]
    public void ObjectTypeMap_CoversEveryDocumentedOracleObjectType()
    {
        string[] configTypes = [.. OracleTypeMaps.ObjectTypeMap.Select(m => m.ConfigType)];

        Assert.Equal(
            ["Tables", "Types", "TypeBodies", "Views", "Procedures", "Functions", "Packages", "PackageBodies", "Triggers", "Synonyms"],
            configTypes);
    }

    [Theory]
    [InlineData("TABLE", "TABLE")]
    [InlineData("VIEW", "VIEW")]
    [InlineData("PACKAGE", "PACKAGE_SPEC")]
    [InlineData("PACKAGE BODY", "PACKAGE_BODY")]
    [InlineData("TYPE BODY", "TYPE_BODY")]
    public void ToDdlType_MapsDictionaryTypesToSeparateMetadataObjects(string oracleObjectType, string expectedDdlType)
    {
        Assert.Equal(expectedDdlType, OracleTypeMaps.ToDdlType(oracleObjectType));
    }
}
