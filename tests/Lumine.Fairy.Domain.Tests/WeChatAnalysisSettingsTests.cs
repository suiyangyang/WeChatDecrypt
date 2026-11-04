using Lumine.Fairy.Protocol;
using Lumine.Fairy.Infrastructure;

namespace Lumine.Fairy.Domain.Tests;

public sealed class WeChatAnalysisSettingsTests
{
    [Fact]
    public void DefaultsToFiveMinuteAnalysisInterval()
    {
        var settings = new WeChatAnalysisSettings();

        Assert.Equal(5, settings.AnalysisIntervalMinutes);
    }

    [Fact]
    public void DefaultsToNotifyingAfterEveryAnalysis()
    {
        var settings = new WeChatAnalysisSettings();

        Assert.True(settings.NotifyAfterEveryAnalysis);
        Assert.False(settings.NotifyWhenWorkContentFound);
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

    [Fact]
    public void WeChatDataDirectoryRoundTripsThroughSettingsStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumine-fairy-wechat-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new AppSettingsStore(path);
            var settings = store.Load();
            settings.WeChatDataDirectory = @"D:\WeChatData";

            store.Save(settings);

            Assert.Equal(@"D:\WeChatData", new AppSettingsStore(path).Load().WeChatDataDirectory);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
