using NextStep.PosReports.Domain.Identifiers;

namespace NextStep.PosReports.Domain.Tests.Identifiers;

public sealed class ObjectKeysTests
{
    [Fact]
    public void FlightIdentifier_SpecificationExample()
    {
        var flightId = FlightIdentifier.Create("UL204", new DateOnly(2026, 9, 4), "RGN", "BKK");

        Assert.Equal(SampleReports.ExampleFlightId, flightId);
    }

    [Fact]
    public void ForRawReport_SpecificationExample()
    {
        var key = ObjectKeys.ForRawReport(SampleReports.ExampleFlightId, SampleReports.ExampleReceivedAt);

        Assert.Equal("pos/UL20420260904RGNBKK-2026-09-04T15:12:30.123Z", key);
    }

    [Fact]
    public void ForAttachment_SpecificationExample()
    {
        var key = ObjectKeys.ForAttachment(SampleReports.ExampleFlightId, new DateTimeOffset(2026, 9, 4, 12, 5, 0, TimeSpan.Zero));

        Assert.Equal("attachment/UL20420260904RGNBKK-2026-09-04T12:05:00Z.json", key);
    }

    [Fact]
    public void ForCalculationResult_SpecificationExample()
    {
        var key = ObjectKeys.ForCalculationResult(
            SampleReports.ExampleFlightId, new DateTimeOffset(2026, 9, 4, 12, 6, 15, 842, TimeSpan.Zero));

        Assert.Equal("results/UL20420260904RGNBKK-2026-09-04T12:06:15.842Z.json", key);
    }

    [Fact]
    public void ParseRawReportKey_RoundTripsFlightIdAndReceiveTime()
    {
        var key = ObjectKeys.ForRawReport(SampleReports.ExampleFlightId, SampleReports.ExampleReceivedAt);

        var parsed = ObjectKeys.ParseRawReportKey(key);

        Assert.Equal(SampleReports.ExampleFlightId, parsed.FlightId);
        Assert.Equal(SampleReports.ExampleReceivedAt, parsed.ReceivedAt);
    }

    [Theory]
    [InlineData("attachment/UL20420260904RGNBKK-2026-09-04T15:12:30.123Z")]
    [InlineData("pos/UL20420260904RGNBKK")]
    [InlineData("pos/-2026-09-04T15:12:30.123Z")]
    [InlineData("pos/UL20420260904RGNBKK-not-a-timestamp")]
    public void ParseRawReportKey_UnexpectedKey_Throws(string objectKey)
    {
        Assert.Throws<FormatException>(() => ObjectKeys.ParseRawReportKey(objectKey));
    }
}
