using WorldMonitor.Data.Time;

namespace WorldMonitor.Caching.Tests.Fakes;

public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan by) => UtcNow += by;
}
