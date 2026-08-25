using System.Text.Json;
using System.Text.Json.Serialization;

namespace GelarkBot;

internal static class JsonUtil
{
    public static readonly JsonSerializerOptions Options = Create(writeIndented: false);

    public static readonly JsonSerializerOptions Indented = Create(writeIndented: true);

    private static JsonSerializerOptions Create(bool writeIndented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = writeIndented,
    };
}
