using WorldMonitor.Data.Caching;
using Xunit;

namespace WorldMonitor.Data.Tests.Unit;

public class SqlExceptionClassifierTests
{
    [Theory]
    [InlineData(-2, true)]    // timeout
    [InlineData(1205, true)]  // deadlock victim
    [InlineData(49918, true)] // not enough resources / throttling
    [InlineData(4060, true)]  // cannot open database (transient on Azure/contended)
    public void Transient_numbers_are_retryable(int number, bool expected)
        => Assert.Equal(expected, SqlExceptionClassifier.IsTransient(number));

    [Theory]
    [InlineData(2627)]  // unique constraint violation
    [InlineData(547)]   // FK/check constraint
    [InlineData(229)]   // permission denied
    public void Permanent_numbers_are_not_retryable(int number)
        => Assert.False(SqlExceptionClassifier.IsTransient(number));
}
