using Lumine.Fairy.Application;
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
}
