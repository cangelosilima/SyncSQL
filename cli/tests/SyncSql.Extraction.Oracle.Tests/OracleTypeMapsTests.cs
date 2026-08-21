using SyncSql.Extraction.Oracle;

namespace SyncSql.Extraction.Oracle.Tests;

public class OracleTypeMapsTests
{
    [Fact]
    public void ObjectTypeMap_CoversEveryDocumentedOracleObjectType()
    {
        string[] configTypes = [.. OracleTypeMaps.ObjectTypeMap.Select(m => m.ConfigType)];

        Assert.Equal(
            ["Tables", "Views", "Procedures", "Functions", "Packages", "PackageBodies", "Triggers", "Synonyms"],
            configTypes);
    }

    [Theory]
    [InlineData("TABLE", "TABLE")]
    [InlineData("VIEW", "VIEW")]
    [InlineData("PACKAGE", "PACKAGE")]
    [InlineData("PACKAGE BODY", "PACKAGE_BODY")]
    public void ToDdlType_OnlyPackageBodyDiffersFromTheObjectTypeName(string oracleObjectType, string expectedDdlType)
    {
        Assert.Equal(expectedDdlType, OracleTypeMaps.ToDdlType(oracleObjectType));
    }
}
