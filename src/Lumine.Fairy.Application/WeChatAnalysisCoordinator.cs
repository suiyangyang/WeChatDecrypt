using System.Security.Cryptography;
using System.Text;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Application;

public sealed class WeChatAnalysisCoordinator(
    IWeChatService weChat,
    IQwenPawClient qwenPaw,
    NotificationService notifications,
    TodoService todos,
    Func<ChatAccount, ChatSession, CancellationToken, Task<ConversationCompanyContext?>>? contextResolver = null)
{
    private static readonly TimeSpan WeChatStageTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QwenPawStageTimeout = TimeSpan.FromMinutes(11);
    private readonly TodoService _todos = todos;
    private readonly Func<ChatAccount, ChatSession, CancellationToken, Task<ConversationCompanyContext?>>? _contextResolver = contextResolver;

    public event EventHandler<AnalysisProgressSnapshot>? ProgressChanged;
    public WeChatAnalysisSettings Settings { get; set; } = new();
    public IReadOnlyList<WeChatWhitelistEntry> Whitelist { get; set; } = [];

    public async Task<string> AnalyzeLatestAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var progress = CreateProgress("微信账号", "最近会话");
        try
        {
            var accounts = await RunStageAsync(progress, "read-account", "正在读取可用微信账号", token => weChat.GetAccountsAsync(token), cancellationToken, WeChatStageTimeout);
            var account = accounts.OrderByDescending(x => x.DatabaseCount).FirstOrDefault()
                          ?? throw FailAndCreateException(progress, "read-account", "未检测到可分析的微信账号。");
            progress.SetContext(account.DisplayName, "最近会话");

            var sessions = await RunStageAsync(progress, "read-session", "正在读取最近微信会话", token => weChat.GetSessionsAsync(account.Id, token), cancellationToken, WeChatStageTimeout);
            var allowedSessions = sessions
                .Where(candidate => WeChatAnalysisSelection.IsSessionAllowed(candidate, Settings, Whitelist))
                .ToList();
            if (allowedSessions.Count == 0)
                throw FailAndCreateException(progress, "read-session", "未检测到可分析的微信会话。");
            if (!Settings.EnableWhitelist)
                allowedSessions = allowedSessions.Take(1).ToList();

            var workItems = new List<WeChatAnalysisWorkItem>();
            foreach (var session in allowedSessions)
            {
                progress.SetContext(account.DisplayName, session.DisplayName);
                var readLimit = Settings.AutomaticMode == WeChatAutomaticAnalysisMode.Unanalyzed
                    ? WeChatAnalysisSelection.UnanalyzedMessageReadLimit
                    : WeChatAnalysisSelection.DefaultMessageReadLimit;
                var messages = await RunStageAsync(progress, "read-messages", "正在读取最近消息", token => weChat.GetLatestMessagesAsync(account.Id, session.Username, readLimit, token), cancellationToken, WeChatStageTimeout);
                if (Settings.AutomaticMode != WeChatAutomaticAnalysisMode.Unanalyzed)
                    messages = WeChatAnalysisSelection.SelectMessages(messages, Settings.AutomaticMode, null);
                if (messages.Count == 0) continue;
                workItems.Add(new WeChatAnalysisWorkItem(account, session, messages));
            }

            if (workItems.Count == 0)
                throw FailAndCreateException(progress, "read-messages", "匹配的微信会话没有可分析的新消息。");

            return await AnalyzeRoundAsync(workItems, agentId, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            progress.FailCurrent(ex.Message);
            throw;
        }
    }

    public async Task<string> AnalyzeMessagesAsync(ChatAccount account, ChatSession session, IReadOnlyList<ChatMessage> messages, string agentId, CancellationToken cancellationToken = default)
    {
        var progress = CreateProgress(account.DisplayName, session.DisplayName);
        progress.Complete("read-account", $"已选择账号：{account.DisplayName}");
        progress.Complete("read-session", $"已选择会话：{session.DisplayName}");
        progress.Start("read-messages", $"已读取 {messages.Count} 条增量消息");
        if (messages.Count == 0)
        {
            var error = "没有可分析的新消息。";
            progress.FailCurrent(error);
            throw new InvalidOperationException(error);
        }
        progress.Complete("read-messages", $"已读取 {messages.Count} 条增量消息");
        var item = new WeChatAnalysisWorkItem(account, session, messages);
        return await AnalyzeRoundAsync([item], agentId, progress, cancellationToken);
    }

    internal Task<string> AnalyzeIncrementalRoundAsync(
        IReadOnlyList<WeChatAnalysisWorkItem> workItems,
        string agentId,
        Func<WeChatAnalysisWorkItem, Task>? onItemCompleted,
        CancellationToken cancellationToken = default)
    {
        var first = workItems.FirstOrDefault()
                    ?? throw new InvalidOperationException("本轮没有可分析的新消息。");
        var progress = CreateProgress(first.Account.DisplayName, first.Session.DisplayName);
        return AnalyzeRoundAsync(workItems, agentId, progress, cancellationToken, onItemCompleted);
    }

    private async Task<string> AnalyzeRoundAsync(
        IReadOnlyList<WeChatAnalysisWorkItem> workItems,
        string agentId,
        AnalysisProgressTracker progress,
        CancellationToken cancellationToken,
        Func<WeChatAnalysisWorkItem, Task>? onItemCompleted = null)
    {
        if (workItems.Count == 0) throw new InvalidOperationException("本轮没有可分析的新消息。");

        var roundSessionId = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}";
        var roundTitle = $"聊天记录分析_{DateTimeOffset.Now:yyyyMMdd_HHmm}";
        progress.SetRound(roundTitle, workItems.Count, workItems.Sum(x => x.Messages.Count));
        await RunStageAsync(
            progress,
            "qwen-send",
            "正在创建本轮 QwenPaw 分析会话",
            async token =>
            {
                await qwenPaw.CreateSessionAsync(roundSessionId, roundTitle, token);
                return true;
            },
            cancellationToken,
            QwenPawStageTimeout,
            "本轮 QwenPaw 分析会话已创建");

        string? lastResult = null;
        for (var index = 0; index < workItems.Count; index++)
        {
            var item = workItems[index];
            progress.SetContext(item.Account.DisplayName, item.Session.DisplayName);
            progress.SetContact(index + 1);
            progress.Start("read-messages", $"已读取 {item.Messages.Count} 条本联系人新增消息");
            progress.Complete("read-messages", $"已读取 {item.Messages.Count} 条本联系人新增消息");

            lastResult = await AnalyzeMessagesCoreAsync(
                item.Account,
                item.Session,
                item.Messages,
                agentId,
                roundSessionId,
                progress,
                cancellationToken);

            progress.CompleteContact();
            if (onItemCompleted is not null)
                await onItemCompleted(item);
        }

        progress.Start("complete", "正在完成本轮分析");
        progress.Complete("complete", $"本轮已完成 {workItems.Count} 个联系人");
        return lastResult!;
    }

    private async Task<string> AnalyzeMessagesCoreAsync(
        ChatAccount account,
        ChatSession session,
        IReadOnlyList<ChatMessage> messages,
        string agentId,
        string roundSessionId,
        AnalysisProgressTracker progress,
        CancellationToken cancellationToken)
    {
        progress.Start("qwen-send", "正在提交分析请求");
        progress.Complete("qwen-send", "分析请求已提交");
        progress.Start("qwen-analyze", "QwenPaw 正在理解消息并生成结构化结果");

        ConversationCompanyContext? analysisContext = null;
        if (_contextResolver is not null)
        {
            try
            {
                analysisContext = await _contextResolver(account, session, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                progress.Start("qwen-analyze", "本地业务上下文查询超时，继续分析聊天内容");
            }
            catch (Exception ex)
            {
                // Context is an accuracy aid, not a prerequisite for analysis.
                progress.Start("qwen-analyze", $"本地业务上下文不可用，继续分析聊天内容：{ex.Message}");
            }
        }
        var prompt = BuildPrompt(account, session, messages, Settings, analysisContext);
        var result = await RunStageAsync(
            progress,
            "qwen-analyze",
            "QwenPaw 正在理解消息并生成结构化结果",
            token => qwenPaw.ChatAsync(agentId, prompt, roundSessionId, token),
            cancellationToken,
            QwenPawStageTimeout,
            "QwenPaw 已返回分析结果");

        progress.Start("parse-result", "正在校验分析结果");
        AnalysisResult analysis;
        try
        {
            analysis = result.Analysis
                       ?? throw new InvalidOperationException(
                           $"QwenPaw 未返回有效的微信分析 JSON，消息游标不会推进。（响应文本长度：{result.Text.Length}，会话：{result.SessionId ?? "未知"}）");
            progress.Complete("parse-result", $"已识别 {analysis.Todos?.Count ?? 0} 个 TODO 候选、{analysis.Questions?.Count ?? 0} 个客户提问");
        }
        catch (Exception ex)
        {
            progress.FailCurrent(ex.Message);
            throw;
        }

        progress.Start("persist", "正在保存 TODO 候选、通知和分析结果");
        try
        {
            if (Allows(WeChatAnalysisContentType.Todo))
            foreach (var todo in analysis.Todos ?? Array.Empty<TodoCandidate>())
            {
            var todoId = BuildTodoId(account, session, todo);
            var title = string.IsNullOrWhiteSpace(todo.Title) ? "微信工作事项" : todo.Title.Trim();
            var summary = string.IsNullOrWhiteSpace(todo.Summary)
                ? "请查看关联微信消息，补充具体工作内容。"
                : todo.Summary.Trim();
            var candidate = new TodoRecord(
                todoId,
                title,
                summary,
                string.IsNullOrWhiteSpace(todo.Priority) ? "medium" : todo.Priority.Trim(),
                todo.DueAt,
                todo.Assignee,
                Math.Clamp(todo.Confidence, 0, 1),
                todo.SourceRefs ?? Array.Empty<string>(),
                "pending_confirmation",
                DateTimeOffset.UtcNow,
                null,
                null,
                "wechat-analysis",
                session.Username,
                analysisContext?.CompanyId,
                analysisContext?.CompanyName,
                session.DisplayName,
                session.IsGroupConversation,
                BoardId: "default",
                ListId: "pending-confirmation");
            var stored = await _todos.EnsureCandidateAsync(candidate, cancellationToken);
            if (stored is null || !stored.Status.Equals("pending_confirmation", StringComparison.OrdinalIgnoreCase)) continue;

            var notificationKey = $"todo-suggestion:{todoId}";
            if (await notifications.GetByDeduplicationKeyAsync(notificationKey, cancellationToken) is not null) continue;

                await notifications.PublishAsync(new NotificationRequest(
                $"发现工作内容：{candidate.Title}",
                string.IsNullOrWhiteSpace(candidate.CompanyName)
                    ? candidate.Summary
                    : $"公司：{candidate.CompanyName}\n{candidate.Summary}",
                "wechat-analysis",
                "work",
                string.Equals(candidate.Priority, "high", StringComparison.OrdinalIgnoreCase) ? "warning" : "info",
                "close_or_confirm",
                86400,
                notificationKey,
                "action",
                [
                    new NotificationAction("confirm-todo", "确认加入 TODO", "confirm-todo"),
                    new NotificationAction("reject-todo", "忽略", "reject-todo"),
                ],
                null,
                new Dictionary<string, object?> { ["todoId"] = todoId }), cancellationToken);
            }

            if (Allows(WeChatAnalysisContentType.CustomerQuestion))
            foreach (var question in analysis.Questions ?? Array.Empty<ClarificationQuestion>())
            {
            var questionPayload = new QuestionPayload(
                question.Problem,
                question.Source,
                question.Solutions ?? Array.Empty<QuestionSolution>(),
                question.AllowCustomInput);
            var actions = questionPayload.Solutions
                .Select(solution => new NotificationAction(solution.Id, solution.Label, "question-solution"))
                .ToArray();
                await notifications.PublishAsync(new NotificationRequest(
                "微信分析需要确认",
                question.Problem,
                "wechat-analysis",
                "wechat",
                "warning",
                "close_or_confirm",
                86400,
                $"question:{question.Id}",
                "question",
                actions,
                questionPayload,
                new Dictionary<string, object?> { ["questionId"] = question.Id }), cancellationToken);
            }

            await notifications.PublishAsync(new NotificationRequest(
            "微信分析完成",
            $"已分析“{session.DisplayName}”最近 {messages.Count} 条消息，发现 {analysis.Todos?.Count ?? 0} 个工作候选，目标 Agent：{agentId}。",
            "wechat-analysis",
            "wechat",
            "success",
            "timeout",
            8,
            $"analysis:{account.Id}:{session.Username}:{messages.Max(x => x.Id)}"), cancellationToken);

            progress.Complete("persist", "TODO 候选和通知已保存");
            return result.Text;
        }
        catch (Exception ex)
        {
            progress.FailCurrent(ex.Message);
            throw;
        }
    }

    public async Task<string> HandleNotificationActionAsync(NotificationRecord notification, string actionId, string agentId, CancellationToken cancellationToken = default)
    {
        if (notification.IsResolved) throw new InvalidOperationException("该通知已经处理，不能重复执行。");
        var action = notification.Actions.FirstOrDefault(x => x.Id.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        if (action is null) throw new InvalidOperationException("通知操作不存在或已失效。");

        if (action.Kind.Equals("confirm-todo", StringComparison.OrdinalIgnoreCase)
            || action.Kind.Equals("reject-todo", StringComparison.OrdinalIgnoreCase))
        {
            var todoId = GetPayloadString(notification, "todoId");
            if (string.IsNullOrWhiteSpace(todoId)) throw new InvalidOperationException("通知缺少 TODO 标识。");
            var todo = action.Kind.Equals("confirm-todo", StringComparison.OrdinalIgnoreCase)
                ? await _todos.ConfirmAsync(todoId, cancellationToken)
                : await _todos.RejectAsync(todoId, cancellationToken);
            if (todo is null) throw new InvalidOperationException("TODO 候选不存在。");
            await notifications.ResolveAsync(notification.Id, cancellationToken);
            var isConfirmed = action.Kind.Equals("confirm-todo", StringComparison.OrdinalIgnoreCase);
            var message = isConfirmed
                ? $"已将“{todo.Title}”加入 TODO。"
                : $"已忽略工作候选“{todo.Title}”。";
            await notifications.PublishAsync(new NotificationRequest(
                isConfirmed ? "TODO 已确认" : "工作候选已忽略",
                message,
                "wechat-analysis",
                "work",
                "success",
                "timeout",
                8,
                $"todo-action:{notification.Id}:{action.Id}"), cancellationToken);
            return message;
        }

        if (action.Kind.Equals("question-solution", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleQuestionResponseAsync(notification, new NotificationResponse(action.Id, null), agentId, cancellationToken);
        }

        await notifications.ResolveAsync(notification.Id, cancellationToken);
        return "通知已处理。";
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

    private AnalysisProgressTracker CreateProgress(string accountDisplayName, string conversationDisplayName)
        => new(accountDisplayName, conversationDisplayName, snapshot => ProgressChanged?.Invoke(this, snapshot));

    private static InvalidOperationException FailAndCreateException(AnalysisProgressTracker progress, string stageId, string message)
    {
        progress.Fail(stageId, message);
        return new InvalidOperationException(message);
    }

    private static async Task<T> RunStageAsync<T>(
        AnalysisProgressTracker progress,
        string stageId,
        string detail,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken,
        TimeSpan timeout,
        string? completedDetail = null)
    {
        progress.Start(stageId, detail);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var result = await action(timeoutSource.Token);
            progress.Complete(stageId, completedDetail ?? "已完成");
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            var error = $"阶段超过 {timeout.TotalSeconds:0} 秒仍未返回。";
            progress.Fail(stageId, error);
            throw new TimeoutException(error);
        }
        catch (Exception ex)
        {
            progress.FailCurrent(ex.Message);
            throw;
        }
    }

    private string BuildPrompt(
        ChatAccount account,
        ChatSession session,
        IReadOnlyList<ChatMessage> messages,
        WeChatAnalysisSettings settings,
        ConversationCompanyContext? analysisContext)
    {
        var builder = new StringBuilder();
        var contentTypes = settings.ContentTypes.Count == 0
            ? new[] { WeChatAnalysisContentType.Todo, WeChatAnalysisContentType.CustomerQuestion }
            : settings.ContentTypes.Distinct().ToArray();
        builder.AppendLine("你正在处理一个分析轮次中的一个联系人。只分析本提示词中当前联系人区域的消息，不要引用本轮其他联系人或历史上下文。");
        builder.AppendLine($"请分析下面的微信聊天内容，仅识别用户选择的内容类型：{string.Join("、", contentTypes.Select(GetContentTypeName))}。");
        if (contentTypes.Contains(WeChatAnalysisContentType.CustomerQuestion))
            builder.AppendLine("客户提问仅指客户在消息中@我并提出问题，且我在该问题后 10 分钟内没有给出回复；请据此识别并生成 clarification question。");
        builder.AppendLine("最终只返回一个合法 JSON 对象，不要返回 Markdown 代码块、解释文字或推理过程。JSON 必须包含 decision、todos、intents、questions、sourceRefs 五个字段；没有结果时使用空数组，并将 decision 设为 no_action。只输出 ToDo 和客户提问，未选择的类型必须返回空数组。");
        builder.AppendLine("todos 非空时，每个工作事项必须严格包含 id、title、summary、priority、dueAt、assignee、confidence、sourceRefs 字段。id、title、summary、priority 不得为 null 或空字符串；summary 必须用一至两句话说明具体要做什么、与当前聊天的关系及预期结果，禁止只重复标题。priority 只能是 low、medium 或 high；dueAt 和 assignee 未知时写 null；confidence 为 0 至 1 的数值；sourceRefs 至少包含一个下方聊天消息 ID。无法生成完整工作事项时返回 todos: []，禁止返回 [{}] 或缺少 summary 的对象。");
        builder.AppendLine("todos 项示例：{\"id\":\"todo-1\",\"title\":\"确认交付时间\",\"summary\":\"根据客户询问，确认当前排期并在今天回复可交付日期。回复后将结果同步给项目负责人。\",\"priority\":\"medium\",\"dueAt\":null,\"assignee\":null,\"confidence\":0.9,\"sourceRefs\":[\"message-id\"]}");
        builder.AppendLine("所有 JSON 字段必须使用 camelCase，禁止使用 snake_case。questions 非空时，每个问题必须严格包含 id、problem、source、solutions、recommendedSolutionId、allowCustomInput 字段；id、problem、source、solutions、recommendedSolutionId 不得为 null 或空字符串。source 必须包含 conversationId、participants、from、to、messageRefs；solutions 至少包含两个方案，每个方案必须包含 id、label、instruction、recommended，并且只能有一个 recommended 为 true。recommendedSolutionId 必须等于推荐方案的 id。无法生成完整问题时返回 questions: []，禁止返回 [{}] 或字段不完整的问题对象。");
        builder.AppendLine("questions 项示例：{\"id\":\"q-1\",\"problem\":\"需要确认的问题\",\"source\":{\"conversationId\":\"conversation-id\",\"participants\":[\"甲\",\"乙\"],\"from\":\"甲\",\"to\":\"乙\",\"messageRefs\":[\"message-id\"]},\"solutions\":[{\"id\":\"s1\",\"label\":\"方案一\",\"instruction\":\"执行方案一\",\"recommended\":true},{\"id\":\"s2\",\"label\":\"方案二\",\"instruction\":\"执行方案二\",\"recommended\":false}],\"recommendedSolutionId\":\"s1\",\"allowCustomInput\":true}");
        builder.AppendLine("每个 sourceRefs 必须使用下面消息中的消息 ID；不要编造消息 ID。");
        builder.AppendLine($"账号：{account.DisplayName}");
        builder.AppendLine($"会话：{session.DisplayName}");
        builder.AppendLine("本地业务上下文（仅作辅助依据；聊天事实优先；未提供的字段不得猜测）：");
        if (analysisContext is null)
        {
            builder.AppendLine("- 未找到本地联系人、公司或项目映射。");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(analysisContext.ContactName))
                builder.AppendLine($"- 联系人：{analysisContext.ContactName}");
            if (!string.IsNullOrWhiteSpace(analysisContext.CompanyName))
                builder.AppendLine($"- 公司：{analysisContext.CompanyName}");
            if (!string.IsNullOrWhiteSpace(analysisContext.ProjectName))
                builder.AppendLine($"- 项目：{analysisContext.ProjectName}");
            if (analysisContext.ProjectTags is { Count: > 0 })
                builder.AppendLine($"- 项目标签：{string.Join("、", analysisContext.ProjectTags)}");
            builder.AppendLine($"- 匹配方式：{analysisContext.MatchMethod ?? "本地目录匹配"}");
        }
        builder.AppendLine("聊天内容：");
        foreach (var message in messages)
            builder.AppendLine($"[{message.Id}] {message.Sender}: {message.Content}");
        return builder.ToString();
    }

    private bool Allows(WeChatAnalysisContentType contentType)
        => Settings.ContentTypes.Count == 0 || Settings.ContentTypes.Contains(contentType);

    private static string GetContentTypeName(WeChatAnalysisContentType contentType)
        => contentType switch
        {
            WeChatAnalysisContentType.Todo => "ToDo",
            WeChatAnalysisContentType.CustomerQuestion => "客户提问",
            _ => contentType.ToString(),
        };

    private static string BuildTodoId(ChatAccount account, ChatSession session, TodoCandidate todo)
    {
        var sourceRefs = todo.SourceRefs ?? Array.Empty<string>();
        var identity = sourceRefs.Count > 0
            ? string.Join("|", sourceRefs.Order(StringComparer.Ordinal))
            : string.Join("|", todo.Title?.Trim(), todo.Summary?.Trim(), todo.Priority?.Trim(), todo.DueAt?.ToString("O"), todo.Assignee?.Trim());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{account.Id}|{session.Username}|{identity}")))[..24].ToLowerInvariant();
        return $"wechat:{account.Id}:{session.Username}:{hash}";
    }

    private static string? GetPayloadString(NotificationRecord notification, string key)
        => notification.Payload.TryGetValue(key, out var value) ? value?.ToString() : null;
}

internal sealed record WeChatAnalysisWorkItem(
    ChatAccount Account,
    ChatSession Session,
    IReadOnlyList<ChatMessage> Messages,
    string? CursorKey = null,
    string? Cursor = null);

internal sealed class AnalysisProgressTracker
{
    private readonly Action<AnalysisProgressSnapshot> _publish;
    private readonly List<AnalysisStageSnapshot> _stages =
    [
        new("read-account", "读取微信账号", "pending", "等待开始", null, null, null),
        new("read-session", "读取微信会话", "pending", "等待开始", null, null, null),
        new("read-messages", "读取微信消息", "pending", "等待开始", null, null, null),
        new("qwen-send", "发送给 QwenPaw", "pending", "等待开始", null, null, null),
        new("qwen-analyze", "QwenPaw 分析", "pending", "等待开始", null, null, null),
        new("parse-result", "校验分析结果", "pending", "等待开始", null, null, null),
        new("persist", "保存结果和通知", "pending", "等待开始", null, null, null),
        new("complete", "完成", "pending", "等待开始", null, null, null),
    ];

    public AnalysisProgressTracker(string accountDisplayName, string conversationDisplayName, Action<AnalysisProgressSnapshot> publish)
    {
        RunId = Guid.NewGuid().ToString("N");
        AccountDisplayName = accountDisplayName;
        ConversationDisplayName = conversationDisplayName;
        StartedAt = DateTimeOffset.UtcNow;
        _publish = publish;
        Emit();
    }

    public string RunId { get; }
    public string AccountDisplayName { get; private set; }
    public string ConversationDisplayName { get; private set; }
    public DateTimeOffset StartedAt { get; }
    public string RoundTitle { get; private set; } = string.Empty;
    public int TotalContacts { get; private set; }
    public int TotalMessages { get; private set; }
    public int CurrentContactIndex { get; private set; }
    public int CompletedContacts { get; private set; }
    public string Notice { get; private set; } = string.Empty;

    public void SetRound(string roundTitle, int totalContacts, int totalMessages)
    {
        RoundTitle = roundTitle;
        TotalContacts = totalContacts;
        TotalMessages = totalMessages;
        Emit();
    }

    public void SetContact(int index)
    {
        CurrentContactIndex = Math.Clamp(index, 0, TotalContacts);
        Emit();
    }

    public void CompleteContact()
    {
        CompletedContacts = Math.Min(TotalContacts, CompletedContacts + 1);
        Emit();
    }

    public void SetNotice(string notice)
    {
        Notice = notice;
        Emit();
    }

    public void SetContext(string accountDisplayName, string conversationDisplayName)
    {
        AccountDisplayName = accountDisplayName;
        ConversationDisplayName = conversationDisplayName;
        Emit();
    }

    public void Start(string id, string detail)
        => Update(id, "running", detail, DateTimeOffset.UtcNow, null, null);

    public void Complete(string id, string detail)
    {
        var stage = _stages.First(x => x.Id == id);
        Update(id, "completed", detail, stage.StartedAt ?? DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
    }

    public void FailCurrent(string error)
    {
        if (_stages.Any(x => x.Status == "error")) return;
        var stage = _stages.FirstOrDefault(x => x.Status == "running")
                    ?? _stages.FirstOrDefault(x => x.Status == "pending")
                    ?? _stages.Last();
        Fail(stage.Id, error);
    }

    public void Fail(string id, string error)
    {
        var stage = _stages.FirstOrDefault(x => x.Id == id);
        if (stage is null) return;
        Update(stage.Id, "error", "阶段执行失败", stage.StartedAt ?? DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, error);
    }

    private void Update(string id, string status, string detail, DateTimeOffset startedAt, DateTimeOffset? completedAt, string? error)
    {
        var index = _stages.FindIndex(x => x.Id == id);
        if (index < 0) return;
        _stages[index] = _stages[index] with
        {
            Status = status,
            Detail = detail,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Error = error,
        };
        Emit();
    }

    private void Emit()
        => _publish(new AnalysisProgressSnapshot(
            RunId,
            AccountDisplayName,
            ConversationDisplayName,
            StartedAt,
            _stages.ToArray(),
            RoundTitle,
            TotalContacts,
            TotalMessages,
            CurrentContactIndex,
            CompletedContacts,
            Notice));
}
