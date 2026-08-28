using Lumine.Fairy.Application;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Domain.Tests;

public sealed class WeChatAnalysisSelectionTests
{
    [Fact]
    public void UnanalyzedModeStartsAfterTheConfiguredAnalysisPoint()
    {
        var messages = new[]
        {
            new ChatMessage("message-1", "客户", "历史消息", 1),
            new ChatMessage("message-2", "客户", "新消息", 2),
        };

        var selected = WeChatAnalysisSelection.SelectMessages(
            messages,
            WeChatAutomaticAnalysisMode.Unanalyzed,
            WeChatAnalysisSelection.GetCursor(messages[0]));

        var message = Assert.Single(selected);
        Assert.Equal("message-2", message.Id);
    }

    [Fact]
    public void UnanalyzedModeUsesTheBoundedRecentWindowWhenCursorIsOutsideIt()
    {
        var messages = new[]
        {
            new ChatMessage("message-1", "客户", "消息 1", 1),
            new ChatMessage("message-2", "客户", "消息 2", 2),
        };

        var selected = WeChatAnalysisSelection.SelectMessages(
            messages,
            WeChatAutomaticAnalysisMode.Unanalyzed,
            "missing-cursor:0");

        Assert.Equal(["message-1", "message-2"], selected.Select(x => x.Id));
        Assert.False(WeChatAnalysisSelection.HasCursor(messages, "missing-cursor:0"));
    }

    [Fact]
    public void UnanalyzedModeDoesNotSelectMessagesWhenCursorTimestampIsInvalid()
    {
        var messages = new[]
        {
            new ChatMessage("message-1", "客户", "消息 1", 1),
        };

        var selected = WeChatAnalysisSelection.SelectMessages(
            messages,
            WeChatAutomaticAnalysisMode.Unanalyzed,
            "missing-cursor:not-a-timestamp");

        Assert.Empty(selected);
    }

    [Fact]
    public void TimeWindowModesOnlySelectMessagesAfterThePreviousCursor()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var messages = new[]
        {
            new ChatMessage("message-1", "客户", "已分析", now - 60),
            new ChatMessage("message-2", "客户", "新增", now),
        };

        var selected = WeChatAnalysisSelection.SelectMessages(
            messages,
            WeChatAutomaticAnalysisMode.Recent50,
            WeChatAnalysisSelection.GetCursor(messages[0]));

        var message = Assert.Single(selected);
        Assert.Equal("message-2", message.Id);
    }

    [Fact]
    public void FallbackMessageIdIsStableAndSeparatesDuplicateMessagesByOrder()
    {
        var first = WeChatDataAnalysisClient.BuildStableFallbackMessageId("account", "contact", "客户", "你好", 123, 0);
        var same = WeChatDataAnalysisClient.BuildStableFallbackMessageId("account", "contact", "客户", "你好", 123, 0);
        var second = WeChatDataAnalysisClient.BuildStableFallbackMessageId("account", "contact", "客户", "你好", 123, 1);

        Assert.Equal(first, same);
        Assert.NotEqual(first, second);
        Assert.StartsWith("fallback-", first);
    }

    [Fact]
    public void QwenPawAnalysisParserIgnoresReasoningBracesAndMarkdown()
    {
        const string response = "思考过程包含 {不是分析结果}。\n```json\n"
            + "{\"decision\":\"no_action\",\"todos\":[],\"intents\":[],\"questions\":[],\"sourceRefs\":[]}"
            + "\n```\n";

        var analysis = QwenPawClient.TryParseAnalysis(response);

        Assert.NotNull(analysis);
        Assert.Equal("no_action", analysis.Decision);
        Assert.Empty(analysis.Todos);
        Assert.Empty(analysis.Questions);
    }

    [Fact]
    public void QwenPawAnalysisParserRejectsIncompleteAnalysisShape()
    {
        var analysis = QwenPawClient.TryParseAnalysis("{\"decision\":\"no_action\"}");

        Assert.Null(analysis);
    }
}
