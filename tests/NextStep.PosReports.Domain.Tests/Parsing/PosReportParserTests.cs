using NextStep.PosReports.Domain.Parsing;

namespace NextStep.PosReports.Domain.Tests.Parsing;

public sealed class PosReportParserTests
{
    [Fact]
    public void Parse_SpecificationExample_ReturnsAllFields()
    {
        var report = PosReportParser.Parse(SampleReports.Example, SampleReports.ExampleReceivedAt);

        Assert.Equal(SampleReports.ExampleFlightId, report.FlightId);
        Assert.Equal("UL204", report.FlightNumber);
        Assert.Equal("RGN", report.Departure);
        Assert.Equal("BKK", report.Destination);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 12, 5, 0, TimeSpan.Zero), report.Timestamp);
        Assert.Equal(16.705, report.Latitude);
        Assert.Equal(96.2083, report.Longitude);
        Assert.Equal(450, report.GroundSpeedKnots);
        Assert.Equal(12500, report.FuelOnBoardKg);
        Assert.Equal(2800, report.FuelFlowKgPerHour);
    }

    [Fact]
    public void Parse_TakesYearAndMonthFromReceiveTime()
    {
        var receivedAt = new DateTimeOffset(2027, 2, 10, 8, 0, 0, TimeSpan.Zero);

        var report = PosReportParser.Parse(SampleReports.Example, receivedAt);

        Assert.Equal(new DateTimeOffset(2027, 2, 4, 12, 5, 0, TimeSpan.Zero), report.Timestamp);
        Assert.Equal("UL20420270204RGNBKK", report.FlightId);
    }

    [Fact]
    public void Parse_ReceiveTimeWithOffset_UsesUtcMonth()
    {
        // 1 Oct 01:00 in UTC+05:00 is still 30 Sep in UTC.
        var receivedAt = new DateTimeOffset(2026, 10, 1, 1, 0, 0, TimeSpan.FromHours(5));

        var report = PosReportParser.Parse(SampleReports.Example, receivedAt);

        Assert.Equal(9, report.Timestamp.Month);
    }

    [Fact]
    public void Parse_SurroundingWhitespace_IsIgnored()
    {
        var report = PosReportParser.Parse($"  {SampleReports.Example}\n", SampleReports.ExampleReceivedAt);

        Assert.Equal(SampleReports.ExampleFlightId, report.FlightId);
    }

    [Fact]
    public void Parse_SouthernAndWesternHemispheres_ReturnsNegativeCoordinates()
    {
        const string report = "POS/QF1.FR SYD/TO LHR/041205/S3356.4W00027.3/480/90000/7000";

        var parsed = PosReportParser.Parse(report, SampleReports.ExampleReceivedAt);

        Assert.Equal(-33.94, parsed.Latitude);
        Assert.Equal(-0.455, parsed.Longitude);
    }

    [Theory]
    [InlineData("9W123", "9W123")]
    [InlineData("QF1", "QF1")]
    [InlineData("BA2490A", "BA2490A")]
    public void Parse_FlightNumberVariants_AreAccepted(string flightNumber, string expectedFlightNumber)
    {
        var report = $"POS/{flightNumber}.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800";

        var parsed = PosReportParser.Parse(report, SampleReports.ExampleReceivedAt);

        Assert.Equal(expectedFlightNumber, parsed.FlightNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800")]       // missing POS/ prefix
    [InlineData("pos/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800")]   // lowercase prefix
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500")]        // too few parts
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800/1")] // too many parts
    [InlineData("POS/UL204 RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800")]      // missing .FR
    [InlineData("POS/UL204.FR RGNX/TO BKK/041205/N1642.3E09612.5/450/12500/2800")]  // departure not 3 letters
    [InlineData("POS/UL204.FR RGN/BKK/041205/N1642.3E09612.5/450/12500/2800")]      // missing TO
    [InlineData("POS/UL204.FR RGN/TO bkk/041205/N1642.3E09612.5/450/12500/2800")]   // lowercase destination
    [InlineData("POS/UL204.FR RGN/TO BKK/41205/N1642.3E09612.5/450/12500/2800")]    // time not 6 digits
    [InlineData("POS/UL204.FR RGN/TO BKK/001205/N1642.3E09612.5/450/12500/2800")]   // day 00
    [InlineData("POS/UL204.FR RGN/TO BKK/311205/N1642.3E09612.5/450/12500/2800")]   // 31 September
    [InlineData("POS/UL204.FR RGN/TO BKK/042405/N1642.3E09612.5/450/12500/2800")]   // hour 24
    [InlineData("POS/UL204.FR RGN/TO BKK/041260/N1642.3E09612.5/450/12500/2800")]   // minute 60
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/1642.3N09612.5E/450/12500/2800")]   // hemisphere after digits
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E9612.5/450/12500/2800")]    // longitude needs 3 degree digits
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1660.0E09612.5/450/12500/2800")]   // minutes >= 60
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N9130.0E09612.5/450/12500/2800")]   // latitude > 90
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E18130.0/450/12500/2800")]   // longitude > 180
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/0/12500/2800")]     // zero ground speed
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/-1/2800")]      // negative fuel
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/abc")]    // non-numeric fuel flow
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/99999999999/2800")] // overflow
    public void Parse_MalformedReport_ThrowsPosReportFormatException(string? rawReport)
    {
        Assert.Throws<PosReportFormatException>(() => PosReportParser.Parse(rawReport, SampleReports.ExampleReceivedAt));
    }

    [Fact]
    public void Parse_WrongSegmentCount_ExplainsTheProblem()
    {
        var exception = Assert.Throws<PosReportFormatException>(
            () => PosReportParser.Parse("POS/UL204.FR RGN/TO BKK", SampleReports.ExampleReceivedAt));

        Assert.Equal("The POS report must have 8 '/'-separated parts but has 3.", exception.Message);
    }

    [Fact]
    public void Parse_LastDayOfLeapFebruary_IsAccepted()
    {
        var receivedAt = new DateTimeOffset(2028, 2, 29, 23, 0, 0, TimeSpan.Zero);

        var report = PosReportParser.Parse("POS/UL204.FR RGN/TO BKK/292300/N1642.3E09612.5/450/12500/2800", receivedAt);

        Assert.Equal(new DateTimeOffset(2028, 2, 29, 23, 0, 0, TimeSpan.Zero), report.Timestamp);
    }
}
