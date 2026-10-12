using NextStep.PosReports.Domain.Airports;
using NextStep.PosReports.Domain.Models;

namespace NextStep.PosReports.Domain.Tests.Airports;

public sealed class AirportDirectoryTests
{
    [Fact]
    public void GetLocation_KnownAirport_ReturnsCoordinates()
    {
        Assert.Equal(new GeoCoordinate(13.6900, 100.7501), AirportDirectory.GetLocation("BKK"));
    }

    [Theory]
    [InlineData("XXX")]
    [InlineData("bkk")]
    [InlineData("")]
    public void GetLocation_UnknownAirport_Throws(string airportCode)
    {
        var exception = Assert.Throws<UnknownAirportException>(() => AirportDirectory.GetLocation(airportCode));

        Assert.Equal(airportCode, exception.AirportCode);
    }

    [Fact]
    public void TryGetLocation_UnknownAirport_ReturnsFalse()
    {
        Assert.False(AirportDirectory.TryGetLocation("XXX", out _));
    }
}
