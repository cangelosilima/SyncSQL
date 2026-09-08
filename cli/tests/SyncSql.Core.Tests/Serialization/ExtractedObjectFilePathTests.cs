using SyncSql.Core.Serialization;

namespace SyncSql.Core.Tests.Serialization;

/// <summary>
/// Path and file-name construction, asserted to be identical on Windows and Linux. The generated tree
/// and its git history are shared between the two - a Windows extraction and a Linux one must agree
/// on every path they produce, which rules out Path.GetInvalidFileNameChars (Unix only rejects '/'
/// and NUL) and Path.DirectorySeparatorChar (a backslash on Windows) as inputs to that decision.
/// </summary>
public class ExtractedObjectFilePathTests
{
    [Fact]
    public void RelativePath_SchemaDefinitionAndUserTypes_ShareSchemaDirectory()
    {
        Assert.Equal("ROOT/AppDb/sales/sales.sql", ExtractedObjectFile.RelativePath("ROOT", "AppDb", null, "Schemas", "sales"));
        Assert.Equal("ROOT/AppDb/sales/Types/Code.sql", ExtractedObjectFile.RelativePath("ROOT", "AppDb", "sales", "Types", "Code"));
        Assert.Equal("ROOT/AppDb/Schemas/sales", ExtractedObjectFile.ObjectId("ROOT", "AppDb", null, "Schemas", "sales"));
    }

    [Fact]
    public void RelativePath_FollowedServer_NestsUnderOriginalLinkAndPreservesDatabaseAndSchema()
    {
        Assert.Equal("ROOT/LinkedServers/REMOTE/SalesDb/sales/Tables/Orders.sql",
            ExtractedObjectFile.RelativePath("REMOTE_2", "SalesDb", "sales", "Tables", "Orders", serverPath: ["ROOT", "LinkedServers", "REMOTE"]));
    }

    [Fact]
    public void RelativePath_FollowedLinkNamedDotDot_CannotEscapeItsFolder()
    {
        Assert.Equal("ROOT/LinkedServers/__/SalesDb/sales/Tables/Orders.sql",
            ExtractedObjectFile.RelativePath("REMOTE", "SalesDb", "sales", "Tables", "Orders", serverPath: ["ROOT", "LinkedServers", ".."]));
    }
    // Each of these is legal in a Unix file name and rejected by Windows. Sanitizing them only
    // where the OS demands it would give the same object two different ids depending on where the
    // extraction ran.
    [Theory]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData(':')]
    [InlineData('"')]
    [InlineData('\\')]
    [InlineData('|')]
    [InlineData('?')]
    [InlineData('*')]
    public void SafeFileName_ReplacesWindowsInvalidCharacter_OnEveryPlatform(char invalid)
    {
        string safe = ExtractedObjectFile.SafeFileName($"Order{invalid}Report");

        Assert.Equal("Order_Report", safe);
    }

    [Fact]
    public void SafeFileName_ReplacesDirectorySeparatorAndControlCharacters()
    {
        Assert.Equal("dbo_Orders", ExtractedObjectFile.SafeFileName("dbo/Orders"));
        Assert.Equal("dbo_Orders", ExtractedObjectFile.SafeFileName("dbo\\Orders"));
        Assert.Equal("Order_Report", ExtractedObjectFile.SafeFileName("Order\tReport"));
        Assert.Equal("Order_Report", ExtractedObjectFile.SafeFileName("Order\nReport"));
        Assert.Equal("Order_Report", ExtractedObjectFile.SafeFileName("Order\0Report"));
    }

    [Fact]
    public void SafeFileName_KeepsCharactersThatAreLegalOnBothPlatforms()
    {
        // Including non-ASCII: an accented or ideographic object name is a valid file name on both,
        // and rewriting it would collapse distinct objects onto one path.
        const string name = "Pedidos Especiais.v2 - Ação 注文";

        Assert.Equal(name, ExtractedObjectFile.SafeFileName(name));
    }

    [Fact]
    public void RelativePath_UsesForwardSlashesOnEveryPlatform()
    {
        string path = ExtractedObjectFile.RelativePath("SQLPROD01", "AppDb", "dbo", "Tables", "Orders");

        Assert.Equal("SQLPROD01/AppDb/dbo/Tables/Orders.sql", path);
        Assert.DoesNotContain('\\', path);
    }

    [Fact]
    public void RelativePath_SegmentContainingSeparators_StaysInsideItsOwnSegment()
    {
        // A database or object name is server-supplied text, not a path fragment: whatever it holds,
        // the result keeps exactly one segment per component, on both platforms.
        string path = ExtractedObjectFile.RelativePath("SQLPROD01", "../../etc", "dbo", "Tables", "..\\Orders");

        Assert.Equal("SQLPROD01/.._.._etc/dbo/Tables/.._Orders.sql", path);
        Assert.Equal(4, path.Count(c => c == '/'));
    }

    [Fact]
    public void ObjectId_RemainsStableWhenExportLayoutChanges()
    {
        string path = ExtractedObjectFile.RelativePath("SQLPROD01", "AppDb", "dbo", "Tables", "Orders");
        string id = ExtractedObjectFile.ObjectId("SQLPROD01", "AppDb", "dbo", "Tables", "Orders");

        Assert.Equal("SQLPROD01/AppDb/Tables/dbo/Orders", id);
        Assert.NotEqual(path[..^".sql".Length], id);
        Assert.DoesNotContain('\\', id);
    }

    [Fact]
    public void RelativePath_NonDefaultExtension_IsAppliedToTheFileNameOnly()
    {
        string path = ExtractedObjectFile.RelativePath("SQLPROD01", "AppDb", schema: null, "LinkedServers", "ORAPROD01", "json");

        Assert.Equal("SQLPROD01/AppDb/LinkedServers/ORAPROD01.json", path);
    }

    [Theory]
    [InlineData("_ServerLevel", null, "sql")]
    [InlineData("_serverLevel", "", "json")]
    public void RelativePath_ServerLevel_OmitsPseudoDatabase(string database, string? schema, string extension)
    {
        string path = ExtractedObjectFile.RelativePath("SQLPROD01", database, schema, "LinkedServers", "REMOTE", extension);

        Assert.Equal($"SQLPROD01/LinkedServers/REMOTE.{extension}", path);
        Assert.Equal("SQLPROD01/_ServerLevel/LinkedServers/REMOTE",
            ExtractedObjectFile.ObjectId("SQLPROD01", "_ServerLevel", schema, "LinkedServers", "REMOTE"));
    }

    [Fact]
    public void RelativePath_SchemaScopedObject_KeepsDatabaseNamedServerLevel()
    {
        Assert.Equal("SQLPROD01/_ServerLevel/dbo/Tables/Orders.sql",
            ExtractedObjectFile.RelativePath("SQLPROD01", "_ServerLevel", "dbo", "Tables", "Orders"));
    }
}
