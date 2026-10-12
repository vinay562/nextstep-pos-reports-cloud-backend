using System.Globalization;

namespace NextStep.PosReports.Domain.Identifiers;

public static class FlightIdentifier
{
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
}
