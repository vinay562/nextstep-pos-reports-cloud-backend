using NextStep.PosReports.Domain.Models;

namespace NextStep.PosReports.Domain.Calculation;

public static class GreatCircle
{
    public const double EarthRadiusNauticalMiles = 3440.065;

    /// <summary>Great-circle distance between two points using the haversine formula.</summary>
    public static double DistanceNauticalMiles(GeoCoordinate from, GeoCoordinate to)
    {
        var deltaLatitude = ToRadians(to.Latitude - from.Latitude);
        var deltaLongitude = ToRadians(to.Longitude - from.Longitude);

        var a = Math.Sin(deltaLatitude / 2) * Math.Sin(deltaLatitude / 2)
              + Math.Cos(ToRadians(from.Latitude)) * Math.Cos(ToRadians(to.Latitude))
              * Math.Sin(deltaLongitude / 2) * Math.Sin(deltaLongitude / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusNauticalMiles * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
