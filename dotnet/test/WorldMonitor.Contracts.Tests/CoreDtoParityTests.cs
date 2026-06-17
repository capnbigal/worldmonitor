using System.Text.Json;
using WorldMonitor.Contracts.Core;
using WorldMonitor.Contracts.Json;
using Xunit;

namespace WorldMonitor.Contracts.Tests;

public class CoreDtoParityTests
{
    [Fact]
    public void GeoCoordinates_wire_shape()
    {
        var json = JsonSerializer.Serialize(new GeoCoordinates { Latitude = 61.12, Longitude = -149.9 }, WmJson.Options);
        Assert.Equal("{\"latitude\":61.12,\"longitude\":-149.9}", json);
    }

    [Fact]
    public void PaginationResponse_wire_shape()
    {
        var json = JsonSerializer.Serialize(new PaginationResponse { NextCursor = "abc", TotalCount = 7 }, WmJson.Options);
        Assert.Equal("{\"nextCursor\":\"abc\",\"totalCount\":7}", json);
    }

    [Fact]
    public void FieldViolation_roundtrips()
    {
        var parsed = JsonSerializer.Deserialize<FieldViolation>(
            "{\"field\":\"id\",\"description\":\"required\"}", WmJson.Options);
        Assert.Equal("id", parsed!.Field);
        Assert.Equal("required", parsed.Description);
    }
}
