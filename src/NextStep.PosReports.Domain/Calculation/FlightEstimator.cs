using NextStep.PosReports.Domain.Models;

namespace NextStep.PosReports.Domain.Calculation;

/// <summary>
/// Simplified remaining-flight estimate: straight great-circle distance at the current ground speed and fuel flow.
/// </summary>
public static class FlightEstimator
{
    public static FlightEstimate Estimate(PosReport report, GeoCoordinate destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(report.GroundSpeedKnots);

        var distanceNauticalMiles = GreatCircle.DistanceNauticalMiles(report.Position, destination);
        var remainingFlightTimeHours = distanceNauticalMiles / report.GroundSpeedKnots;
        var estimatedFuelAtArrivalKg = report.FuelOnBoardKg - report.FuelFlowKgPerHour * remainingFlightTimeHours;

        return new FlightEstimate(distanceNauticalMiles, remainingFlightTimeHours, estimatedFuelAtArrivalKg);
    }
}

public sealed record FlightEstimate(
    double DistanceNauticalMiles,
    double RemainingFlightTimeHours,
    double EstimatedFuelAtArrivalKg)
{
    public bool IsLowOnFuel => EstimatedFuelAtArrivalKg < 0;
}
