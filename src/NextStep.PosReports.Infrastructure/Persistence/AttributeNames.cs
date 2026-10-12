namespace NextStep.PosReports.Infrastructure.Persistence;

/// <summary>DynamoDB attribute names. Both tables use flightId (partition key) + timestamp (sort key).</summary>
internal static class AttributeNames
{
    public const string FlightId = "flightId";
    public const string Timestamp = "timestamp";
    public const string Attachment = "attachment";
    public const string Status = "status";
    public const string RemainingFlightTimeMinutes = "remainingFlightTimeMinutes";
    public const string EstimatedFuelAtArrivalKg = "estimatedFuelAtArrivalKg";
    public const string LowFuelWarning = "lowFuelWarning";
}
