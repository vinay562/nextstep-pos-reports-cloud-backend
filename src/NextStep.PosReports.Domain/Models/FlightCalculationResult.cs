using System.Text.Json.Serialization;
using NextStep.PosReports.Domain.Calculation;
using NextStep.PosReports.Domain.Time;

namespace NextStep.PosReports.Domain.Models;

/// <summary>
/// Self-contained calculation result (inputs + outputs). Serialized as-is into the <c>results/</c> object.
/// <see cref="Timestamp"/> is when the result was calculated, not the POS report time.
/// </summary>
public sealed record FlightCalculationResult(
    string FlightId,
    [property: JsonConverter(typeof(MillisecondPrecisionUtcTimestampConverter))] DateTimeOffset Timestamp,
    FlightCalculationInput Input,
    int RemainingFlightTimeMinutes,
    int EstimatedFuelAtArrivalKg,
    bool LowFuelWarning)
{
    [JsonIgnore]
    public string Status => LowFuelWarning ? FlightStatus.LowFuelWarning : FlightStatus.Calculated;

    public static FlightCalculationResult Create(PosReport report, FlightEstimate estimate, DateTimeOffset calculatedAt) =>
        new(
            report.FlightId,
            calculatedAt,
            new FlightCalculationInput(
                report.Latitude,
                report.Longitude,
                report.Destination,
                report.GroundSpeedKnots,
                report.FuelOnBoardKg,
                report.FuelFlowKgPerHour),
            RoundToInteger(estimate.RemainingFlightTimeHours * 60),
            RoundToInteger(estimate.EstimatedFuelAtArrivalKg),
            estimate.IsLowOnFuel);

    private static int RoundToInteger(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}

public sealed record FlightCalculationInput(
    double CurrentLatitude,
    double CurrentLongitude,
    string Destination,
    int GroundSpeedKnots,
    int FuelOnBoardKg,
    int FuelFlowKgPerHour);
