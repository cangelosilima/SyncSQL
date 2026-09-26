using System.CommandLine;
using System.Globalization;
using SyncSql.Cli.Commands;
using SyncSql.Cli.Parsing;

namespace SyncSql.Cli.Tests;

public sealed class ParserDashboardLifecycleTests
{
    [Fact]
    public void LoopRedrawsOnKeysAndBothResizeDimensionsAndRestoresConsole()
    {
        TestTerminal terminal = new();
        terminal.Keys.Enqueue(Key(ConsoleKey.DownArrow));
        terminal.OnWait = count =>
        {
            switch (count)
            {
                case 1: break; // An idle tick must not redraw.
                case 2: terminal.Columns++; break;
                case 3: terminal.Rows++; break;
                case 4: terminal.Keys.Enqueue(Key(ConsoleKey.Q)); break;
            }
        };
        ParserDashboard.Run(Inspection(), terminal, CancellationToken.None);
        string output = terminal.Writer.ToString();
        Assert.Equal(4, output.Split("SYNCSQL / PARSER", StringSplitOptions.None).Length - 1);
        Assert.StartsWith("\u001b[?1049h\u001b[?25l", output, StringComparison.Ordinal);
        Assert.EndsWith("\u001b[0m\u001b[?25h\u001b[?1049l", output, StringComparison.Ordinal);
        Assert.False(terminal.ControlCAsInput);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationRestoresOriginalControlCMode(bool originalMode)
    {
        using CancellationTokenSource cancellation = new();
        TestTerminal terminal = new() { ControlCAsInput = originalMode, OnWait = _ => cancellation.Cancel() };
        ParserDashboard.Run(Inspection(), terminal, cancellation.Token);
        Assert.Equal(originalMode, terminal.ControlCAsInput);
        Assert.Contains("\u001b[?1049l", terminal.Writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFailureRestoresTerminalAndControlCMode()
    {
        TestTerminal terminal = new() { ReadFailure = new IOException("terminal disconnected") };
        terminal.Keys.Enqueue(Key(ConsoleKey.Q));
        Assert.Throws<IOException>(() => ParserDashboard.Run(Inspection(), terminal, CancellationToken.None));
        Assert.False(terminal.ControlCAsInput);
        Assert.EndsWith("\u001b[0m\u001b[?25h\u001b[?1049l", terminal.Writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OutputFailureStillRestoresControlCMode()
    {
        TestTerminal terminal = new() { FailedOutput = new DisconnectedWriter() };
        Assert.Throws<IOException>(() => ParserDashboard.Run(Inspection(), terminal, CancellationToken.None));
        Assert.False(terminal.ControlCAsInput);
    }

    [Fact]
    public async Task CommandUsesInteractiveTerminalAndNeverChangesInputFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            const string sql = "SELECT 1;";
            await File.WriteAllTextAsync(path, sql);
            TestTerminal terminal = new();
            terminal.Keys.Enqueue(Key(ConsoleKey.Q));
            int exit = await new RootCommand { ParserCommand.Build(terminal) }.Parse(["parser", "--file", path]).InvokeAsync();
            Assert.Equal(0, exit);
            Assert.Contains("SYNCSQL / PARSER", terminal.Writer.ToString());
            Assert.Equal(sql, await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(true, false, false, null, true)]
    [InlineData(false, true, false, null, true)]
    [InlineData(false, false, true, null, true)]
    [InlineData(false, false, false, "dumb", true)]
    [InlineData(false, false, false, "xterm", false)]
    [InlineData(false, false, false, null, false)]
    public void PlainModeHonorsRedirectsAndTerminalCapabilities(bool requested, bool input, bool output, string? term, bool expected) =>
        Assert.Equal(expected, ParserCommand.UsePlainOutput(requested, input, output, term));

    [Fact]
    public void ConsoleBoundaryReportsRedirectedInputInsteadOfBlocking()
    {
        // VSTest redirects stdin. A native ReadKey must fail promptly, never wait for a key.
        ParserTerminal terminal = new();
        Assert.True(terminal.InputRedirected);
        Assert.Same(Console.Out, terminal.Output);
        Assert.Same(Console.Error, terminal.Error);
        Assert.Equal(Console.IsOutputRedirected, terminal.OutputRedirected);
        Assert.Equal(Environment.GetEnvironmentVariable("TERM"), terminal.TerminalType);
        AssertConsoleOperation(() => Assert.True(terminal.Width >= 0));
        AssertConsoleOperation(() => Assert.True(terminal.Height >= 0));
        Assert.IsType<InvalidOperationException>(Record.Exception(() => terminal.ReadKey()));
        AssertConsoleOperation(() => _ = terminal.KeyAvailable);
        // Whether console-mode operations succeed with redirected handles is OS-dependent.
        bool mode = false;
        AssertConsoleOperation(() => mode = terminal.ControlCAsInput);
        AssertConsoleOperation(() => terminal.ControlCAsInput = mode);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        terminal.Wait(cancellation.Token);
    }

    private static void AssertConsoleOperation(Action action) =>
        Assert.True(Record.Exception(action) is null or IOException or InvalidOperationException);

    internal static ConsoleKeyInfo Key(ConsoleKey key, char character = '\0', bool shift = false, bool control = false) =>
        new(character, key, shift, false, control);

    private static SqlInspection Inspection() => SqlInspection.Parse("query.sql", "SELECT 1;", "mssql");

    private sealed class TestTerminal : ParserTerminal
    {
        private int _waits;
        public StringWriter Writer { get; } = new(CultureInfo.InvariantCulture);
        public Queue<ConsoleKeyInfo> Keys { get; } = new();
        public int Columns { get; set; } = 100;
        public int Rows { get; set; } = 24;
        public Action<int>? OnWait { get; set; }
        public IOException? ReadFailure { get; init; }
        public TextWriter? FailedOutput { get; init; }
        public override TextWriter Output => FailedOutput ?? Writer;
        public override bool InputRedirected => false;
        public override bool OutputRedirected => false;
        public override string? TerminalType => "xterm";
        public override int Width => Columns;
        public override int Height => Rows;
        public override bool KeyAvailable => Keys.Count > 0;
        public override bool ControlCAsInput { get; set; }
        public override ConsoleKeyInfo ReadKey() => ReadFailure is null ? Keys.Dequeue() : throw ReadFailure;
        public override void Wait(CancellationToken cancellationToken) => OnWait?.Invoke(++_waits);
    }

    private sealed class DisconnectedWriter : StringWriter
    {
        public override void Write(string? value) => throw new IOException("terminal disconnected");
    }
}
