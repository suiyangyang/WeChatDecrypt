using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.TodoList.App.Services;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class ReportMindDailyReportTextFormatterTests
{
    [Fact]
    public void BuildText_UsesSampleLikeDateAndBlankLineLayout()
    {
        var text = ReportMindDailyReportTextFormatter.BuildText(
        [
            new DailyReport
            {
                Date = new DateOnly(2026, 3, 23),
                FormattedReportText = "1、(LabChrom3)序列支持每行多次运行&多通道支持bug修复,8;\n2、(LabChrom)周会,1;"
            },
            new DailyReport
            {
                Date = new DateOnly(2026, 3, 24),
                FormattedReportText = "1、(浩天晖)设备调试,1;"
            }
        ]);

        var expected = string.Join(
            Environment.NewLine,
            [
                "3/23",
                string.Empty,
                "1、(LabChrom3)序列支持每行多次运行&多通道支持bug修复,8;",
                "2、(LabChrom)周会,1;",
                string.Empty,
                "3/24",
                string.Empty,
                "1、(浩天晖)设备调试,1;"
            ]);

        Assert.Equal(expected, text);
    }
}