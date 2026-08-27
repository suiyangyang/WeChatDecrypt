using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Domain.Tests;

public sealed class WeChatAnalysisSettingsTests
{
    [Fact]
    public void DefaultsToFiveMinuteAnalysisInterval()
    {
        var settings = new WeChatAnalysisSettings();

        Assert.Equal(5, settings.AnalysisIntervalMinutes);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(1440, 1440)]
    [InlineData(2000, 1440)]
    public void NormalizesAnalysisIntervalToSupportedRange(int value, int expected)
    {
        Assert.Equal(expected, WeChatAnalysisSettings.NormalizeAnalysisIntervalMinutes(value));
    }
}
