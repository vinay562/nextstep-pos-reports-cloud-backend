using System.Text.Json.Serialization;
using NextStep.PosReports.Domain.Time;

namespace NextStep.PosReports.Infrastructure.Messaging;

/// <summary>Queue message asking the calculator to process one parsed report.</summary>
public sealed record CalculationRequest(
    string FlightId,
    [property: JsonConverter(typeof(SecondPrecisionUtcTimestampConverter))] DateTimeOffset Timestamp);
