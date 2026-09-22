namespace SyncSql.Core.Abstractions;

/// <summary>Serializes progress from independent jobs and combines their local object counts.</summary>
public sealed class ExtractionProgressAggregator(IProgress<ExtractionProgress>? target, int initialCount = 0)
    : IProgress<ExtractionProgress>
{
    private readonly object _gate = new();
    private readonly IProgress<ExtractionProgress>? _target = target;
    private int _count = initialCount;

    public IProgress<ExtractionProgress> CreateChild() => new Child(this);

    public void Report(ExtractionProgress value)
    {
        lock (_gate) { _target?.Report(value with { ObjectsExtracted = _count }); }
    }

    private sealed class Child(ExtractionProgressAggregator parent) : IProgress<ExtractionProgress>
    {
        private int _previousCount;
        public void Report(ExtractionProgress value)
        {
            lock (parent._gate)
            {
                parent._count += value.ObjectsExtracted - _previousCount;
                _previousCount = value.ObjectsExtracted;
                parent._target?.Report(value with { ObjectsExtracted = parent._count });
            }
        }
    }
}
