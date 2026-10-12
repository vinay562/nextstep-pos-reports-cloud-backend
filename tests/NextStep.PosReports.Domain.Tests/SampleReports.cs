namespace NextStep.PosReports.Domain.Tests;

/// <summary>The worked example from the specification.</summary>
internal static class SampleReports
{
    public const string Example = "POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800";

    public const string ExampleFlightId = "UL20420260904RGNBKK";

    public static readonly DateTimeOffset ExampleReceivedAt = new(2026, 9, 4, 15, 12, 30, 123, TimeSpan.Zero);
}
