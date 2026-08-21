using System.Text.Json.Serialization;

namespace QwenPawRemotePc.Protocol;

public sealed record QwenPawAgent(
    string Id,
    string Name,
    string Description,
    bool Enabled);

public sealed record NotificationAction(
    string Id,
    string Label,
    string Kind = "open");

public sealed record NotificationRequest(
    string Title,
    string Body,
    string Source = "local",
    string Category = "other",
    string Severity = "info",
    string DismissPolicy = "timeout",
    int? DurationSeconds = null,
    string? DeduplicationKey = null,
    string NotificationType = "normal",
    IReadOnlyList<NotificationAction>? Actions = null,
    QuestionPayload? Question = null,
    IReadOnlyDictionary<string, object?>? Payload = null);

public sealed record QuestionPayload(
    string Problem,
    QuestionSource Source,
    IReadOnlyList<QuestionSolution> Solutions,
    bool AllowCustomInput = true,
    string InputPlaceholder = "不使用以上解决方案时，请输入你的处理要求");

public sealed record QuestionSource(
    string ConversationId,
    IReadOnlyList<string> Participants,
    string From,
    string To,
    IReadOnlyList<string> MessageRefs);

public sealed record QuestionSolution(
    string Id,
    string Label,
    string Instruction,
    bool Recommended);

public sealed record NotificationResponse(
    string? SolutionId,
    string? CustomInput);

public sealed record NotificationRecord(
    string Id,
    string Title,
    string Body,
    string Source,
    string Category,
    string Severity,
    string DismissPolicy,
    int DurationSeconds,
    string NotificationType,
    DateTimeOffset CreatedAt,
    bool IsRead,
    bool IsResolved,
    string? DeduplicationKey,
    IReadOnlyList<NotificationAction> Actions,
    QuestionPayload? Question,
    string? RelatedTaskId);

public sealed record QwenPawLoginResult(string Token, string Username);

public sealed record ChatAccount(string Id, string DisplayName, int DatabaseCount);

public sealed record ChatSession(string Username, string DisplayName, string Preview, long LastMessageTime);

public sealed record ChatMessage(string Id, string Sender, string Content, long Timestamp);

public sealed record WeChatAnalysisRequest(
    string AccountId,
    string ConversationId,
    IReadOnlyList<ChatMessage> Messages,
    string AgentId);

public sealed record AnalysisResult(
    string Decision,
    IReadOnlyList<TodoCandidate> Todos,
    IReadOnlyList<IntentCandidate> Intents,
    IReadOnlyList<ClarificationQuestion> Questions,
    IReadOnlyList<string> SourceRefs);

public sealed record TodoCandidate(
    string Id,
    string Title,
    string Summary,
    string Priority,
    DateTimeOffset? DueAt,
    string? Assignee,
    double Confidence,
    IReadOnlyList<string> SourceRefs);

public sealed record IntentCandidate(
    string Id,
    string Type,
    string Title,
    string Summary,
    double Confidence,
    IReadOnlyList<string> SourceRefs);

public sealed record ClarificationQuestion(
    string Id,
    string Problem,
    QuestionSource Source,
    IReadOnlyList<QuestionSolution> Solutions,
    string RecommendedSolutionId,
    bool AllowCustomInput = true);

public sealed record QwenPawChatResult(
    string Text,
    string? SessionId,
    AnalysisResult? Analysis);

public sealed record ApiResponse(string Status, string? Message = null);
