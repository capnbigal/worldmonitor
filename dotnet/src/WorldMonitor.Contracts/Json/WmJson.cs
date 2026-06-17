using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldMonitor.Contracts.Json;

/// <summary>Canonical wire-format serializer options for all World Monitor DTOs.</summary>
public static class WmJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.Strict,
    };
}
