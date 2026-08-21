using System.Text;
using QwenPawRemotePc.Domain;
using QwenPawRemotePc.Protocol;

namespace QwenPawRemotePc.Application;

public sealed class WeChatAnalysisCoordinator(
    IWeChatService weChat,
    IQwenPawClient qwenPaw,
    NotificationService notifications)
{
    public async Task<string> AnalyzeLatestAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var accounts = await weChat.GetAccountsAsync(cancellationToken);
        var account = accounts.OrderByDescending(x => x.DatabaseCount).FirstOrDefault() ?? throw new InvalidOperationException("未检测到可分析的微信账号。");
        var sessions = await weChat.GetSessionsAsync(account.Id, cancellationToken);
        var session = sessions.FirstOrDefault() ?? throw new InvalidOperationException("未检测到可分析的微信会话。");
        var messages = await weChat.GetLatestMessagesAsync(account.Id, session.Username, 20, cancellationToken);
        if (messages.Count == 0) throw new InvalidOperationException("所选会话没有可分析的新消息。");

        return await AnalyzeMessagesAsync(account, session, messages, agentId, cancellationToken);
    }

    public async Task<string> AnalyzeMessagesAsync(ChatAccount account, ChatSession session, IReadOnlyList<ChatMessage> messages, string agentId, CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) throw new InvalidOperationException("没有可分析的新消息。");

        var prompt = BuildPrompt(account, session, messages);
        var result = await qwenPaw.ChatAsync(agentId, prompt, cancellationToken);
        var analysis = result.Analysis;

        if (analysis is not null)
        {
            foreach (var todo in analysis.Todos ?? Array.Empty<TodoCandidate>())
            {
                await notifications.PublishAsync(new NotificationRequest(
                    $"新增 TODO：{todo.Title}",
                    todo.Summary,
                    "wechat-analysis",
                    "work",
                    todo.Priority.Equals("high", StringComparison.OrdinalIgnoreCase) ? "warning" : "info",
                    "close_or_confirm",
                    10,
                    $"todo:{todo.Id}"), cancellationToken);
            }

            foreach (var intent in analysis.Intents ?? Array.Empty<IntentCandidate>())
            {
                await notifications.PublishAsync(new NotificationRequest(
                    intent.Title,
                    intent.Summary,
                    "wechat-analysis",
                    "work",
                    "info",
                    "timeout",
                    8,
                    $"intent:{intent.Id}"), cancellationToken);
            }
        }

        await notifications.PublishAsync(new NotificationRequest(
            "微信分析完成",
            $"已分析“{session.DisplayName}”最近 {messages.Count} 条消息，目标 Agent：{agentId}。",
            "wechat-analysis",
            "wechat",
            "success",
            "timeout",
            8,
            $"analysis:{account.Id}:{session.Username}:{messages.Max(x => x.Id)}"), cancellationToken);

        return result.Text;
    }

    public async Task<string> HandleQuestionResponseAsync(NotificationRecord notification, NotificationResponse response, string agentId, CancellationToken cancellationToken = default)
    {
        if (notification.Question is null)
            throw new InvalidOperationException("该通知不是待处理的问题。");

        var selected = notification.Question.Solutions.FirstOrDefault(x => x.Id.Equals(response.SolutionId, StringComparison.OrdinalIgnoreCase));
        var instruction = selected?.Instruction;
        if (string.IsNullOrWhiteSpace(instruction)) instruction = response.CustomInput;
        if (string.IsNullOrWhiteSpace(instruction))
            throw new InvalidOperationException("请先选择解决方案或输入自定义处理要求。");

        var prompt = $"用户已处理微信分析中的疑问。问题：{notification.Question.Problem}\n来源：{notification.Question.Source.From} 与 {notification.Question.Source.To}\n用户指示：{instruction}\n请根据用户指示确认后续需要做什么，并给出简短结论。";
        var result = await qwenPaw.ChatAsync(agentId, prompt, cancellationToken);
        await notifications.ResolveAsync(notification.Id, cancellationToken);
        await notifications.PublishAsync(new NotificationRequest(
            "微信疑问已提交",
            result.Text.Length > 300 ? result.Text[..300] : result.Text,
            "wechat-analysis",
            "wechat",
            "success",
            "timeout",
            8,
            $"question-response:{notification.Id}:{response.SolutionId}:{response.CustomInput}"), cancellationToken);
        return result.Text;
    }

    private static string BuildPrompt(ChatAccount account, ChatSession session, IReadOnlyList<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine("请分析下面的微信聊天增量，识别 TODO、工作意向或需要向用户澄清的问题。");
        builder.AppendLine("请尽量只返回 JSON，结构为 AnalysisResult；如果没有事项，decision 返回 no_action。");
        builder.AppendLine($"账号：{account.DisplayName}");
        builder.AppendLine($"会话：{session.DisplayName}");
        builder.AppendLine("聊天内容：");
        foreach (var message in messages)
            builder.AppendLine($"[{message.Id}] {message.Sender}: {message.Content}");
        return builder.ToString();
    }
}
