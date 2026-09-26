using System.CommandLine;
using System.Globalization;
using SyncSql.Cli.Commands;
using SyncSql.Cli.Parsing;

namespace SyncSql.Cli.Tests;

public sealed class ParserCommandTests
{
    [Theory]
    [InlineData("mssql", "SELECT o.Id FROM dbo.Orders AS o WHERE o.Id = 1;", "SelectStatement")]
    [InlineData("oracle", "SELECT o.Id FROM app.Orders o WHERE o.Id = 1;", "select_statement")]
    public void InspectionIncludesTreeTokensAndLineage(string engine, string sql, string statement)
    {
        SqlInspection result = SqlInspection.Parse("example.sql", sql, engine);
        Assert.False(result.HasErrors);
        Assert.Contains(result.Sections[0].Pieces, p => p.Name.Trim() == statement);
        Assert.Contains(result.Sections[1].Pieces, p => result.Describe(p).Contains("Orders", StringComparison.Ordinal));
        Assert.Contains(result.Sections[2].Pieces, p => p.Name.EndsWith("Orders", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Sections[3].Pieces, p => p.Name.Equals("o", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Sections[4].Pieces, p => p.Name.Equals("o.Id", StringComparison.OrdinalIgnoreCase));
        Assert.All(result.Sections[0].Pieces, p => Assert.InRange(p.Offset, 0, sql.Length));
        Assert.Equal(sql, result.Sql);
    }

    [Theory]
    [InlineData("mssql")]
    [InlineData("oracle")]
    public void InvalidSqlKeepsDiagnosticsAndOriginalSource(string engine)
    {
        SqlInspection result = SqlInspection.Parse("broken.sql", "SELECT FROM ;", engine);
        Assert.True(result.HasErrors);
        Assert.NotEmpty(result.Sections[5].Pieces);
        Assert.All(result.Sections[5].Pieces, p => Assert.True(p.Line > 0 && p.Column > 0));
        Assert.Contains("SELECT FROM ;", result.Describe(result.Sections[6].Pieces[0]));
    }

    [Fact]
    public void BatchesCommentsStringsAndLocationsArePreserved()
    {
        const string sql = "-- comment\nSELECT 'GO; not a statement';\nGO\nSELECT 2;";
        SqlInspection result = SqlInspection.Parse("batch.sql", sql, "mssql");
        Assert.False(result.HasErrors);
        Assert.Equal(2, result.Sections[0].Pieces.Count(p => p.Name.Trim() == "SelectStatement"));
        Assert.Contains(result.Sections[1].Pieces, p => p.Name == "SingleLineComment");
        Assert.Contains(result.Sections[0].Pieces, p => p.Name.Trim() == "SelectStatement" && p.Line == 4);
    }

    [Theory]
    [InlineData("mssql")]
    [InlineData("oracle")]
    public void EmptyFilesAreInspectable(string engine)
    {
        SqlInspection result = SqlInspection.Parse("empty.sql", "", engine);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Sections[2].Pieces);
        Assert.Contains("empty.sql", ParserDashboard.Render(new DashboardState(result), 100, 24));
    }

    [Fact]
    public void NavigationFilteringScrollingAndQuitWork()
    {
        DashboardState state = new(SqlInspection.Parse("query.sql", "SELECT o.Id FROM dbo.Orders o;", "mssql"));
        Assert.True(state.Handle(Key(ConsoleKey.End), 10));
        Assert.Equal(state.Items.Count - 1, state.Selected);
        state.Handle(Key(ConsoleKey.Tab), 10);
        Assert.Equal(1, state.Section);
        Assert.Equal(0, state.Selected);
        state.Handle(new ConsoleKeyInfo('/', ConsoleKey.Oem2, false, false, false), 10);
        foreach (char c in "Identifier") { state.Handle(new ConsoleKeyInfo(c, ConsoleKey.A, false, false, false), 10); }
        state.Handle(Key(ConsoleKey.Enter), 10);
        Assert.NotEmpty(state.Items);
        Assert.All(state.Items, p => Assert.Contains("Identifier", p.Name));
        state.Handle(Key(ConsoleKey.PageDown), 10);
        Assert.Equal(10, state.DetailRow);
        state.Handle(Key(ConsoleKey.L), 10);
        Assert.Equal(8, state.DetailColumn);
        state.Handle(Key(ConsoleKey.LeftArrow), 10);
        Assert.Equal("", state.Filter);
        Assert.Equal(0, state.DetailRow);
        Assert.False(state.Handle(Key(ConsoleKey.Q), 10));
    }

    [Fact]
    public void RenderingHandlesSmallWindowsEmptyFiltersAndControlCharacters()
    {
        SqlInspection inspection = SqlInspection.Parse("unsafe\u001b[2J.sql", "SELECT '\u001b[2J';", "mssql");
        DashboardState state = new(inspection);
        Assert.Contains("Resize terminal", ParserDashboard.Render(state, 60, 10));
        state.Handle(new ConsoleKeyInfo('/', ConsoleKey.Oem2, false, false, false), 10);
        state.Handle(new ConsoleKeyInfo('~', ConsoleKey.Oem3, false, false, false), 10);
        string frame = ParserDashboard.Render(state, 100, 24);
        Assert.Contains("No pieces match", frame);
        Assert.DoesNotContain("\u001b[2J", frame, StringComparison.Ordinal);
        Assert.Equal(24, frame.Split("\r\n", StringSplitOptions.None).Length);
        using StringWriter output = new(CultureInfo.InvariantCulture);
        ParserDashboard.Print(inspection, output);
        Assert.DoesNotContain('\u001b', output.ToString());
        Assert.Contains("[Tokens]", output.ToString());
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("SELECT 1;", 0)]
    [InlineData("SELECT FROM ;", 1)]
    public async Task PlainCommandReturnsParseStatus(string sql, int expected)
    {
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, sql);
            Assert.Equal(expected, await new RootCommand { ParserCommand.Build() }
                .Parse(["parser", "--file", file, "--plain"]).InvokeAsync());
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task CommandRejectsMissingFilesAndInvalidArguments()
    {
        RootCommand root = new() { ParserCommand.Build() };
        Assert.NotEmpty(root.Parse("parser").Errors);
        Assert.NotEmpty(root.Parse("parser --file test.sql --engine postgres").Errors);
        Assert.Equal(1, await root.Parse(["parser", "--file", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sql"), "--plain"]).InvokeAsync());
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
}
