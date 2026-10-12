using System.Globalization;
using System.Text.RegularExpressions;

namespace NextStep.PosReports.Domain.Identifiers;

public static partial class FlightIdentifier
{
    /// <summary>Flight number (airline + 1-4 digits + optional suffix), yyyyMMdd, departure, destination.</summary>
    [GeneratedRegex(@"^[A-Z0-9]{2}\d{1,4}[A-Z]?\d{8}[A-Z]{3}[A-Z]{3}$")]
    private static partial Regex FlightIdPattern();

    /// <summary>
    /// Builds the flight id: flight number + flight date (yyyyMMdd) + departure + destination, no separators.
    /// Example: <c>UL204</c>, 2026-09-04, <c>RGN</c>, <c>BKK</c> → <c>UL20420260904RGNBKK</c>.
    /// </summary>
    public static string Create(string flightNumber, DateOnly flightDate, string departure, string destination) =>
        string.Concat(
            flightNumber,
            flightDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            departure,
            destination);

    /// <summary>True if <paramref name="flightId"/> has the shape produced by <see cref="Create"/>.</summary>
    public static bool IsValid(string flightId) => FlightIdPattern().IsMatch(flightId);
}
