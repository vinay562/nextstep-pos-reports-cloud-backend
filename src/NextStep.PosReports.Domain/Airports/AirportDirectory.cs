using System.Collections.Frozen;
using NextStep.PosReports.Domain.Models;

namespace NextStep.PosReports.Domain.Airports;

/// <summary>Hardcoded IATA airport code → location lookup. Extend this table to support more destinations.</summary>
public static class AirportDirectory
{
    private static readonly FrozenDictionary<string, GeoCoordinate> LocationsByCode =
        new Dictionary<string, GeoCoordinate>
        {
            ["RGN"] = new(16.9073, 96.1332),   // Yangon
            ["BKK"] = new(13.6900, 100.7501),  // Bangkok Suvarnabhumi
            ["SIN"] = new(1.3644, 103.9915),   // Singapore Changi
            ["KUL"] = new(2.7456, 101.7072),   // Kuala Lumpur
            ["HKG"] = new(22.3080, 113.9185),  // Hong Kong
            ["DEL"] = new(28.5562, 77.1000),   // Delhi
            ["SYD"] = new(-33.9399, 151.1753), // Sydney
            ["LHR"] = new(51.4700, -0.4543),   // London Heathrow
            ["JFK"] = new(40.6413, -73.7781),  // New York JFK
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool TryGetLocation(string airportCode, out GeoCoordinate location) =>
        LocationsByCode.TryGetValue(airportCode, out location);

    public static GeoCoordinate GetLocation(string airportCode) =>
        TryGetLocation(airportCode, out var location) ? location : throw new UnknownAirportException(airportCode);
}

public sealed class UnknownAirportException(string airportCode)
    : Exception($"Airport '{airportCode}' is not in the airport directory.")
{
    public string AirportCode { get; } = airportCode;
}
