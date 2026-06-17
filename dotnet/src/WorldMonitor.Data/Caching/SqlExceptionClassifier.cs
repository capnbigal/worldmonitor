using System.Collections.Frozen;
using Microsoft.Data.SqlClient;

namespace WorldMonitor.Data.Caching;

/// <summary>Classifies SQL Server error numbers as transient (retry) vs permanent (surface).
/// Mirrors the legacy nonRetryable / PERMANENT_4XX distinction.</summary>
public static class SqlExceptionClassifier
{
    private static readonly FrozenSet<int> Transient = new[]
    {
        -2,     // command timeout
        1205,   // deadlock victim
        1222,   // lock request timeout
        49918,  // cannot process request — not enough resources
        49919,  // too many operations
        49920,  // too busy
        4060,   // cannot open database (contended)
        40197, 40501, 40613, 10928, 10929, 10053, 10054, 10060, 233, 64, // connectivity/throttle
    }.ToFrozenSet();

    public static bool IsTransient(int number) => Transient.Contains(number);

    /// <summary>True if any error in the SqlException is transient.</summary>
    public static bool IsTransient(SqlException ex)
    {
        foreach (SqlError e in ex.Errors)
            if (Transient.Contains(e.Number)) return true;
        return false;
    }
}
