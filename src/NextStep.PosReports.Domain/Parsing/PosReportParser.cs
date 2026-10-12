using System.Globalization;
using System.Text.RegularExpressions;
using NextStep.PosReports.Domain.Identifiers;
using NextStep.PosReports.Domain.Models;

namespace NextStep.PosReports.Domain.Parsing;

/// <summary>
/// Parses a raw POS report:
/// <c>POS/{IATA}{flightNumber}.FR {departure}/TO {destination}/{ddHHmm}/{position}/{groundSpeedKnots}/{fuelOnBoardKg}/{fuelFlowKgPerHour}</c>
/// </summary>
/// <remarks>
/// The report only carries day, hour and minute. Year and month are taken from <c>receivedAt</c>,
/// the moment the API received the report.
/// </remarks>
public static partial class PosReportParser
{
    private const string ReportPrefix = "POS/";
    private const char SegmentSeparator = '/';
    private const int ExpectedSegmentCount = 8;

    [GeneratedRegex(@"^(?<flightNumber>[A-Z0-9]{2}\d{1,4}[A-Z]?)\.FR (?<departure>[A-Z]{3})$")]
    private static partial Regex FlightSegmentPattern();

    [GeneratedRegex(@"^TO (?<destination>[A-Z]{3})$")]
    private static partial Regex DestinationSegmentPattern();

    [GeneratedRegex(@"^(?<day>\d{2})(?<hour>\d{2})(?<minute>\d{2})$")]
    private static partial Regex DayTimeSegmentPattern();

    public static PosReport Parse(string? rawReport, DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(rawReport))
        {
            throw new PosReportFormatException("The POS report is empty.");
        }

        var report = rawReport.Trim();
        if (!report.StartsWith(ReportPrefix, StringComparison.Ordinal))
        {
            throw new PosReportFormatException($"The POS report must start with '{ReportPrefix}'.");
        }

        var segments = report.Split(SegmentSeparator);
        if (segments.Length != ExpectedSegmentCount)
        {
            throw new PosReportFormatException(
                $"The POS report must have {ExpectedSegmentCount} '{SegmentSeparator}'-separated parts but has {segments.Length}.");
        }

        var (flightNumber, departure) = ParseFlightSegment(segments[1]);
        var destination = ParseDestinationSegment(segments[2]);
        var timestamp = ParseTimestamp(segments[3], receivedAt);
        var position = CoordinateParser.ParsePosition(segments[4]);
        var groundSpeedKnots = ParseInteger(segments[5], "Ground speed", minimumValue: 1);
        var fuelOnBoardKg = ParseInteger(segments[6], "Fuel on board", minimumValue: 0);
        var fuelFlowKgPerHour = ParseInteger(segments[7], "Fuel flow", minimumValue: 0);

        var flightId = FlightIdentifier.Create(flightNumber, DateOnly.FromDateTime(timestamp.UtcDateTime), departure, destination);

        return new PosReport(
            flightId,
            flightNumber,
            departure,
            destination,
            timestamp,
            position.Latitude,
            position.Longitude,
            groundSpeedKnots,
            fuelOnBoardKg,
            fuelFlowKgPerHour);
    }

    private static (string FlightNumber, string Departure) ParseFlightSegment(string segment)
    {
        var match = FlightSegmentPattern().Match(segment);
        if (!match.Success)
        {
            throw new PosReportFormatException(
                $"Flight segment '{segment}' is not in the expected format, e.g. 'UL204.FR RGN'.");
        }

        return (match.Groups["flightNumber"].Value, match.Groups["departure"].Value);
    }

    private static string ParseDestinationSegment(string segment)
    {
        var match = DestinationSegmentPattern().Match(segment);
        if (!match.Success)
        {
            throw new PosReportFormatException(
                $"Destination segment '{segment}' is not in the expected format, e.g. 'TO BKK'.");
        }

        return match.Groups["destination"].Value;
    }

    private static DateTimeOffset ParseTimestamp(string segment, DateTimeOffset receivedAt)
    {
        var match = DayTimeSegmentPattern().Match(segment);
        if (!match.Success)
        {
            throw new PosReportFormatException($"Time segment '{segment}' must be six digits in the format ddHHmm.");
        }

        var day = int.Parse(match.Groups["day"].ValueSpan, CultureInfo.InvariantCulture);
        var hour = int.Parse(match.Groups["hour"].ValueSpan, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups["minute"].ValueSpan, CultureInfo.InvariantCulture);

        var receivedAtUtc = receivedAt.UtcDateTime;
        var daysInMonth = DateTime.DaysInMonth(receivedAtUtc.Year, receivedAtUtc.Month);

        if (day < 1 || day > daysInMonth)
        {
            throw new PosReportFormatException(
                $"Day {day:00} is not valid for {receivedAtUtc:yyyy-MM}, the month the report was received in.");
        }

        if (hour > 23 || minute > 59)
        {
            throw new PosReportFormatException($"Time {hour:00}:{minute:00} in segment '{segment}' is not a valid time of day.");
        }

        return new DateTimeOffset(receivedAtUtc.Year, receivedAtUtc.Month, day, hour, minute, 0, TimeSpan.Zero);
    }

    private static int ParseInteger(string segment, string fieldName, int minimumValue)
    {
        if (!int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimumValue)
        {
            throw new PosReportFormatException(
                $"{fieldName} '{segment}' must be a whole number of at least {minimumValue}.");
        }

        return value;
    }
}
