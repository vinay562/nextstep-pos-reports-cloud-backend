using System.Globalization;

namespace NextStep.PosReports.Domain.Time;

/// <summary>
/// The two ISO-8601 UTC timestamp formats used across the pipeline.
/// Every timestamp that ends up in an S3 key or a DynamoDB sort key goes through here,
/// so values of the same kind always sort correctly as strings.
/// </summary>
public static class UtcTimestamp
{
    private const string SecondPrecisionFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    private const string MillisecondPrecisionFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>POS report time, e.g. <c>2026-09-04T12:05:00Z</c>.</summary>
    public static string ToSecondPrecision(DateTimeOffset value) =>
        value.UtcDateTime.ToString(SecondPrecisionFormat, CultureInfo.InvariantCulture);

    /// <summary>System ("now") time, e.g. <c>2026-09-04T15:12:30.123Z</c>.</summary>
    public static string ToMillisecondPrecision(DateTimeOffset value) =>
        value.UtcDateTime.ToString(MillisecondPrecisionFormat, CultureInfo.InvariantCulture);

    /// <summary>Drops sub-millisecond ticks so the in-memory value matches its formatted representation.</summary>
    public static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);

    public static bool TryParse(string? value, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp);

    public static DateTimeOffset Parse(string value) =>
        TryParse(value, out var timestamp)
            ? timestamp
            : throw new FormatException($"'{value}' is not a valid UTC timestamp.");
}
