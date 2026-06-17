namespace WorldMonitor.Data.Time;

/// <summary>Abstracts "now" so freshness logic is deterministic in tests.
/// NOTE: SQL-side timestamps use SYSUTCDATETIME() (server time) to avoid app/DB clock skew;
/// IClock is for app-side logic (P1a-2 wrapper/classifier).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
