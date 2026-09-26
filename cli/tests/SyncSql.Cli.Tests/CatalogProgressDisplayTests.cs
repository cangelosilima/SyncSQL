using System.Globalization;
using Microsoft.Extensions.Logging;
using SyncSql.Cli.Sync;

namespace SyncSql.Cli.Tests;

public sealed class CatalogProgressDisplayTests
{
    [Fact]
    public async Task Display_ShowsPhaseCountsCurrentObjectAndMemory_AndPreservesLogs()
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        SyncSqlTerminal terminal = new(output, animated: true, colorEnabled: true);
        await using CatalogProgressDisplay display = terminal.StartCatalog();
        display.Report(new("Inferring lineage", 3, 8, "ORA/APP/Views/V1", Nodes: 8, Edges: 2));
        string lines = string.Join('\n', display.Lines(200, 30));
        Assert.Contains("3/8 objects (37%)", lines);
        Assert.Contains("8 nodes | 2 edges | elapsed", lines);
        Assert.Contains("RAM", lines);
        Assert.Contains("peak", lines);
        Assert.Contains("managed", lines);
        Assert.Contains("ORA/APP/Views/V1", lines);
        terminal.Render();
        using SyncSqlConsoleLoggerProvider provider = new(terminal);
        provider.CreateLogger("test").LogWarning("Unresolved reference");
        Assert.Contains("\e[4A\r\e[J", output.ToString());
        Assert.Contains("Unresolved reference", output.ToString());
        display.Report(new("Published", 8, 8, Nodes: 8, Edges: 4));
        display.Complete();
        display.Report(new("Late update", 999));
        await display.DisposeAsync();
        Assert.Contains("Catalog Done", output.ToString());
        Assert.DoesNotContain("Late update", output.ToString());
        Assert.EndsWith("\e[?25h", output.ToString());
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task StoppedBuild_RestoresCursorAndKeepsItsStatus(string status)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        SyncSqlTerminal terminal = new(output, animated: true);
        CatalogProgressDisplay display = terminal.StartCatalog();
        display.Report(new("Inferring lineage", 0, 2, "slow-object"));
        if (status == "Failed")
        {
            display.Complete(status);
        }
        await display.DisposeAsync();
        await display.DisposeAsync();
        Assert.Contains($"Catalog {status}", output.ToString());
        Assert.DoesNotContain("Catalog Done", output.ToString());
        Assert.EndsWith("\e[?25h", output.ToString());
    }

    [Fact]
    public async Task RedirectedOutput_IsPlainAndThrottled_AndUnknownTotalsHaveNoPercentage()
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        SyncSqlTerminal terminal = new(output, animated: false);
        await using CatalogProgressDisplay display = terminal.StartCatalog();
        for (int i = 0; i < 200; i++)
        {
            display.Report(new("Scanning files", i, Current: "path\n\e[2J", Unit: "files"));
        }
        Assert.DoesNotContain('%', output.ToString());
        display.Report(new("Publishing payloads", 0, 0, Unit: "partitions"));
        display.Complete();
        await display.DisposeAsync();
        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain('\e', output.ToString());
        Assert.Contains("0/0 partitions (100%)", output.ToString());
        Assert.Contains("Catalog Done", output.ToString());
        Assert.All(display.Lines(40, 2), line => Assert.True(line.Length <= 40 && !line.Any(char.IsControl)));
    }

    private sealed class HeartbeatWriter : StringWriter
    {
        private int _updates;
        public TaskCompletionSource Heartbeat { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Write(string? value)
        {
            base.Write(value);
            if (value?.Contains("Inferring lineage", StringComparison.Ordinal) == true && ++_updates >= 2)
            {
                Heartbeat.TrySetResult();
            }
        }
    }

    private sealed class AnimationWriter : StringWriter
    {
        private readonly HashSet<string> _frames = [];
        public TaskCompletionSource Animated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value?.StartsWith("Catalog ", StringComparison.Ordinal) == true && _frames.Add(value) && _frames.Count >= 2)
            {
                Animated.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task Animation_RefreshesWhileAnObjectIsStillParsing()
    {
        using AnimationWriter output = new();
        SyncSqlTerminal terminal = new(output, animated: true);
        await using CatalogProgressDisplay display = terminal.StartCatalog();
        display.Report(new("Inferring lineage", 0, 1, "slow-object"));
        await output.Animated.Task.WaitAsync(TimeSpan.FromSeconds(10));
        display.Complete();
    }

    [Fact]
    public async Task RedirectedOutput_ReportsHeartbeatDuringAnUnchangedLongParse()
    {
        using HeartbeatWriter output = new();
        SyncSqlTerminal terminal = new(output, animated: false);
        await using CatalogProgressDisplay display = terminal.StartCatalog();
        display.Report(new("Inferring lineage", 0, 1, "slow-object"));
        await output.Heartbeat.Task.WaitAsync(TimeSpan.FromSeconds(20));
        display.Complete();
    }
}
