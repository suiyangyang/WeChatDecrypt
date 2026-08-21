using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Models;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class ScratchNoteParseServiceTests
{
    [Fact]
    public void Parse_LegacyScratchNote_RecognizesDailyItemsProjectBacklogStatusesAndClientIssues()
    {
        var service = new ScratchNoteParseService();

        var result = service.Parse(
            """
            3/18

            1.LabChrom3:多通道部分重构，与双塔模式统一 10

            3/19

            1.其他：内部沟通、客服等 1

            ==============================================================================

            Lumine.Fairy

            1.拖动后仍然会打开聊天窗口 √
            2.加入Paddle OCR VL提升识别能力 ←
            3.加入 联系人&公司&项目&人际关系管理功能

            ==============================================================================

            AFS剩余功能
            1.自动稀释
             |-- 稀释（与额外稀释不是一回事）
             |-- 稀释倍数是指自动配标液中的稀释倍数 dilution = originConc / conc    完成
            2.灵敏度校正 - 只有最后一个点需要                                        未开始
            3.AFS国标-道间干扰&其他                                                    未开始 是否需要做待定

            客户/项目：福立
            问题：检测器联机失败
            方案：先重启服务
            """);

        Assert.Equal(new DateOnly(DateTime.Today.Year, 3, 19), result.WorkDate);
        Assert.Equal(8, result.TodoEntries.Count);
        Assert.Equal(2, result.TodoEntries.Count(item => item.IsDaily));

        var dailyItem = Assert.Single(result.TodoEntries, item => item.ProjectName == "LabChrom3");
        Assert.Equal(10m, dailyItem.WorkHours);
        Assert.True(dailyItem.IsDaily);

        var completedBacklog = Assert.Single(result.TodoEntries, item => item.ProjectName == "Lumine.Fairy" && item.Status == TodoStatus.Completed);
        Assert.Equal("拖动后仍然会打开聊天窗口", completedBacklog.Content);

        var inProgressBacklog = Assert.Single(result.TodoEntries, item => item.ProjectName == "Lumine.Fairy" && item.Content.Contains("Paddle OCR", StringComparison.Ordinal));
        Assert.Equal(TodoStatus.InProgress, inProgressBacklog.Status);

        var afsTodo = Assert.Single(result.TodoEntries, item => item.ProjectName == "AFS" && item.Content.Contains("灵敏度校正", StringComparison.Ordinal));
        Assert.Equal(TodoStatus.NotStarted, afsTodo.Status);

        var blockedTodo = Assert.Single(result.TodoEntries, item => item.ProjectName == "AFS" && item.Content.Contains("AFS国标", StringComparison.Ordinal));
        Assert.Equal(TodoStatus.Blocked, blockedTodo.Status);
        Assert.Contains("是否需要做", blockedTodo.Notes, StringComparison.Ordinal);

        var parentTodo = Assert.Single(result.TodoEntries, item => item.ProjectName == "AFS" && item.Content == "自动稀释");
        Assert.Contains("稀释（与额外稀释不是一回事）", parentTodo.Notes, StringComparison.Ordinal);
        Assert.Contains("稀释倍数是指自动配标液中的稀释倍数", parentTodo.Notes, StringComparison.Ordinal);

        var clientIssue = Assert.Single(result.ClientIssues);
        Assert.Equal("福立", clientIssue.ProjectName);
        Assert.Single(clientIssue.IssueLines);
        Assert.Single(clientIssue.SolutionLines);
    }

    [Fact]
    public void Parse_WhenTextContainsUnknownLines_PreservesUnclassifiedFragments()
    {
        var service = new ScratchNoteParseService();

        var result = service.Parse(
            """
            3/21
            今天先讨论方案
            1.Lumine.Fairy: TodoList 管理功能方案整理 2
            ???
            """);

        Assert.Single(result.TodoEntries);
        Assert.Equal(2, result.UnclassifiedFragments.Count);
        Assert.Equal("今天先讨论方案", result.UnclassifiedFragments[0].Text);
        Assert.Equal("???", result.UnclassifiedFragments[1].Text);
    }
}