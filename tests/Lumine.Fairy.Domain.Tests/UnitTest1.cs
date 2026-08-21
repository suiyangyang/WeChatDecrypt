namespace Lumine.Fairy.Domain.Tests;

public class UnitTest1
{
    [Fact]
    public void ConfiguredTimeZoneConvertsUtcToBeijingTime()
    {
        var context = new TimeZoneContext("China Standard Time");

        var result = context.Convert(new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 8, 21, 8, 0, 0, TimeSpan.FromHours(8)), result);
    }

    [Fact]
    public void UnixTimeConversionSupportsSecondsAndMilliseconds()
    {
        var context = new TimeZoneContext("China Standard Time");
        var utc = new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);
        var expected = new DateTimeOffset(2026, 8, 21, 8, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal(expected, context.FromUnixTime(utc.ToUnixTimeSeconds()));
        Assert.Equal(expected, context.FromUnixTime(utc.ToUnixTimeMilliseconds()));
    }
}
