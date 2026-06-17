using Xunit;

namespace WorldMonitor.Api.Tests;

/// <summary>Groups every WebApplicationFactory-based test into a single, non-parallel collection. They share
/// the <see cref="TestDatabase.ConnectionString"/> database, and EF's migration app-lock guards only the
/// migration history — not the initial <c>CREATE DATABASE</c>. Without this, the first run on a fresh machine
/// (e.g. CI) races two hosts creating/migrating the same database at once. Serializing them removes the race.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApiIntegrationCollection
{
    public const string Name = "ApiIntegration";
}
