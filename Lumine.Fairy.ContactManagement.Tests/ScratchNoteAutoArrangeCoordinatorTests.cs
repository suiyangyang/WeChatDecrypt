using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Models;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class ScratchNoteAutoArrangeCoordinatorTests
{
    [Fact]
    public void Arrange_BuildsDailyReportAndPreservesClientIssueBlocks()
    {
        var coordinator = new ScratchNoteAutoArrangeCoordinator();
        var document = new ScratchNoteDocument
        {
            Id = "note-1",
            Title = "今日随手记",
            RawText = """
                2026/3/21
                1.Lumine.Fairy: TodoList 管理功能方案整理 2
                2.其他: 处理用户反馈问题 1

                客户/项目: 福立
                问题: 检测器联机失败
                问题: 设备重连后状态异常
                方案: 重启服务并复核串口参数
                """,
            CreatedAtUtc = new DateTimeOffset(2026, 3, 21, 0, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 3, 21, 0, 5, 0, TimeSpan.Zero)
        };

        var result = coordinator.Arrange(document, arrangedAtUtc: new DateTimeOffset(2026, 3, 21, 0, 6, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 3, 21), result.DailyReport.Date);
        Assert.Equal(3m, result.DailyReport.TotalHours);
        Assert.Equal(2, result.ParseResult.TodoEntries.Count);
        Assert.Single(result.ParseResult.ClientIssues);
        Assert.Equal("福立", result.ParseResult.ClientIssues[0].ProjectName);
        Assert.Equal(2, result.ParseResult.ClientIssues[0].IssueLines.Count);
        Assert.Equal("1、(Lumine.Fairy)TodoList 管理功能方案整理,2;" + Environment.NewLine + "2、(其他)处理用户反馈问题,1;", result.DailyReport.FormattedReportText);
        Assert.Equal(new DateTimeOffset(2026, 3, 21, 0, 6, 0, TimeSpan.Zero), result.Document.LastArrangedAtUtc);
    }

    [Fact]
    public void Arrange_LegacyBacklogSection_KeepsBacklogOutOfDailyReportButPreservesStructuredTodos()
    {
        var coordinator = new ScratchNoteAutoArrangeCoordinator();
        var document = new ScratchNoteDocument
        {
            Id = "note-2",
            Title = "历史随手记",
            RawText = """
                2026/3/21
                1.Lumine.Fairy: 联系人管理启动 Skill 接入与验证 3

                ==============================================================================
                Lumine.Fairy
                1.加入 todolist管理功能
                2.加入 微信聊天汇总并加入到todolist功能 √
                """
        };

        var result = coordinator.Arrange(document, arrangedAtUtc: new DateTimeOffset(2026, 3, 21, 1, 0, 0, TimeSpan.Zero));

        Assert.Equal(3, result.ParseResult.TodoEntries.Count);
        Assert.Single(result.DailyReport.Items);
        Assert.Equal("1、(Lumine.Fairy)联系人管理启动 Skill 接入与验证,3;", result.DailyReport.FormattedReportText);
        Assert.Equal(TodoStatus.Completed, result.ParseResult.TodoEntries.Single(item => item.Content.Contains("微信聊天汇总", StringComparison.Ordinal)).Status);
    }
}