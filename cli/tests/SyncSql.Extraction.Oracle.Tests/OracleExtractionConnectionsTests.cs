using Oracle.ManagedDataAccess.Client;

namespace SyncSql.Extraction.Oracle.Tests;

public sealed class OracleExtractionConnectionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSessionIsDisposedAndReplacedBeforeRunningWork(bool failOpen)
    {
        OracleException failure = FakeOracleDatabase.Error(3113);
        using FakeOracleDatabase failed = new()
        {
            OpenFailure = failOpen ? failure : null,
            Execute = (_, _) => throw failure,
        };
        using FakeOracleDatabase first = new() { Execute = (_, _) => null };
        using FakeOracleDatabase second = new() { Execute = (_, _) => null };
        FakeOracleDatabase[] sessions = [failed, first, second];
        int created = 0, executed = 0;
        OracleExtractionConnections connections = new(() => sessions[created++]);
        OracleException actual = await Assert.ThrowsAsync<OracleException>(() => connections.UseAsync(_ =>
        {
            executed++;
            return Task.FromResult(0);
        }, CancellationToken.None));
        Assert.Same(failure, actual);
        Assert.True(failed.WasDisposed);
        Assert.Equal(0, executed);

        for (int i = 1; i < sessions.Length; i++)
        {
            int result = await connections.UseAsync(connection =>
            {
                Assert.Same(sessions[i], connection);
                Assert.False(sessions[i].WasDisposed);
                return Task.FromResult(++executed);
            }, CancellationToken.None);
            Assert.Equal(i, result);
            Assert.Single(sessions[i].Queries);
            Assert.True(sessions[i].WasDisposed);
        }
        Assert.Equal(3, created);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkFailureOrCancellationDisposesLeaseBeforeReturning(bool cancel)
    {
        using CancellationTokenSource cancellation = new();
        using FakeOracleDatabase session = new() { Execute = (_, _) => null };
        OracleExtractionConnections connections = new(() => session);
        Exception failure = cancel ? new OperationCanceledException(cancellation.Token) : FakeOracleDatabase.Error(3113);
        Exception? actual = await Record.ExceptionAsync(() => connections.UseAsync(_ =>
        {
            if (cancel) { cancellation.Cancel(); }
            return Task.FromException<int>(failure);
        }, cancellation.Token));
        Assert.Same(failure, actual);
        Assert.True(session.WasDisposed);
    }
}
