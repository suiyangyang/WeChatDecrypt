using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Models;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class DailyReportFormatterTests
{
    [Fact]
    public void Format_UsesStrictExpectedDailyReportSyntax()
    {
        var formatter = new DailyReportFormatter();

        var result = formatter.Format(
        [
            new TodoEntry
            {
                ProjectName = "（福立）",
                Title = "检测器设备联机调试",
                WorkHours = 8m,
                SortOrder = 1
            }
        ]);

        Assert.Equal("1、(福立)检测器设备联机调试,8;", result);
    }
}