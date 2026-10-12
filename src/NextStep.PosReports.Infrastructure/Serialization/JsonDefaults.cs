using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NextStep.PosReports.Infrastructure.Serialization;

/// <summary>JSON settings for every document the pipeline writes: camelCase names, nulls omitted.</summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Output is only ever served as application/json, never embedded in HTML,
        // so keep characters such as ' readable instead of escaping them to '.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
