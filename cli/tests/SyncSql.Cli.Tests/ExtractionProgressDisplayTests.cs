using System.Globalization;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Sync;
using SyncSql.Core.Abstractions;

namespace SyncSql.Cli.Tests;

public sealed class ExtractionProgressDisplayTests
{
    [Theory]
    [InlineData(true, false, null, false)]
    [InlineData(false, true, null, false)]
    [InlineData(false, false, "dumb", false)]
    [InlineData(false, false, "DUMB", false)]
    [InlineData(false, false, "xterm-256color", true)]
    [InlineData(false, false, null, true)]
    public void Terminal_AnimatesOnlyWhenBothStreamsSupportIt(bool outputRedirected, bool errorRedirected, string? term, bool expected) =>
        Assert.Equal(expected, SyncSqlTerminal.CanAnimate(outputRedirected, errorRedirected, term));

    [Fact]
    public async Task EmptyDisplay_AndFinishedObserversRemainStable()
    {
        SyncSqlTerminal terminal = new(TextWriter.Null, animated: false);
        await using ExtractionProgressDisplay display = terminal.StartExtraction();
        Assert.Contains("0% | 0/0 finished", display.Lines(200, 30)[0]);
        display.Add("SQL");
        IProgress<ExtractionProgress> observer = display.Start("SQL");
        display.Complete("SQL", "Skipped", "Filtered", 0);
        observer.Report(new("Late update", 999));
        Assert.Contains("SQL | Skipped | 0 objects", display.Lines(200, 30)[2]);
        Assert.DoesNotContain("Late update", display.Lines(200, 30)[2]);
    }

    [Fact]
    public async Task ReplacedDisplay_DoesNotClearNewSessionAndUsesTerminalDimensions()
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        SyncSqlTerminal terminal = new(output, animated: true, size: () => (41, 7));
        await using ExtractionProgressDisplay previous = terminal.StartExtraction();
        previous.Add("OLD");
        await using ExtractionProgressDisplay current = terminal.StartExtraction();
        current.Add("NEW");
        await previous.DisposeAsync();
        lock (terminal.Gate)
        {
            output.GetStringBuilder().Clear();
            terminal.Render();
            string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.All(lines, line => Assert.True(line.Length <= 40));
            Assert.Contains(lines, line => line.Contains("NEW | Queued", StringComparison.Ordinal));
        }
        await current.DisposeAsync();
        lock (terminal.Gate)
        {
            string completed = output.ToString();
            terminal.Render();
            Assert.Equal(completed, output.ToString());
        }
    }

    [Fact]
    public async Task Display_TracksEveryServerAndExpandsTotalsForDiscovery()
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        SyncSqlTerminal terminal = new(output, animated: true);
        await using ExtractionProgressDisplay display = terminal.StartExtraction();
        display.Add("SQL");
        display.Add("ORACLE");
        IProgress<ExtractionProgress> sql = display.Start("SQL");
        IProgress<ExtractionProgress> oracle = display.Start("ORACLE");
        sql.Report(new("db: Tables", 25));
        oracle.Report(new("HR: Views", 12, 3, 10));
        string running = string.Join('\n', display.Lines(200, 30));
        Assert.Contains("0/2 finished | 2 active", running);
        Assert.Contains("37 objects extracted", running);
        Assert.Contains("SQL | Extracting | 25 objects", running);
        Assert.Contains("ORACLE | Extracting (3/10)", running);
        Assert.Contains("HR: Views", running);

        display.Complete("SQL", "Done", "Files written", 25);
        Assert.Contains("50%", display.Lines(200, 30)[0]);
        display.Add("REMOTE");
        Assert.Contains("33%", display.Lines(200, 30)[0]);
        Assert.Contains(display.Lines(200, 30), line => line.Contains("REMOTE | Queued", StringComparison.Ordinal));
        display.Complete("ORACLE", "Failed", "See error above");
        Assert.Contains("1 failed", display.Lines(200, 30)[0]);

        await display.DisposeAsync();
        Assert.Contains("REMOTE | Cancelled", output.ToString());
        Assert.EndsWith("\e[?25h", output.ToString());
    }

    [Fact]
    public async Task Logs_ClearAndRedrawLiveRowsWithoutLosingWarnings()
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        SyncSqlTerminal terminal = new(output, animated: true);
        await using ExtractionProgressDisplay display = terminal.StartExtraction();
        display.Add("SQL");
        display.Start("SQL").Report(new("Writing files", 20, 7, 30));
        terminal.Render();
        using SyncSqlConsoleLoggerProvider provider = new(terminal);
        provider.CreateLogger("test").LogWarning("Metrics unavailable");
        string transcript = output.ToString();
        Assert.Contains("\e[3A\r\e[J[WARN]  Metrics unavailable", transcript);
        Assert.Contains("Writing (7/30)", transcript);
        Assert.True(transcript.LastIndexOf("Extraction [", StringComparison.Ordinal) > transcript.IndexOf("Metrics unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RedirectedOutput_ContainsOnlyPlainLogs()
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        SyncSqlTerminal terminal = new(output, animated: false);
        await using ExtractionProgressDisplay display = terminal.StartExtraction();
        display.Add("SQL");
        display.Start("SQL").Report(new("Tables", 3));
        terminal.Render();
        using SyncSqlConsoleLoggerProvider provider = new(terminal);
        provider.CreateLogger("test").LogInformation("SQL: 3 objects");
        await display.DisposeAsync();
        Assert.Equal($"[INFO]  SQL: 3 objects{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task SmallTerminal_PaginatesAndTruncatesWithoutControlCharacters()
    {
        SyncSqlTerminal terminal = new(TextWriter.Null, animated: false);
        await using ExtractionProgressDisplay display = terminal.StartExtraction();
        for (int i = 0; i < 20; i++)
        {
            display.Add($"SQL{i}\n\e[2J");
        }
        string[] lines = display.Lines(40, 6);
        Assert.Equal(6, lines.Length);
        Assert.All(lines, line => Assert.True(line.Length <= 40 && !line.Any(char.IsControl)));
        Assert.Contains("page 1/7", lines[^1]);
    }

    private sealed class AnimationWriter : StringWriter
    {
        private readonly HashSet<char> _frames = [];
        public TaskCompletionSource Animated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value is not null && value.Contains("SQL | Extracting", StringComparison.Ordinal))
            {
                _frames.Add(value[0]);
                if (_frames.Count >= 2)
                {
                    Animated.TrySetResult();
                }
            }
        }
    }

    [Fact]
    public async Task Animation_RefreshesDuringAnUnchangedLongQueryAndRestoresCursor()
    {
        using AnimationWriter output = new();
        SyncSqlTerminal terminal = new(output, animated: true);
        await using ExtractionProgressDisplay display = terminal.StartExtraction();
        display.Add("SQL");
        display.Start("SQL").Report(new("Reading metadata"));
        await output.Animated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await display.DisposeAsync();
        Assert.Contains("SQL | Cancelled", output.ToString());
        Assert.EndsWith("\e[?25h", output.ToString());
    }
}
