namespace NextStep.PosReports.Domain.Models;

/// <summary>Processing states a flight moves through, as exposed by the API.</summary>
public static class FlightStatus
{
    /// <summary>Raw report accepted and stored; returned by <c>POST /pos-reports</c>.</summary>
    public const string Received = "RECEIVED";

    /// <summary>Report parsed and recorded; calculation not finished yet.</summary>
    public const string Parsed = "PARSED";

    /// <summary>Calculation finished, enough fuel to reach the destination.</summary>
    public const string Calculated = "CALCULATED";

    /// <summary>Calculation finished, estimated fuel at arrival is negative.</summary>
    public const string LowFuelWarning = "LOW_FUEL_WARNING";
}
