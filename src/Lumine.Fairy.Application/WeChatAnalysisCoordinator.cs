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
    Func<ChatAccount, ChatSession, CancellationToken, Task<ConversationCompanyContext?>>? companyResolver = null)
{
    private static readonly TimeSpan WeChatStageTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QwenPawStageTimeout = TimeSpan.FromMinutes(3);
    private readonly TodoService _todos = todos;
    private readonly Func<ChatAccount, ChatSession, CancellationToken, Task<ConversationCompanyContext?>>? _companyResolver = companyResolver;

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

            string? lastResult = null;
            var analyzedSessionCount = 0;
            foreach (var session in allowedSessions)
            {
                progress.SetContext(account.DisplayName, session.DisplayName);
                var messages = await RunStageAsync(progress, "read-messages", "正在读取最近消息", token => weChat.GetLatestMessagesAsync(account.Id, session.Username, 50, token), cancellationToken, WeChatStageTimeout);
                if (Settings.AutomaticMode != WeChatAutomaticAnalysisMode.Unanalyzed)
                    messages = WeChatAnalysisSelection.SelectMessages(messages, Settings.AutomaticMode, null);
                if (messages.Count == 0) continue;

                lastResult = await AnalyzeMessagesCoreAsync(account, session, messages, agentId, progress, cancellationToken);
                analyzedSessionCount++;
            }

            if (analyzedSessionCount == 0)
                throw FailAndCreateException(progress, "read-messages", "匹配的微信会话没有可分析的新消息。");

            return lastResult!;
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
        return await AnalyzeMessagesCoreAsync(account, session, messages, agentId, progress, cancellationToken);
    }

    private async Task<string> AnalyzeMessagesCoreAsync(ChatAccount account, ChatSession session, IReadOnlyList<ChatMessage> messages, string agentId, AnalysisProgressTracker progress, CancellationToken cancellationToken)
    {
        progress.Start("qwen-send", "正在提交分析请求");
        progress.Complete("qwen-send", "分析请求已提交");
        progress.Start("qwen-analyze", "QwenPaw 正在理解消息并生成结构化结果");

        var prompt = BuildPrompt(account, session, messages, Settings);
        var result = await RunStageAsync(
            progress,
            "qwen-analyze",
            "QwenPaw 正在理解消息并生成结构化结果",
            token => qwenPaw.ChatAsync(agentId, prompt, token),
            cancellationToken,
            QwenPawStageTimeout,
            "QwenPaw 已返回分析结果");

        progress.Start("parse-result", "正在校验分析结果");
        AnalysisResult analysis;
        try
        {
            analysis = result.Analysis
                       ?? throw new InvalidOperationException("QwenPaw 未返回有效的微信分析 JSON，消息游标不会推进。");
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
            var companyContext = _companyResolver is null
                ? null
                : await _companyResolver(account, session, cancellationToken);

            if (Allows(WeChatAnalysisContentType.Todo))
            foreach (var todo in analysis.Todos ?? Array.Empty<TodoCandidate>())
            {
            var todoId = BuildTodoId(account, session, todo);
            var candidate = new TodoRecord(
                todoId,
                string.IsNullOrWhiteSpace(todo.Title) ? "微信工作事项" : todo.Title.Trim(),
                todo.Summary?.Trim() ?? string.Empty,
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
                companyContext?.CompanyId,
                companyContext?.CompanyName,
                session.DisplayName,
                session.IsGroupConversation);
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
            progress.Start("complete", "正在完成本次分析");
            progress.Complete("complete", "本次微信分析已完成");
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

    private string BuildPrompt(ChatAccount account, ChatSession session, IReadOnlyList<ChatMessage> messages, WeChatAnalysisSettings settings)
    {
        var builder = new StringBuilder();
        var contentTypes = settings.ContentTypes.Count == 0
            ? new[] { WeChatAnalysisContentType.Todo, WeChatAnalysisContentType.CustomerQuestion }
            : settings.ContentTypes.Distinct().ToArray();
        builder.AppendLine($"请分析下面的微信聊天内容，仅识别用户选择的内容类型：{string.Join("、", contentTypes.Select(GetContentTypeName))}。");
        if (contentTypes.Contains(WeChatAnalysisContentType.CustomerQuestion))
            builder.AppendLine("客户提问仅指客户在消息中@我并提出问题，且我在该问题后 10 分钟内没有给出回复；请据此识别并生成 clarification question。");
        builder.AppendLine("请尽量只返回 JSON，结构为 AnalysisResult；如果没有事项，decision 返回 no_action。");
        builder.AppendLine($"账号：{account.DisplayName}");
        builder.AppendLine($"会话：{session.DisplayName}");
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
        => _publish(new AnalysisProgressSnapshot(RunId, AccountDisplayName, ConversationDisplayName, StartedAt, _stages.ToArray()));
}
