using SyncSql.Core.Abstractions;

namespace SyncSql.Core.Tests.Abstractions;

public sealed class ExtractionProgressAggregatorTests
{
    [Fact]
    public void CombinesChildDeltasWithoutDoubleCountingRepeatedProgress()
    {
        RecordingProgress target = new();
        ExtractionProgressAggregator aggregator = new(target, initialCount: 10);
        IProgress<ExtractionProgress> first = aggregator.CreateChild();
        IProgress<ExtractionProgress> second = aggregator.CreateChild();

        first.Report(new("first", 2));
        second.Report(new("second", 3));
        first.Report(new("first", 2));
        first.Report(new("first", 5));
        aggregator.Report(new("finished", 999, Completed: 2, Total: 2));

        Assert.Equal([12, 15, 15, 18, 18], target.Values.Select(value => value.ObjectsExtracted));
        Assert.Equal(new ExtractionProgress("finished", 18, 2, 2), target.Values[^1]);
    }

    private sealed class RecordingProgress : IProgress<ExtractionProgress>
    {
        public List<ExtractionProgress> Values { get; } = [];
        public void Report(ExtractionProgress value) => Values.Add(value);
    }
}
