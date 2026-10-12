using NextStep.PosReports.Domain.Parsing;

namespace NextStep.PosReports.Domain.Tests.Parsing;

public sealed class CoordinateParserTests
{
    [Theory]
    [InlineData("N1642.3E09612.5", 16.705, 96.2083)]
    [InlineData("S1642.3E09612.5", -16.705, 96.2083)]
    [InlineData("N1642.3W09612.5", 16.705, -96.2083)]
    [InlineData("S1642.3W09612.5", -16.705, -96.2083)]
    [InlineData("N0000.0E00000.0", 0, 0)]
    [InlineData("S0000.0W00000.0", 0, 0)]
    [InlineData("N9000.0E18000.0", 90, 180)]
    [InlineData("N1642E09612", 16.7, 96.2)]
    public void ParsePosition_ValidPosition_ReturnsDecimalDegrees(string position, double expectedLatitude, double expectedLongitude)
    {
        var coordinate = CoordinateParser.ParsePosition(position);

        Assert.Equal(expectedLatitude, coordinate.Latitude);
        Assert.Equal(expectedLongitude, coordinate.Longitude);
    }

    [Theory]
    [InlineData("")]
    [InlineData("N1642.3")]
    [InlineData("X1642.3E09612.5")]
    [InlineData("N1642.3E09612.5X")]
    [InlineData("N164.3E09612.5")]
    [InlineData("N1642.3E09612.")]
    [InlineData("N1642.3E09660.0")]
    [InlineData("N9000.1E09612.5")]
    public void ParsePosition_InvalidPosition_Throws(string position)
    {
        Assert.Throws<PosReportFormatException>(() => CoordinateParser.ParsePosition(position));
    }
}
