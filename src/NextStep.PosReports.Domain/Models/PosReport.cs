using System.Text.Json.Serialization;
using NextStep.PosReports.Domain.Time;

namespace NextStep.PosReports.Domain.Models;

/// <summary>A parsed POS report. Serialized as-is into the <c>attachment/</c> object.</summary>
public sealed record PosReport(
    string FlightId,
    string FlightNumber,
    string Departure,
    string Destination,
    [property: JsonConverter(typeof(SecondPrecisionUtcTimestampConverter))] DateTimeOffset Timestamp,
    double Latitude,
    double Longitude,
    int GroundSpeedKnots,
    int FuelOnBoardKg,
    int FuelFlowKgPerHour)
{
    [JsonIgnore]
    public GeoCoordinate Position => new(Latitude, Longitude);
}
