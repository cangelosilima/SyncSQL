namespace SyncSql.Core.Abstractions;

/// <summary>Abstracts "now" so anything that stamps a timestamp (catalog generation, git commit dedupe, ...) is deterministically testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real system clock - the only implementation registered outside tests.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
