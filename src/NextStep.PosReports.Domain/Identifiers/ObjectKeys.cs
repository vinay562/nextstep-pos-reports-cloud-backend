using NextStep.PosReports.Domain.Time;

namespace NextStep.PosReports.Domain.Identifiers;

/// <summary>S3 object key conventions for the three stages of the pipeline.</summary>
public static class ObjectKeys
{
    public const string RawReportPrefix = "pos/";
    public const string AttachmentPrefix = "attachment/";
    public const string CalculationResultPrefix = "results/";

    /// <summary><c>pos/{flightId}-{receivedAt}</c>, e.g. <c>pos/UL20420260904RGNBKK-2026-09-04T15:12:30.123Z</c>.</summary>
    public static string ForRawReport(string flightId, DateTimeOffset receivedAt) =>
        $"{RawReportPrefix}{flightId}-{UtcTimestamp.ToMillisecondPrecision(receivedAt)}";

    /// <summary><c>attachment/{flightId}-{reportTimestamp}.json</c>.</summary>
    public static string ForAttachment(string flightId, DateTimeOffset reportTimestamp) =>
        $"{AttachmentPrefix}{flightId}-{UtcTimestamp.ToSecondPrecision(reportTimestamp)}.json";

    /// <summary><c>results/{flightId}-{calculatedAt}.json</c>.</summary>
    public static string ForCalculationResult(string flightId, DateTimeOffset calculatedAt) =>
        $"{CalculationResultPrefix}{flightId}-{UtcTimestamp.ToMillisecondPrecision(calculatedAt)}.json";

    /// <summary>
    /// Splits a raw report key back into flight id and receive time. The flight id never contains '-',
    /// so the first '-' after the prefix separates it from the timestamp.
    /// </summary>
    public static RawReportKey ParseRawReportKey(string objectKey)
    {
        if (!objectKey.StartsWith(RawReportPrefix, StringComparison.Ordinal))
        {
            throw new FormatException($"Object key '{objectKey}' does not start with '{RawReportPrefix}'.");
        }

        var fileName = objectKey[RawReportPrefix.Length..];
        var separatorIndex = fileName.IndexOf('-');

        if (separatorIndex <= 0 || !UtcTimestamp.TryParse(fileName[(separatorIndex + 1)..], out var receivedAt))
        {
            throw new FormatException($"Object key '{objectKey}' is not in the format '{RawReportPrefix}{{flightId}}-{{timestamp}}'.");
        }

        return new RawReportKey(fileName[..separatorIndex], receivedAt);
    }
}

public sealed record RawReportKey(string FlightId, DateTimeOffset ReceivedAt);
