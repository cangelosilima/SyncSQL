using System.Diagnostics;
using SyncSql.Core.Abstractions;

namespace SyncSql.Cli.Sync;

/// <summary>Shares sync's synchronized terminal, with periodic plain output for redirected runs.</summary>
internal sealed class CatalogProgressDisplay : IProgress<CatalogProgress>, ITerminalProgressDisplay, IAsyncDisposable
{
    private readonly SyncSqlTerminal _terminal;
    private readonly Action<string> _log;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Stopwatch _phase = Stopwatch.StartNew();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(100));
    private readonly Task _animation;
    private CatalogProgress _progress = new("Starting");
    private string? _status;
    private int _nodes;
    private int _edges;
    private TimeSpan _lastLog;
    private TimeSpan _lastMemorySample = TimeSpan.MinValue;
    private long _workingSet;
    private long _peakWorkingSet;
    private long _managed;
    private bool _disposed;

    internal CatalogProgressDisplay(SyncSqlTerminal terminal, Action<string> log)
    {
        _terminal = terminal;
        _log = log;
        _animation = AnimateAsync();
    }

    public void Report(CatalogProgress value)
    {
        lock (_terminal.Gate)
        {
            if (_status is not null)
            {
                return;
            }
            bool changed = value.Activity != _progress.Activity;
            if (changed)
            {
                _phase.Restart();
            }
            _progress = value;
            _nodes = value.Nodes ?? _nodes;
            _edges = value.Edges ?? _edges;
            if (!_terminal.Animated && changed)
            {
                LogProgress();
            }
        }
    }

    public void Complete(string status = "Done")
    {
        lock (_terminal.Gate)
        {
            if (_status is not null)
            {
                return;
            }
            _status = status;
            _elapsed.Stop();
            _phase.Stop();
            _lastMemorySample = TimeSpan.MinValue;
            if (!_terminal.Animated)
            {
                LogProgress();
            }
        }
    }

    public string[] Lines(int width, int height)
    {
        lock (_terminal.Gate)
        {
            SampleMemory();
            string count = _progress.Total is { } total
                ? FormattableString.Invariant($"{_progress.Completed}/{total} {_progress.Unit} ({(total == 0 ? 100 : (long)_progress.Completed * 100 / total)}%)")
                : FormattableString.Invariant($"{_progress.Completed} {_progress.Unit}");
            string marker = _status ?? (_terminal.Animated
                ? "|/-\\"[(int)(_elapsed.ElapsedMilliseconds / 100 % 4)].ToString() : "Running");
            string color = _status switch { "Done" => TerminalColors.Green, "Failed" => TerminalColors.Red, "Cancelled" => TerminalColors.Yellow, _ => TerminalColors.Cyan };
            string[] lines =
            [
                TerminalColors.Wrap($"Catalog {marker} | {_progress.Activity} | {count}", color, _terminal.ColorEnabled),
                TerminalColors.Wrap(FormattableString.Invariant($"{_nodes} nodes | {_edges} edges | elapsed {_elapsed.Elapsed:hh\\:mm\\:ss} | phase {_phase.Elapsed:hh\\:mm\\:ss}"), TerminalColors.Dim, _terminal.ColorEnabled),
                TerminalColors.Wrap(FormattableString.Invariant($"RAM {_workingSet / 1048576.0:F0} MiB | peak {_peakWorkingSet / 1048576.0:F0} MiB | managed {_managed / 1048576.0:F0} MiB"), TerminalColors.Dim, _terminal.ColorEnabled),
                _progress.Current ?? "",
            ];
            return [.. lines.Take(Math.Max(1, height)).Select(line => TerminalColors.Fit(line, width))];
        }
    }

    private void SampleMemory()
    {
        if (_lastMemorySample != TimeSpan.MinValue && _elapsed.Elapsed - _lastMemorySample < TimeSpan.FromSeconds(1))
        {
            return;
        }
        using Process process = Process.GetCurrentProcess();
        _workingSet = process.WorkingSet64;
        _peakWorkingSet = process.PeakWorkingSet64;
        _managed = GC.GetTotalMemory(forceFullCollection: false);
        _lastMemorySample = _elapsed.Elapsed;
    }

    private void LogProgress()
    {
        _log(string.Join(" | ", Lines(int.MaxValue, 4).Where(line => line.Length > 0)));
        _lastLog = _elapsed.Elapsed;
    }

    private async Task AnimateAsync()
    {
        while (await _timer.WaitForNextTickAsync())
        {
            lock (_terminal.Gate)
            {
                if (_terminal.Animated)
                {
                    _terminal.Render();
                }
                else if (_status is null && _elapsed.Elapsed - _lastLog >= TimeSpan.FromSeconds(10))
                {
                    LogProgress();
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Dispose();
        try
        {
            await _animation;
        }
        finally
        {
            Complete("Cancelled");
            _terminal.Finish(this);
        }
    }
}
