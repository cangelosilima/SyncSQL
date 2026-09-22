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
        using FakeOracleDatabase healthy = new() { Execute = (_, _) => null };
        int created = 0, executed = 0;
        await using (OracleExtractionConnections pool = new(() => ++created == 1 ? failed : healthy))
        {
            OracleException actual = await Assert.ThrowsAsync<OracleException>(() => pool.UseAsync(_ =>
            {
                executed++;
                return Task.FromResult(0);
            }, CancellationToken.None));
            Assert.Same(failure, actual);
            Assert.True(failed.WasDisposed);
            Assert.Equal(0, executed);

            for (int i = 0; i < 2; i++)
            {
                int result = await pool.UseAsync(connection =>
                {
                    Assert.Same(healthy, connection);
                    return Task.FromResult(++executed);
                }, CancellationToken.None);
                Assert.Equal(i + 1, result);
            }
            Assert.Equal(2, created);
            Assert.Single(healthy.Queries);
            Assert.False(healthy.WasDisposed);
        }
        Assert.True(healthy.WasDisposed);
    }
}
