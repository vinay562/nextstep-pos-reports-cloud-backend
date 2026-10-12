using System.Globalization;
using System.Text.RegularExpressions;
using NextStep.PosReports.Domain.Models;

namespace NextStep.PosReports.Domain.Parsing;

/// <summary>
/// Parses the position block, e.g. <c>N1642.3E09612.5</c>: hemisphere letter, degrees (2 digits for latitude,
/// 3 for longitude), then decimal minutes. Only the hemisphere letters separate the two halves.
/// </summary>
public static partial class CoordinateParser
{
    /// <summary>4 decimal places ≈ 11 m, matching the precision shown in the specification (96.2083).</summary>
    private const int DecimalDegreesPrecision = 4;

    private const int MaxLatitudeDegrees = 90;
    private const int MaxLongitudeDegrees = 180;
    private const double MinutesPerDegree = 60.0;

    [GeneratedRegex(
        @"^(?<latitudeHemisphere>[NS])(?<latitudeDegrees>\d{2})(?<latitudeMinutes>\d{2}(?:\.\d+)?)" +
        @"(?<longitudeHemisphere>[EW])(?<longitudeDegrees>\d{3})(?<longitudeMinutes>\d{2}(?:\.\d+)?)$")]
    private static partial Regex PositionPattern();

    public static GeoCoordinate ParsePosition(string position)
    {
        var match = PositionPattern().Match(position);
        if (!match.Success)
        {
            throw new PosReportFormatException(
                $"Position '{position}' is not in the expected format, e.g. 'N1642.3E09612.5'.");
        }

        var latitude = ToDecimalDegrees(
            ReadInteger(match, "latitudeDegrees"),
            ReadMinutes(match, "latitudeMinutes"),
            isNegative: match.Groups["latitudeHemisphere"].Value == "S",
            MaxLatitudeDegrees,
            "Latitude");

        var longitude = ToDecimalDegrees(
            ReadInteger(match, "longitudeDegrees"),
            ReadMinutes(match, "longitudeMinutes"),
            isNegative: match.Groups["longitudeHemisphere"].Value == "W",
            MaxLongitudeDegrees,
            "Longitude");

        return new GeoCoordinate(latitude, longitude);
    }

    /// <summary>decimal = degrees + minutes / 60, negated for the southern / western hemisphere.</summary>
    public static double ToDecimalDegrees(int degrees, double minutes, bool isNegative, int maxDegrees, string axisName)
    {
        if (minutes >= MinutesPerDegree)
        {
            throw new PosReportFormatException($"{axisName} minutes must be less than 60 but was {minutes}.");
        }

        var decimalDegrees = Math.Round(degrees + minutes / MinutesPerDegree, DecimalDegreesPrecision, MidpointRounding.AwayFromZero);
        if (decimalDegrees > maxDegrees)
        {
            throw new PosReportFormatException($"{axisName} must not exceed {maxDegrees} degrees.");
        }

        return isNegative && decimalDegrees != 0 ? -decimalDegrees : decimalDegrees;
    }

    private static int ReadInteger(Match match, string groupName) =>
        int.Parse(match.Groups[groupName].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);

    private static double ReadMinutes(Match match, string groupName) =>
        double.Parse(match.Groups[groupName].ValueSpan, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
