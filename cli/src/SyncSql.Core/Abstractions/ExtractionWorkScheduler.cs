using SyncSql.Core.Domain;

namespace SyncSql.Core.Abstractions;

/// <summary>A shared, work-conserving budget. Runnable engines with fewer active jobs get the next slot.</summary>
public sealed class ExtractionWorkScheduler
{
    private readonly object _gate = new();
    private readonly Dictionary<DatabaseEngine, Queue<WorkItem>> _queues = [];
    private readonly Dictionary<DatabaseEngine, int> _active = [];
    private int _running;
    private int _nextEngine;

    public ExtractionWorkScheduler(int maxParallelism)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);
        MaxParallelism = maxParallelism;
    }

    public int MaxParallelism { get; }

    public Task<T[]> RunAsync<T>(
        IEnumerable<(DatabaseEngine Engine, Func<ExtractionWorkContext, Task<T>> Work)> jobs,
        CancellationToken cancellationToken) => RunBatchAsync(jobs, null, cancellationToken);

    internal async Task<T[]> RunBatchAsync<T>(
        IEnumerable<(DatabaseEngine Engine, Func<ExtractionWorkContext, Task<T>> Work)> jobs,
        ExtractionWorkContext? parent, CancellationToken cancellationToken)
    {
        using CancellationTokenSource batch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        List<WorkItem<T>> items = [.. jobs.Select(job => new WorkItem<T>(job.Engine, job.Work, batch))];
        List<CancellationTokenRegistration> registrations = [];
        try
        {
            lock (_gate)
            {
                foreach (WorkItem<T> item in items)
                {
                    if (!_queues.TryGetValue(item.Engine, out Queue<WorkItem>? queue))
                    {
                        _queues[item.Engine] = queue = new();
                        _active[item.Engine] = 0;
                    }
                    queue.Enqueue(item);
                    registrations.Add(batch.Token.Register(() =>
                    {
                        lock (_gate)
                        {
                            if (!item.Started) { item.Cancel(); }
                            Dispatch();
                        }
                    }));
                }
                // Enqueue children before returning the parent's slot, so another engine cannot
                // borrow it in the gap between discovery finishing and child work becoming runnable.
                if (parent is not null) { ReleaseCore(parent); }
                Dispatch();
            }
            return await Task.WhenAll(items.Select(item => item.Completion.Task));
        }
        finally
        {
            foreach (CancellationTokenRegistration registration in registrations) { await registration.DisposeAsync(); }
        }
    }

    private void Dispatch()
    {
        DatabaseEngine[] engines = [.. _queues.Keys];
        while (_running < MaxParallelism)
        {
            int selected = -1;
            for (int offset = 0; offset < engines.Length; offset++)
            {
                int index = (_nextEngine + offset) % engines.Length;
                Queue<WorkItem> queue = _queues[engines[index]];
                while (queue.TryPeek(out WorkItem? head) && head.IsCompleted) { queue.Dequeue(); }
                if (queue.Count > 0 && (selected < 0 || _active[engines[index]] < _active[engines[selected]]))
                {
                    selected = index;
                }
            }
            if (selected < 0) { return; }
            DatabaseEngine engine = engines[selected];
            WorkItem item = _queues[engine].Dequeue();
            item.Started = true;
            _active[engine]++;
            _running++;
            _nextEngine = (selected + 1) % engines.Length;
            ExtractionWorkContext context = new(this, engine, item.Token);
            _ = Task.Run(async () =>
            {
                try { await item.ExecuteAsync(context); }
                finally
                {
                    lock (_gate)
                    {
                        ReleaseCore(context);
                        Dispatch();
                    }
                }
            }, CancellationToken.None);
        }
    }

    private void ReleaseCore(ExtractionWorkContext context)
    {
        if (context.Released) { return; }
        context.Released = true;
        _active[context.Engine]--;
        _running--;
    }

    private abstract class WorkItem(DatabaseEngine engine, CancellationTokenSource batch)
    {
        public DatabaseEngine Engine { get; } = engine;
        protected CancellationTokenSource Batch { get; } = batch;
        public CancellationToken Token { get; } = batch.Token;
        public bool Started { get; set; }
        public abstract bool IsCompleted { get; }
        public abstract void Cancel();
        public abstract Task ExecuteAsync(ExtractionWorkContext context);
    }

    private sealed class WorkItem<T>(DatabaseEngine engine, Func<ExtractionWorkContext, Task<T>> work,
        CancellationTokenSource batch) : WorkItem(engine, batch)
    {
        public TaskCompletionSource<T> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool IsCompleted => Completion.Task.IsCompleted;
        public override void Cancel() => Completion.TrySetCanceled(Token);
        public override async Task ExecuteAsync(ExtractionWorkContext context)
        {
            try
            {
                Token.ThrowIfCancellationRequested();
                Completion.TrySetResult(await work(context));
            }
            catch (OperationCanceledException) when (Token.IsCancellationRequested) { Cancel(); }
            catch (Exception ex)
            {
                // Keep the failing job incomplete until cancellation has been delivered. This
                // ensures its batch cannot dispose the token source while we're still using it.
                await Batch.CancelAsync();
                Completion.TrySetException(ex);
            }
        }
    }
}

/// <summary>A running job can replace itself with finer-grained work without holding a slot while waiting.</summary>
public sealed class ExtractionWorkContext
{
    private readonly ExtractionWorkScheduler _scheduler;
    internal ExtractionWorkContext(ExtractionWorkScheduler scheduler, DatabaseEngine engine, CancellationToken token)
    {
        _scheduler = scheduler;
        Engine = engine;
        CancellationToken = token;
    }

    internal DatabaseEngine Engine { get; }
    internal bool Released { get; set; }
    public CancellationToken CancellationToken { get; }

    public Task<T[]> RunChildrenAsync<T>(IEnumerable<Func<ExtractionWorkContext, Task<T>>> jobs) =>
        _scheduler.RunBatchAsync(jobs.Select(job => (Engine, job)), this, CancellationToken);
}
