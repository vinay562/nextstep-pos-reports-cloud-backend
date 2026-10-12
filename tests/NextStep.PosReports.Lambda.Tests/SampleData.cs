namespace NextStep.PosReports.Lambda.Tests;

/// <summary>The worked example from the specification, at every stage of the pipeline.</summary>
internal static class SampleData
{
    public const string RawReport = "POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800";

    public const string FlightId = "UL20420260904RGNBKK";

    public static readonly DateTimeOffset ReceivedAt = new(2026, 9, 4, 15, 12, 30, 123, TimeSpan.Zero);
    public static readonly DateTimeOffset ReportTimestamp = new(2026, 9, 4, 12, 5, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset CalculatedAt = new(2026, 9, 4, 12, 6, 15, 842, TimeSpan.Zero);

    public const string RawReportKey = "pos/UL20420260904RGNBKK-2026-09-04T15:12:30.123Z";
    public const string AttachmentKey = "attachment/UL20420260904RGNBKK-2026-09-04T12:05:00Z.json";
    public const string ResultKey = "results/UL20420260904RGNBKK-2026-09-04T12:06:15.842Z.json";

    public const string AttachmentJson =
        """{"flightId":"UL20420260904RGNBKK","flightNumber":"UL204","departure":"RGN","destination":"BKK","timestamp":"2026-09-04T12:05:00Z","latitude":16.705,"longitude":96.2083,"groundSpeedKnots":450,"fuelOnBoardKg":12500,"fuelFlowKgPerHour":2800}""";

    public const string ResultJson =
        """{"flightId":"UL20420260904RGNBKK","timestamp":"2026-09-04T12:06:15.842Z","input":{"currentLatitude":16.705,"currentLongitude":96.2083,"destination":"BKK","groundSpeedKnots":450,"fuelOnBoardKg":12500,"fuelFlowKgPerHour":2800},"remainingFlightTimeMinutes":43,"estimatedFuelAtArrivalKg":10513,"lowFuelWarning":false}""";

    public const string CalculationRequestJson = """{"flightId":"UL20420260904RGNBKK","timestamp":"2026-09-04T12:05:00Z"}""";
}
