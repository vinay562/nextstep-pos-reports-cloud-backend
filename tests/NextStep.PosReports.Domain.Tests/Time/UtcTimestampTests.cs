using NextStep.PosReports.Domain.Time;

namespace NextStep.PosReports.Domain.Tests.Time;

public sealed class UtcTimestampTests
{
    private static readonly DateTimeOffset SampleTime = new(2026, 9, 4, 15, 12, 30, 123, TimeSpan.Zero);

    [Fact]
    public void ToSecondPrecision_FormatsAsIsoUtc()
    {
        Assert.Equal("2026-09-04T15:12:30Z", UtcTimestamp.ToSecondPrecision(SampleTime));
    }

    [Fact]
    public void ToMillisecondPrecision_FormatsAsIsoUtc()
    {
        Assert.Equal("2026-09-04T15:12:30.123Z", UtcTimestamp.ToMillisecondPrecision(SampleTime));
    }

    [Fact]
    public void ToMillisecondPrecision_KeepsTrailingZeroMilliseconds()
    {
        var onTheSecond = new DateTimeOffset(2026, 9, 4, 15, 12, 30, TimeSpan.Zero);

        Assert.Equal("2026-09-04T15:12:30.000Z", UtcTimestamp.ToMillisecondPrecision(onTheSecond));
    }

    [Fact]
    public void Format_NonUtcOffset_IsConvertedToUtc()
    {
        var withOffset = new DateTimeOffset(2026, 9, 4, 20, 42, 30, TimeSpan.FromHours(5.5));

        Assert.Equal("2026-09-04T15:12:30Z", UtcTimestamp.ToSecondPrecision(withOffset));
    }

    [Fact]
    public void TruncateToMilliseconds_DropsSubMillisecondTicks()
    {
        var withTicks = SampleTime.AddTicks(4567);

        Assert.Equal(SampleTime, UtcTimestamp.TruncateToMilliseconds(withTicks));
    }

    [Fact]
    public void Parse_RoundTripsFormattedValue()
    {
        Assert.Equal(SampleTime, UtcTimestamp.Parse(UtcTimestamp.ToMillisecondPrecision(SampleTime)));
    }

    [Fact]
    public void Parse_InvalidValue_Throws()
    {
        Assert.Throws<FormatException>(() => UtcTimestamp.Parse("yesterday"));
    }
}
