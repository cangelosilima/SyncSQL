namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// A [Fact] that skips itself when the sample fleet is not switched on.
///
/// xUnit v2 has no runtime skip, so the decision is made once, at discovery time, from
/// <see cref="SampleFleet.IsEnabled"/>. The effect is that a plain `dotnet test cli/SyncSql.slnx`
/// on a machine with no containers reports every benchmark test as skipped with a message saying
/// how to run it, instead of failing.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SampleFleetFactAttribute : FactAttribute
{
    public SampleFleetFactAttribute()
    {
        if (!SampleFleet.IsEnabled)
        {
            Skip = SampleFleet.SkipReason;
        }
    }
}
