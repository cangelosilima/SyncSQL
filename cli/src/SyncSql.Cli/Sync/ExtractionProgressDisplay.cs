using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using SyncSql.Core.Abstractions;

namespace SyncSql.Cli.Sync;

/// <summary>One synchronized output path for log messages and the animated extraction display.</summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "StartExtraction transfers ownership of the display to the caller's await using scope.")]
internal sealed class SyncSqlTerminal(TextWriter output, bool animated, Func<(int Width, int Height)>? size = null)
{
    internal object Gate { get; } = new();
    private ExtractionProgressDisplay? _display;
    private int _rows;
    internal bool Animated => animated;

    public static SyncSqlTerminal Create() => new(Console.Out,
        !Console.IsOutputRedirected && !Console.IsErrorRedirected
        && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase),
        () => (Console.WindowWidth, Console.WindowHeight));

    public ExtractionProgressDisplay StartExtraction()
    {
        lock (Gate)
        {
            _display = new(this);
            if (animated)
            {
                output.Write("\e[?25l");
            }
            return _display;
        }
    }

    public void WriteLog(string message)
    {
        lock (Gate)
        {
            Clear();
            output.Write(message);
            Render();
        }
    }

    internal void Render()
    {
        lock (Gate)
        {
            if (!animated || _display is null)
            {
                return;
            }
            Clear();
            var (width, height) = size?.Invoke() ?? (100, 30);
            string[] lines = _display.Lines(Math.Max(10, width - 1), Math.Max(4, height - 1));
            foreach (string line in lines)
            {
                output.WriteLine(line);
            }
            _rows = lines.Length;
            output.Flush();
        }
    }

    internal void Finish(ExtractionProgressDisplay display)
    {
        lock (Gate)
        {
            if (_display != display)
            {
                return;
            }
            try
            {
                Clear();
                if (animated)
                {
                    foreach (string line in display.Lines(int.MaxValue, int.MaxValue))
                    {
                        output.WriteLine(line);
                    }
                }
            }
            finally
            {
                _display = null;
                if (animated)
                {
                    output.Write("\e[?25h");
                    output.Flush();
                }
            }
        }
    }

    private void Clear()
    {
        if (_rows == 0)
        {
            return;
        }
        output.Write(FormattableString.Invariant($"\e[{_rows}A\r\e[J"));
        _rows = 0;
    }
}

internal sealed class ExtractionProgressDisplay : IAsyncDisposable
{
    private sealed class ServerState(string name)
    {
        public string Name { get; } = name;
        public string Status { get; set; } = "Queued";
        public ExtractionProgress Progress { get; set; } = new("Waiting for a slot");
        public Stopwatch Elapsed { get; } = new();
        public bool Finished => Status is "Done" or "Failed" or "Skipped" or "Cancelled";
    }

    private sealed class Observer(ExtractionProgressDisplay display, string server) : IProgress<ExtractionProgress>
    {
        public void Report(ExtractionProgress value) => display.Update(server, value);
    }

    private readonly SyncSqlTerminal _terminal;
    private readonly Dictionary<string, ServerState> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _animation;
    private bool _disposed;

    internal ExtractionProgressDisplay(SyncSqlTerminal terminal)
    {
        _terminal = terminal;
        _animation = terminal.Animated ? AnimateAsync() : Task.CompletedTask;
    }

    public void Add(string server)
    {
        lock (_terminal.Gate)
        {
            _servers.TryAdd(server, new(server));
        }
    }

    public IProgress<ExtractionProgress> Start(string server)
    {
        lock (_terminal.Gate)
        {
            _servers[server].Status = "Extracting";
            _servers[server].Progress = new("Connecting");
            _servers[server].Elapsed.Start();
        }
        return new Observer(this, server);
    }

    private void Update(string server, ExtractionProgress progress)
    {
        lock (_terminal.Gate)
        {
            ServerState state = _servers[server];
            if (!state.Finished)
            {
                state.Progress = progress;
                state.Status = progress.Activity == "Writing files" ? "Writing" : "Extracting";
            }
        }
    }

    public void Complete(string server, string status, string detail, int? objects = null)
    {
        lock (_terminal.Gate)
        {
            ServerState state = _servers[server];
            state.Status = status;
            state.Elapsed.Stop();
            state.Progress = new(detail, objects ?? state.Progress.ObjectsExtracted);
        }
    }

    internal string[] Lines(int width, int height)
    {
        lock (_terminal.Gate)
        {
            int finished = _servers.Values.Count(s => s.Finished);
            int total = _servers.Count;
            int percent = total == 0 ? 0 : finished * 100 / total;
            int active = _servers.Values.Count(s => !s.Finished && s.Status != "Queued");
            int failed = _servers.Values.Count(s => s.Status == "Failed");
            int objects = _servers.Values.Sum(s => s.Progress.ObjectsExtracted);
            char spinner = "|/-\\"[(int)(_elapsed.ElapsedMilliseconds / 100 % 4)];
            string bar = new string('#', percent / 5).PadRight(20, '-');
            List<string> lines =
            [
                FormattableString.Invariant($"Extraction [{bar}] {percent}% | {finished}/{total} finished | {active} active | {failed} failed"),
                FormattableString.Invariant($"{objects} objects extracted | elapsed {_elapsed.Elapsed:hh\\:mm\\:ss} | totals include discovered servers"),
            ];
            int capacity = Math.Max(1, height - 3);
            int pages = Math.Max(1, (int)Math.Ceiling((double)total / capacity));
            int page = (int)(_elapsed.ElapsedMilliseconds / 3000 % pages);
            foreach (ServerState state in _servers.Values.Skip(page * capacity).Take(capacity))
            {
                string marker = state.Finished ? state.Status == "Done" ? "+" : "!" : state.Status == "Queued" ? "." : spinner.ToString();
                string units = state.Progress.Total is { } count
                    ? FormattableString.Invariant($" ({state.Progress.Completed}/{count})") : string.Empty;
                lines.Add(FormattableString.Invariant($"{marker} {state.Name} | {state.Status}{units} | {state.Progress.ObjectsExtracted} objects | {state.Elapsed.Elapsed:hh\\:mm\\:ss} | {state.Progress.Activity}"));
            }
            if (pages > 1)
            {
                lines.Add(FormattableString.Invariant($"Servers page {page + 1}/{pages} (rotates every 3s)"));
            }
            return [.. lines.Select(line => Fit(line, width))];
        }
    }

    private static string Fit(string value, int width)
    {
        string clean = new([.. value.Select(c => char.IsControl(c) ? ' ' : c)]);
        return clean.Length <= width ? clean : clean[..(width - 3)] + "...";
    }

    private async Task AnimateAsync()
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                _terminal.Render();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        await _stop.CancelAsync();
        try
        {
            await _animation;
        }
        finally
        {
            lock (_terminal.Gate)
            {
                foreach (ServerState state in _servers.Values.Where(s => !s.Finished))
                {
                    Complete(state.Name, "Cancelled", "Run stopped");
                }
                _elapsed.Stop();
                _terminal.Finish(this);
            }
            _stop.Dispose();
        }
    }
}
