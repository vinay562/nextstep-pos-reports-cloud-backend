using NextStep.PosReports.Domain.Airports;
using NextStep.PosReports.Domain.Calculation;
using NextStep.PosReports.Domain.Models;
using NextStep.PosReports.Domain.Parsing;

namespace NextStep.PosReports.Domain.Tests.Calculation;

public sealed class FlightEstimatorTests
{
    private static readonly DateTimeOffset CalculatedAt = new(2026, 9, 4, 12, 6, 15, 842, TimeSpan.Zero);

    [Fact]
    public void SpecificationExample_MatchesWorkedResult()
    {
        var report = PosReportParser.Parse(SampleReports.Example, SampleReports.ExampleReceivedAt);

        var estimate = FlightEstimator.Estimate(report, AirportDirectory.GetLocation(report.Destination));
        var result = FlightCalculationResult.Create(report, estimate, CalculatedAt);

        Assert.Equal(43, result.RemainingFlightTimeMinutes);
        Assert.Equal(10513, result.EstimatedFuelAtArrivalKg);
        Assert.False(result.LowFuelWarning);
        Assert.Equal(FlightStatus.Calculated, result.Status);
    }

    [Fact]
    public void SpecificationExample_ResultCarriesItsInputs()
    {
        var report = PosReportParser.Parse(SampleReports.Example, SampleReports.ExampleReceivedAt);

        var estimate = FlightEstimator.Estimate(report, AirportDirectory.GetLocation(report.Destination));
        var result = FlightCalculationResult.Create(report, estimate, CalculatedAt);

        Assert.Equal(SampleReports.ExampleFlightId, result.FlightId);
        Assert.Equal(CalculatedAt, result.Timestamp);
        Assert.Equal(new FlightCalculationInput(16.705, 96.2083, "BKK", 450, 12500, 2800), result.Input);
    }

    [Fact]
    public void Estimate_NotEnoughFuel_FlagsLowFuelWarning()
    {
        // Singapore → London is ~5,900 NM: ~12 h at 480 kt, burning 7,000 kg/h needs far more than 20,000 kg.
        var report = PosReportParser.Parse(
            "POS/SQ322.FR SIN/TO LHR/041205/N0121.9E10359.5/480/20000/7000",
            SampleReports.ExampleReceivedAt);

        var estimate = FlightEstimator.Estimate(report, AirportDirectory.GetLocation(report.Destination));
        var result = FlightCalculationResult.Create(report, estimate, CalculatedAt);

        Assert.True(estimate.IsLowOnFuel);
        Assert.True(result.LowFuelWarning);
        Assert.True(result.EstimatedFuelAtArrivalKg < 0);
        Assert.Equal(FlightStatus.LowFuelWarning, result.Status);
    }

    [Fact]
    public void Estimate_AtDestination_NeedsNoTimeOrFuel()
    {
        var bangkok = AirportDirectory.GetLocation("BKK");
        var report = new PosReport(
            "UL20420260904RGNBKK", "UL204", "RGN", "BKK", CalculatedAt,
            bangkok.Latitude, bangkok.Longitude, 450, 12500, 2800);

        var estimate = FlightEstimator.Estimate(report, bangkok);

        Assert.Equal(0, estimate.RemainingFlightTimeHours, precision: 9);
        Assert.Equal(12500, estimate.EstimatedFuelAtArrivalKg, precision: 6);
        Assert.False(estimate.IsLowOnFuel);
    }

    [Fact]
    public void Estimate_ZeroGroundSpeed_Throws()
    {
        var report = new PosReport(
            "UL20420260904RGNBKK", "UL204", "RGN", "BKK", CalculatedAt, 16.705, 96.2083, 0, 12500, 2800);

        Assert.Throws<ArgumentOutOfRangeException>(() => FlightEstimator.Estimate(report, AirportDirectory.GetLocation("BKK")));
    }
}
