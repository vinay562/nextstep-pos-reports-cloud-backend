using NextStep.PosReports.Domain.Calculation;
using NextStep.PosReports.Domain.Models;

namespace NextStep.PosReports.Domain.Tests.Calculation;

public sealed class GreatCircleTests
{
    [Fact]
    public void DistanceNauticalMiles_SamePoint_IsZero()
    {
        var point = new GeoCoordinate(16.705, 96.2083);

        Assert.Equal(0, GreatCircle.DistanceNauticalMiles(point, point), precision: 9);
    }

    [Fact]
    public void DistanceNauticalMiles_SpecificationExample_IsAbout319()
    {
        var current = new GeoCoordinate(16.705, 96.2083);
        var bangkok = new GeoCoordinate(13.6900, 100.7501);

        var distance = GreatCircle.DistanceNauticalMiles(current, bangkok);

        Assert.InRange(distance, 318.5, 319.5);
    }

    [Fact]
    public void DistanceNauticalMiles_OneDegreeOfLatitude_IsAbout60()
    {
        var distance = GreatCircle.DistanceNauticalMiles(new GeoCoordinate(0, 0), new GeoCoordinate(1, 0));

        Assert.Equal(60.04, distance, precision: 2);
    }

    [Fact]
    public void DistanceNauticalMiles_IsSymmetric()
    {
        var singapore = new GeoCoordinate(1.3644, 103.9915);
        var london = new GeoCoordinate(51.4700, -0.4543);

        Assert.Equal(
            GreatCircle.DistanceNauticalMiles(singapore, london),
            GreatCircle.DistanceNauticalMiles(london, singapore),
            precision: 9);
    }
}
