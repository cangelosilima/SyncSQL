using System.Text;
using SyncSql.Core.Domain;
using SyncSql.Core.Serialization;

namespace SyncSql.Core.Tests.Serialization;

/// <summary>
/// The extracted object format's encoding contract, exercised through real files on disk rather than
/// in-memory strings: object files are written by this CLI but also read back long after the fact,
/// including files produced by the PowerShell extractor this format was ported from (which wrote a
/// UTF-8 BOM, and whose Out-File default was UTF-16). Every assertion here holds identically on
/// Windows and on Linux - that is the point of the suite.
/// </summary>
public sealed class ExtractedObjectFileEncodingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("syncsql-encoding-").FullName;

    // Latin-1 accents (the common case for a Brazilian schema), a CJK ideograph, and a non-BMP
    // emoji, which is a surrogate pair in UTF-16 and four bytes in UTF-8 - between them they break
    // any code path that assumes one char is one byte or that the console code page is enough.
    private const string NonAscii = "Pedidos: descrição da coleção · 注文 · 🧾";

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static ExtractedObject SampleObject(
        string ddl,
        string? description = null,
        IReadOnlyList<ExtractedColumn>? columns = null,
        IReadOnlyList<ExtractedSection>? sections = null)
    {
        return new ExtractedObject
        {
            Server = "SQLPROD01",
            Database = "AppDb",
            Schema = "dbo",
            Type = "Tables",
            Name = "Orders",
            Ddl = ddl,
            Engine = DatabaseEngine.MsSql,
            Description = description,
            Columns = columns ?? [],
            Sections = sections ?? [],
        };
    }

    private string WriteFile(string content, Encoding encoding)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}.sql");
        File.WriteAllText(path, content, encoding);
        return path;
    }

    [Fact]
    public void WriteThenReadBackFromDisk_NonAsciiTextSurvivesUtf8RoundTrip()
    {
        ExtractedObject original = SampleObject(
            $"CREATE TABLE [dbo].[Orders] (\n    [Descrição] NVARCHAR(200) -- {NonAscii}\n);",
            description: NonAscii,
            columns: [new ExtractedColumn("Descrição", "nvarchar(200)", NonAscii)]);

        string path = WriteFile(ExtractedObjectFile.Write(original), Utf8NoBom);
        ParsedObjectFile parsed = ExtractedObjectFile.Parse(File.ReadAllLines(path));

        Assert.Equal(original.Ddl, parsed.Ddl);
        Assert.Equal(NonAscii, parsed.Description);
        ExtractedColumn column = Assert.Single(parsed.Columns);
        Assert.Equal("Descrição", column.Name);
        Assert.Equal(NonAscii, column.Description);
    }

    [Fact]
    public void Parse_Utf8WithBomFile_ReadThroughReadAllLines_RecoversEverything()
    {
        string path = WriteFile(
            ExtractedObjectFile.Write(SampleObject("CREATE TABLE [dbo].[Orders] ([Id] INT);", description: NonAscii)),
            Utf8WithBom);

        ParsedObjectFile parsed = ExtractedObjectFile.Parse(File.ReadAllLines(path));

        Assert.Equal(DatabaseEngine.MsSql, parsed.Engine);
        Assert.Equal("CREATE TABLE [dbo].[Orders] ([Id] INT);", parsed.Ddl);
        Assert.Equal(NonAscii, parsed.Description);
    }

    [Fact]
    public void Parse_Utf16LeFile_ReadThroughReadAllLines_RecoversEverything()
    {
        // What Windows PowerShell 5.1's Out-File wrote by default, so it is what a tree extracted
        // before the port can still hold. ReadAllLines detects it from the byte-order mark.
        string path = WriteFile(
            ExtractedObjectFile.Write(SampleObject("CREATE TABLE [dbo].[Orders] ([Id] INT);", description: NonAscii)),
            Encoding.Unicode);

        ParsedObjectFile parsed = ExtractedObjectFile.Parse(File.ReadAllLines(path));

        Assert.Equal(DatabaseEngine.MsSql, parsed.Engine);
        Assert.Equal("CREATE TABLE [dbo].[Orders] ([Id] INT);", parsed.Ddl);
        Assert.Equal(NonAscii, parsed.Description);
    }

    [Fact]
    public void Parse_RawBytesCarryingAUtf8Bom_StillRecoversHeaderAndDdl()
    {
        // The `git show` path: blob bytes decoded straight to a string, with no StreamReader to
        // strip the mark. Left in place, the BOM breaks the "-- " header scan on line 1, which
        // drags the whole header into the DDL and loses the engine with it.
        string path = WriteFile(
            ExtractedObjectFile.Write(SampleObject("CREATE TABLE [dbo].[Orders] ([Id] INT);", description: "Order header table")),
            Utf8WithBom);
        string raw = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.StartsWith("\uFEFF", raw, StringComparison.Ordinal);

        ParsedObjectFile parsed = ExtractedObjectFile.Parse(raw.Split('\n'));

        Assert.Equal(DatabaseEngine.MsSql, parsed.Engine);
        Assert.Equal("CREATE TABLE [dbo].[Orders] ([Id] INT);", parsed.Ddl);
        Assert.Equal("Order header table", parsed.Description);
    }

    [Fact]
    public void Write_FileOnDisk_HoldsNoCarriageReturnBytesOnAnyPlatform()
    {
        ExtractedObject original = SampleObject(
            "CREATE TABLE [dbo].[Orders] (\r\n    [Id] INT\r\n);",
            description: "Order header table",
            columns: [new ExtractedColumn("Id", "int", "Primary key")]);

        string path = WriteFile(ExtractedObjectFile.Write(original), Utf8NoBom);

        Assert.DoesNotContain((byte)'\r', File.ReadAllBytes(path));
    }

    [Fact]
    public void Write_SameObjectWithCrlfOrLfDdl_ProducesIdenticalBytes()
    {
        const string body = "CREATE TABLE [dbo].[Orders] (\n    [Id] INT\n);";

        byte[] fromLf = Utf8NoBom.GetBytes(ExtractedObjectFile.Write(SampleObject(body)));
        byte[] fromCrlf = Utf8NoBom.GetBytes(ExtractedObjectFile.Write(SampleObject(body.ReplaceLineEndings("\r\n"))));
        byte[] fromCr = Utf8NoBom.GetBytes(ExtractedObjectFile.Write(SampleObject(body.ReplaceLineEndings("\r"))));

        // Two machines extracting the same object must commit the same bytes, or every object in
        // the tree shows a whole-file diff the first time extraction moves between platforms.
        Assert.Equal(fromLf, fromCrlf);
        Assert.Equal(fromLf, fromCr);
    }

    [Fact]
    public void WriteThenParse_MultiLineSectionContentWithCrlf_KeepsSectionIntact()
    {
        ExtractedObject original = SampleObject(
            "CREATE TABLE [dbo].[Orders] ([Id] INT);",
            sections: [new ExtractedSection("Indexes", "CREATE INDEX [IX_A]\r\n    ON [dbo].[Orders] ([Id]);\r\nGO")]);

        string path = WriteFile(ExtractedObjectFile.Write(original), Utf8NoBom);
        ParsedObjectFile parsed = ExtractedObjectFile.Parse(File.ReadAllLines(path));

        ExtractedSection section = Assert.Single(parsed.Sections);
        Assert.Equal("Indexes", section.Title);
        Assert.Equal("CREATE INDEX [IX_A]\n    ON [dbo].[Orders] ([Id]);\nGO", section.Content);
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
