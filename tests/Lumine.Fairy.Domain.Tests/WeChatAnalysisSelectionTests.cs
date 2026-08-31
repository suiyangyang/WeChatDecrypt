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

    [Fact]
    public void QwenPawAnalysisParserRejectsQuestionWithMissingNestedFields()
    {
        const string response = "{\"decision\":\"question\",\"todos\":[],\"intents\":[],"
            + "\"questions\":[{}],\"sourceRefs\":[]}";

        var analysis = QwenPawClient.TryParseAnalysis(response);

        Assert.Null(analysis);
    }

    [Fact]
    public void QwenPawAnalysisParserRejectsTodoWithMissingSummary()
    {
        const string response = "{\"decision\":\"todo\",\"todos\":[{\"id\":\"todo-1\",\"title\":\"确认交付时间\","
            + "\"priority\":\"medium\",\"dueAt\":null,\"assignee\":null,\"confidence\":0.9,\"sourceRefs\":[\"message-1\"]}],"
            + "\"intents\":[],\"questions\":[],\"sourceRefs\":[]}";

        var analysis = QwenPawClient.TryParseAnalysis(response);

        Assert.Null(analysis);
    }

    [Fact]
    public void QwenPawAnalysisParserAcceptsTodoWithDescription()
    {
        const string response = "{\"decision\":\"todo\",\"todos\":[{\"id\":\"todo-1\",\"title\":\"确认交付时间\","
            + "\"summary\":\"确认当前排期并回复客户可交付日期。\",\"priority\":\"medium\",\"dueAt\":null,"
            + "\"assignee\":null,\"confidence\":0.9,\"sourceRefs\":[\"message-1\"]}],\"intents\":[],"
            + "\"questions\":[],\"sourceRefs\":[]}";

        var analysis = QwenPawClient.TryParseAnalysis(response);

        Assert.NotNull(analysis);
        var todo = Assert.Single(analysis!.Todos);
        Assert.Equal("确认当前排期并回复客户可交付日期。", todo.Summary);
    }

    [Fact]
    public void QwenPawAnalysisParserAcceptsCompleteQuestionContract()
    {
        const string response = "{\"decision\":\"question\",\"todos\":[],\"intents\":[],"
            + "\"questions\":[{\"id\":\"q-1\",\"problem\":\"需要确认的问题\","
            + "\"source\":{\"conversationId\":\"conversation-1\",\"participants\":[\"甲\",\"乙\"],"
            + "\"from\":\"甲\",\"to\":\"乙\",\"messageRefs\":[\"message-1\"]},"
            + "\"solutions\":[{\"id\":\"s1\",\"label\":\"方案一\",\"instruction\":\"执行方案一\",\"recommended\":true},"
            + "{\"id\":\"s2\",\"label\":\"方案二\",\"instruction\":\"执行方案二\",\"recommended\":false}],"
            + "\"recommendedSolutionId\":\"s1\",\"allowCustomInput\":true}],\"sourceRefs\":[]}";

        var analysis = QwenPawClient.TryParseAnalysis(response);

        Assert.NotNull(analysis);
        var question = Assert.Single(analysis!.Questions);
        Assert.Equal("q-1", question.Id);
        Assert.Equal("s1", question.RecommendedSolutionId);
    }
}
