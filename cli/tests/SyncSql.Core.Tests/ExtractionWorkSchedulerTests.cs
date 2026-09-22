using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests;

public sealed class ExtractionWorkSchedulerTests
{
    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(3, 2, 1)]
    [InlineData(5, 3, 2)]
    public async Task OddBudgetsShareRemainderWithoutExceedingGlobalLimit(int limit, int sqlShare, int oracleShare)
    {
        ExtractionWorkScheduler scheduler = new(limit);
        TaskCompletionSource full = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int sqlStarted = 0, oracleStarted = 0, started = 0;
        Task<int[]> run = scheduler.RunAsync(new[] { DatabaseEngine.MsSql, DatabaseEngine.Oracle }.SelectMany(engine =>
            Enumerable.Range(0, 7).Select(index => (engine, (Func<ExtractionWorkContext, Task<int>>)(async context =>
            {
                if (engine == DatabaseEngine.MsSql) { Interlocked.Increment(ref sqlStarted); }
                else { Interlocked.Increment(ref oracleStarted); }
                if (Interlocked.Increment(ref started) == limit) { full.TrySetResult(); }
                await release.Task.WaitAsync(context.CancellationToken);
                return index;
            })))), CancellationToken.None);
        try
        {
            await full.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(sqlShare, Volatile.Read(ref sqlStarted));
            Assert.Equal(oracleShare, Volatile.Read(ref oracleStarted));
        }
        finally
        {
            release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(7, sqlStarted);
        Assert.Equal(7, oracleStarted);
    }

    [Theory]
    [InlineData(DatabaseEngine.MsSql)]
    [InlineData(DatabaseEngine.Oracle)]
    public async Task BalancesEnginesThenTransfersAllFreedSlots(DatabaseEngine finishingEngine)
    {
        ExtractionWorkScheduler scheduler = new(4);
        TaskCompletionSource initial = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource transferred = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finishRest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        object gate = new();
        int shortActive = 0, longActive = 0, peak = 0;
        async Task<int> Work(DatabaseEngine engine, int index, ExtractionWorkContext context)
        {
            bool shortJob = engine == finishingEngine;
            lock (gate)
            {
                if (shortJob) { shortActive++; } else { longActive++; }
                peak = Math.Max(peak, shortActive + longActive);
                if (shortActive == 2 && longActive == 2) { initial.TrySetResult(); }
                if (longActive == 4) { transferred.TrySetResult(); }
            }
            try { await (shortJob ? finishFirst.Task : finishRest.Task).WaitAsync(context.CancellationToken); return index; }
            finally { lock (gate) { if (shortJob) { shortActive--; } else { longActive--; } } }
        }
        var jobs = new[] { DatabaseEngine.MsSql, DatabaseEngine.Oracle }.SelectMany(engine =>
            Enumerable.Range(0, engine == finishingEngine ? 2 : 6).Select(index =>
                (engine, (Func<ExtractionWorkContext, Task<int>>)(context => Work(engine, index, context)))));
        Task<int[]> run = scheduler.RunAsync(jobs, CancellationToken.None);
        try
        {
            await initial.Task.WaitAsync(TimeSpan.FromSeconds(10));
            lock (gate) { Assert.Equal(2, shortActive); Assert.Equal(2, longActive); }
            finishFirst.SetResult();
            await transferred.Task.WaitAsync(TimeSpan.FromSeconds(10));
            lock (gate) { Assert.Equal(0, shortActive); Assert.Equal(4, longActive); }
        }
        finally
        {
            finishFirst.TrySetResult(); finishRest.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(4, peak);
        Assert.Equal(8, (await run).Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task NestedWorkReleasesParentsAndUsesEntireBudget(int limit)
    {
        ExtractionWorkScheduler scheduler = new(limit);
        TaskCompletionSource full = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int started = 0, active = 0, exceeded = 0;
        Task<int[][][]> run = scheduler.RunAsync<int[][]>([(DatabaseEngine.Oracle, root =>
            root.RunChildrenAsync<int[]>([schema => schema.RunChildrenAsync(Enumerable.Range(0, 8).Select(index =>
                (Func<ExtractionWorkContext, Task<int>>)(async child =>
                {
                    if (Interlocked.Increment(ref active) > limit) { Interlocked.Exchange(ref exceeded, 1); }
                    if (Interlocked.Increment(ref started) == limit) { full.TrySetResult(); }
                    try { await release.Task.WaitAsync(child.CancellationToken); return index; }
                    finally { Interlocked.Decrement(ref active); }
                })))]))], CancellationToken.None);
        try
        {
            await full.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(limit, Volatile.Read(ref started));
        }
        finally
        {
            release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(0, exceeded);
        Assert.Equal(Enumerable.Range(0, 8), (await run)[0][0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAndFailureDrainChildrenAndLeaveSchedulerReusable(bool fail)
    {
        ExtractionWorkScheduler scheduler = new(2);
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource full = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource trigger = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int started = 0, stopped = 0;
        Task<int[][]> run = scheduler.RunAsync<int[]>([(DatabaseEngine.Oracle, root => root.RunChildrenAsync(
            Enumerable.Range(0, 8).Select(index => (Func<ExtractionWorkContext, Task<int>>)(async child =>
            {
                if (Interlocked.Increment(ref started) == 2) { full.TrySetResult(); }
                try
                {
                    if (fail && index == 0)
                    {
                        await trigger.Task.WaitAsync(child.CancellationToken);
                        throw new InvalidOperationException("failed child");
                    }
                    await Task.Delay(Timeout.InfiniteTimeSpan, child.CancellationToken);
                    return index;
                }
                finally { Interlocked.Increment(ref stopped); }
            }))))], cancellation.Token);
        try
        {
            await full.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (fail)
            {
                trigger.SetResult();
                await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            else
            {
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
            }
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) when (fail) { }
        }
        Assert.Equal(2, started);
        Assert.Equal(2, stopped);
        int[] result = await scheduler.RunAsync<int>([(DatabaseEngine.MsSql, _ => Task.FromResult(42))], CancellationToken.None);
        Assert.Equal([42], result);
    }
}
