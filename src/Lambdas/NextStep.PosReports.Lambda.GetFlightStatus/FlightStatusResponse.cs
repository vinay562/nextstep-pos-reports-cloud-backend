using NextStep.PosReports.Domain.Models;
using NextStep.PosReports.Domain.Time;
using NextStep.PosReports.Infrastructure.Persistence;

namespace NextStep.PosReports.Lambda.GetFlightStatus;

/// <summary>
/// Status API response. The calculation fields are omitted until the calculation has run.
/// <see cref="Timestamp"/> is the calculation time once calculated, otherwise the POS report time.
/// </summary>
public sealed record FlightStatusResponse(
    string FlightId,
    string Status,
    string Timestamp,
    int? RemainingFlightTimeMinutes = null,
    int? EstimatedFuelAtArrivalKg = null,
    bool? LowFuelWarning = null)
{
    public static FlightStatusResponse FromCalculationResult(CalculationResultRecord result) =>
        new(
            result.FlightId,
            result.Status,
            UtcTimestamp.ToMillisecondPrecision(result.Timestamp),
            result.RemainingFlightTimeMinutes,
            result.EstimatedFuelAtArrivalKg,
            result.LowFuelWarning);

    public static FlightStatusResponse FromParsedReport(ParsedReportRecord report) =>
        new(report.FlightId, FlightStatus.Parsed, UtcTimestamp.ToSecondPrecision(report.Timestamp));
}
