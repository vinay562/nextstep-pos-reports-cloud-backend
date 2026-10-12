using System.Text.Json;
using System.Text.Json.Serialization;

namespace NextStep.PosReports.Domain.Time;

/// <summary>Serializes a timestamp as <c>yyyy-MM-ddTHH:mm:ssZ</c> (POS report time).</summary>
public sealed class SecondPrecisionUtcTimestampConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        UtcTimestamp.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(UtcTimestamp.ToSecondPrecision(value));
}

/// <summary>Serializes a timestamp as <c>yyyy-MM-ddTHH:mm:ss.fffZ</c> (system time).</summary>
public sealed class MillisecondPrecisionUtcTimestampConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        UtcTimestamp.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(UtcTimestamp.ToMillisecondPrecision(value));
}
