using System.Text.Json;
using WorldMonitor.Contracts.Json;
using Xunit;

namespace WorldMonitor.Contracts.Tests;

public class JsonConventionsTests
{
    private sealed record Sample
    {
        public string FirstName { get; init; } = "";
        public int? OptionalCount { get; init; }
    }

    [Fact]
    public void Serializes_camelCase_and_omits_null_optionals()
    {
        var json = JsonSerializer.Serialize(new Sample { FirstName = "ada" }, WmJson.Options);

        Assert.Contains("\"firstName\":\"ada\"", json);
        Assert.DoesNotContain("optionalCount", json);
        Assert.DoesNotContain("FirstName", json);
    }

    [Fact]
    public void Deserializes_case_insensitively()
    {
        var s = JsonSerializer.Deserialize<Sample>("{\"FirstName\":\"ada\"}", WmJson.Options);
        Assert.Equal("ada", s!.FirstName);
    }
}
