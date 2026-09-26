using SyncSql.Cli.Parsing;
using static SyncSql.Cli.Tests.ParserDashboardLifecycleTests;

namespace SyncSql.Cli.Tests;

public sealed class ParserDashboardNavigationTests
{
    [Fact]
    public void NavigationWrapsSectionsAndBoundsSelections()
    {
        DashboardState state = new(SqlInspection.Parse("query.sql", "SELECT 1;", "mssql"));
        state.Handle(Key(ConsoleKey.UpArrow), 5);
        Assert.Equal(0, state.Selected);
        state.Handle(Key(ConsoleKey.DownArrow), 5);
        Assert.Equal(1, state.Selected);
        state.Handle(Key(ConsoleKey.K), 5);
        Assert.Equal(0, state.Selected);
        state.Handle(Key(ConsoleKey.J), 5);
        state.Handle(Key(ConsoleKey.Home), 5);
        Assert.Equal(0, state.Selected);
        state.Handle(Key(ConsoleKey.Tab, shift: true), 5);
        Assert.Equal(6, state.Section);
        state.Handle(Key(ConsoleKey.RightArrow), 5);
        Assert.Equal(0, state.Section);
        state.Handle(Key(ConsoleKey.D7, '7'), 5);
        Assert.Equal(6, state.Section);
        state.Handle(Key(ConsoleKey.D7, '7'), 5);
        state.Handle(Key(ConsoleKey.D8, '8'), 5);
        state.Handle(Key(ConsoleKey.C), 5);
        Assert.Equal(6, state.Section);
        state.Handle(Key(ConsoleKey.End), 5);
        state.Handle(Key(ConsoleKey.DownArrow), 5);
        Assert.Equal(0, state.Selected);
        state.Handle(Key(ConsoleKey.PageDown), 5);
        state.Handle(Key(ConsoleKey.PageUp), 5);
        state.Handle(Key(ConsoleKey.PageUp), 5);
        Assert.Equal(0, state.DetailRow);
        state.Handle(Key(ConsoleKey.L), 5);
        state.Handle(Key(ConsoleKey.H), 5);
        state.Handle(Key(ConsoleKey.H), 5);
        Assert.Equal(0, state.DetailColumn);
        Assert.False(state.Handle(Key(ConsoleKey.C, control: true), 5));
        Assert.False(state.Handle(Key(ConsoleKey.Escape), 5));
    }

    [Fact]
    public void EditingFilterSupportsDeleteCancelIgnoredKeysAndDescriptionMatches()
    {
        SqlInspection inspection = new("query.sql", "", "mssql", [new("Objects", [new("Orders", Description: "Dynamic"), new("Other")])], false);
        DashboardState state = new(inspection);
        state.Handle(Key(ConsoleKey.Oem2, '/'), 5);
        state.Handle(Key(ConsoleKey.D, 'd'), 5);
        Assert.Single(state.Items);
        Assert.Contains("Enter to apply", ParserDashboard.Render(state, 120, 12));
        state.Handle(Key(ConsoleKey.LeftArrow), 5);
        Assert.Equal("d", state.Filter);
        state.Handle(Key(ConsoleKey.Backspace), 5);
        state.Handle(Key(ConsoleKey.Backspace), 5);
        Assert.Equal("", state.Filter);
        state.Handle(Key(ConsoleKey.Z, 'z'), 5);
        Assert.Empty(state.Items);
        state.Handle(Key(ConsoleKey.Escape), 5);
        Assert.False(state.EditingFilter);
        Assert.Equal(2, state.Items.Count);
    }

    [Fact]
    public void RendererShowsParseErrorsAndClampsDetailAfterPanningAndResizing()
    {
        DashboardState state = new(SqlInspection.Parse("bad.sql", "SELECT FROM ;", "mssql"));
        Assert.Contains("PARSE ERRORS", ParserDashboard.Render(state, 100, 24));
        state.Handle(Key(ConsoleKey.D7, '7'), 5);
        state.Handle(Key(ConsoleKey.PageDown), 100);
        state.Handle(Key(ConsoleKey.L), 5);
        string rendered = ParserDashboard.Render(state, 100, 24);
        Assert.Equal(0, state.DetailRow);
        Assert.Contains("|", rendered);
        Assert.Contains("Resize terminal", ParserDashboard.Render(state, 100, 10));
        Assert.Contains("Resize terminal", ParserDashboard.Render(state, 60, 24));
    }

    [Fact]
    public void LongPathsAndSqlStayInsideTheTerminalViewport()
    {
        string text = new('x', 300);
        DashboardState state = new(SqlInspection.Parse(text + ".sql", $"SELECT '{text}';", "mssql"));
        string rendered = ParserDashboard.Render(state, 80, 24);
        string visible = System.Text.RegularExpressions.Regex.Replace(rendered, "\u001b\\[[0-9;]*[Hm]", "");
        Assert.All(visible.Split("\r\n", StringSplitOptions.None), row => Assert.Equal(79, row.Length));
        Assert.DoesNotContain(text, visible);
    }
}
