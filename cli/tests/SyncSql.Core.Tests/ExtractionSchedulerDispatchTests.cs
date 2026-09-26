using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests;

public sealed class ExtractionSchedulerDispatchTests
{
    [Fact]
    public async Task CancellationAfterDispatchButBeforeExecutionNeverInvokesWork()
    {
        ManualTaskScheduler dispatcher = new();
        ExtractionWorkScheduler scheduler = new(1, dispatcher);
        using CancellationTokenSource cancellation = new();
        bool invoked = false;
        Task<int[]> run = scheduler.RunAsync<int>([(DatabaseEngine.MsSql, _ =>
        {
            invoked = true;
            return Task.FromResult(42);
        })], cancellation.Token);
        Assert.False(run.IsCompleted);
        Assert.Equal(1, dispatcher.PendingCount);

        cancellation.Cancel();
        dispatcher.Drain();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(invoked);
        Task<int[]> retry = scheduler.RunAsync<int>([(DatabaseEngine.MsSql, _ => Task.FromResult(7))], CancellationToken.None);
        dispatcher.Drain();
        int[] retried = await retry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([7], retried);
    }

    [Fact]
    public async Task EqualLoadEnginesRotateInsteadOfStarvingTheSecondEngine()
    {
        ManualTaskScheduler dispatcher = new();
        ExtractionWorkScheduler scheduler = new(1, dispatcher);
        List<string> order = [];
        Task<int[]> run = scheduler.RunAsync<int>(new[] { DatabaseEngine.MsSql, DatabaseEngine.Oracle }.SelectMany(engine =>
            Enumerable.Range(0, 3).Select(index => (engine, (Func<ExtractionWorkContext, Task<int>>)(_ =>
            {
                order.Add($"{engine}:{index}");
                return Task.FromResult(index);
            })))), CancellationToken.None);
        dispatcher.Drain();

        int[] results = await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([0, 1, 2, 0, 1, 2], results);
        Assert.Equal(["MsSql:0", "Oracle:0", "MsSql:1", "Oracle:1", "MsSql:2", "Oracle:2"], order);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidBudgetsAreRejected(int budget) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExtractionWorkScheduler(budget));

    private sealed class ManualTaskScheduler : TaskScheduler
    {
        private readonly Queue<Task> _pending = new();
        public int PendingCount => _pending.Count;
        protected override IEnumerable<Task> GetScheduledTasks() => _pending.ToArray();
        protected override void QueueTask(Task task) => _pending.Enqueue(task);
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
        public void Drain()
        {
            while (_pending.TryDequeue(out Task? task)) { TryExecuteTask(task); }
        }
    }
}
