using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Domain;

public interface INotificationStore
{
    Task<NotificationRecord> AddAsync(NotificationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationRecord>> ListAsync(string? keyword = null, bool unreadOnly = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationRecord>> ListPageAsync(string? keyword, bool unreadOnly, bool readOnly, int offset, int limit, CancellationToken cancellationToken = default);
    Task<bool> MarkReadAsync(string id, CancellationToken cancellationToken = default);
    Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default);
    Task<bool> ResolveAsync(string id, CancellationToken cancellationToken = default);
    Task<NotificationRecord?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<NotificationRecord?> GetByDeduplicationKeyAsync(string key, CancellationToken cancellationToken = default);
}

public interface ITodoStore
{
    Task<TodoRecord?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<TodoRecord?> AddCandidateAsync(TodoRecord candidate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TodoRecord>> ListAsync(bool includeRejected = false, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<TodoRecord?> UpdateStatusAsync(string id, string status, CancellationToken cancellationToken = default);
    Task<TodoRecord?> UpdateDetailsAsync(TodoRecord todo, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<TodoRecord?> MoveAsync(string todoId, string boardId, string listId, int position, string? status = null, CancellationToken cancellationToken = default);
    Task<TodoBoardRecord> GetBoardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TodoListRecord>> ListListsAsync(string boardId, bool includeArchived = false, CancellationToken cancellationToken = default);
    Task<TodoListRecord?> AddListAsync(TodoListRecord list, CancellationToken cancellationToken = default);
    Task<TodoListRecord?> UpdateListAsync(TodoListRecord list, CancellationToken cancellationToken = default);
    Task<TodoListRecord?> MoveListAsync(string id, string boardId, int position, CancellationToken cancellationToken = default);
    Task<bool> ArchiveListAsync(string id, bool isArchived, CancellationToken cancellationToken = default);
    Task<bool> DeleteListAsync(string id, CancellationToken cancellationToken = default);
}

public interface IQwenPawClient
{
    bool IsAuthenticated { get; }
    string? Username { get; }
    Task<QwenPawLoginResult> LoginAsync(string baseUrl, string username, string password, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QwenPawAgent>> GetAgentsAsync(CancellationToken cancellationToken = default);
    Task CreateSessionAsync(string sessionId, string title, CancellationToken cancellationToken = default);
    Task<QwenPawChatResult> ChatAsync(string agentId, string input, CancellationToken cancellationToken = default);
    Task<QwenPawChatResult> ChatAsync(string agentId, string input, string? sessionId, CancellationToken cancellationToken = default);
    Task RenameSessionAsync(string sessionId, string title, CancellationToken cancellationToken = default);
}

public interface IWeChatService
{
    Task<IReadOnlyList<ChatAccount>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatSession>> GetSessionsAsync(string accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatContact>> GetChatContactsAsync(string accountId, bool includeGroups = true, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetLatestMessagesAsync(string accountId, string conversationId, int limit, CancellationToken cancellationToken = default);
}

public interface IWeChatGroupChatProvider
{
    Task<IReadOnlyList<WeChatGroupChatCandidate>> GetAllAsync(CancellationToken cancellationToken = default);
}

public sealed record WeChatGroupChatCandidate(
    string AccountId,
    string AccountDisplayName,
    string WeChatUsername,
    string DisplayName,
    DateTimeOffset? LastMessageAt,
    bool IsGroupChat = true);

public sealed record ConversationCompanyContext(
    string? CompanyId,
    string? CompanyName,
    string? ContactId = null,
    string? ContactName = null,
    string? ProjectId = null,
    string? ProjectName = null,
    IReadOnlyList<string>? ProjectTags = null,
    string? MatchMethod = null);

public interface IProtectedTokenStore
{
    Task SaveAsync(string token, CancellationToken cancellationToken = default);
    Task<string?> LoadAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class NotificationService(INotificationStore store)
{
    public event EventHandler<NotificationRecord>? NotificationAdded;

    public async Task<NotificationRecord> PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        var result = await store.AddAsync(request, cancellationToken);
        NotificationAdded?.Invoke(this, result);
        return result;
    }

    public Task<IReadOnlyList<NotificationRecord>> ListAsync(string? keyword = null, bool unreadOnly = false, CancellationToken cancellationToken = default)
        => store.ListAsync(keyword, unreadOnly, cancellationToken);

    public Task<IReadOnlyList<NotificationRecord>> ListPageAsync(string? keyword, bool unreadOnly, bool readOnly, int offset, int limit, CancellationToken cancellationToken = default)
        => store.ListPageAsync(keyword, unreadOnly, readOnly, offset, limit, cancellationToken);

    public Task<bool> MarkReadAsync(string id, CancellationToken cancellationToken = default)
        => store.MarkReadAsync(id, cancellationToken);

    public Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default)
        => store.MarkAllReadAsync(cancellationToken);

    public Task<bool> ResolveAsync(string id, CancellationToken cancellationToken = default)
        => store.ResolveAsync(id, cancellationToken);

    public Task<NotificationRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
        => store.GetAsync(id, cancellationToken);

    public Task<NotificationRecord?> GetByDeduplicationKeyAsync(string key, CancellationToken cancellationToken = default)
        => store.GetByDeduplicationKeyAsync(key, cancellationToken);
}

public sealed class TodoService(ITodoStore store)
{
    public event EventHandler<TodoRecord>? TodoChanged;

    public Task<IReadOnlyList<TodoRecord>> ListAsync(bool includeRejected = false, bool includeArchived = false, CancellationToken cancellationToken = default)
        => store.ListAsync(includeRejected, includeArchived, cancellationToken);

    public async Task<TodoRecord?> AddCandidateAsync(TodoRecord candidate, CancellationToken cancellationToken = default)
    {
        var result = await store.AddCandidateAsync(candidate, cancellationToken);
        if (result is not null) TodoChanged?.Invoke(this, result);
        return result;
    }

    public async Task<TodoRecord?> EnsureCandidateAsync(TodoRecord candidate, CancellationToken cancellationToken = default)
    {
        var existing = await store.GetAsync(candidate.Id, cancellationToken);
        if (existing is not null)
        {
            if (!ShouldRefreshIncompleteAnalysisCandidate(existing, candidate)) return existing;

            var updated = await store.UpdateDetailsAsync(existing with
            {
                Title = string.Equals(existing.Title, "微信工作事项", StringComparison.Ordinal)
                    ? candidate.Title
                    : existing.Title,
                Summary = candidate.Summary,
            }, cancellationToken);
            if (updated is not null) TodoChanged?.Invoke(this, updated);
            return updated ?? existing;
        }

        var inserted = await store.AddCandidateAsync(candidate, cancellationToken);
        if (inserted is not null) TodoChanged?.Invoke(this, inserted);
        return inserted ?? await store.GetAsync(candidate.Id, cancellationToken);
    }

    public Task<TodoRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
        => store.GetAsync(id, cancellationToken);

    public async Task<TodoRecord?> ConfirmAsync(string id, CancellationToken cancellationToken = default)
        => await UpdateStatusAsync(id, "pending", cancellationToken);

    public async Task<TodoRecord?> RejectAsync(string id, CancellationToken cancellationToken = default)
        => await UpdateStatusAsync(id, "rejected", cancellationToken);

    public Task<TodoRecord?> CompleteAsync(string id, CancellationToken cancellationToken = default)
        => UpdateStatusAsync(id, "completed", cancellationToken);

    public async Task<TodoRecord?> UpdateDetailsAsync(TodoRecord todo, CancellationToken cancellationToken = default)
    {
        var result = await store.UpdateDetailsAsync(todo, cancellationToken);
        if (result is not null) TodoChanged?.Invoke(this, result);
        return result;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => store.DeleteAsync(id, cancellationToken);

    public async Task<TodoRecord?> MoveAsync(string todoId, string boardId, string listId, int position, string? status = null, CancellationToken cancellationToken = default)
    {
        var result = await store.MoveAsync(todoId, boardId, listId, position, status, cancellationToken);
        if (result is not null) TodoChanged?.Invoke(this, result);
        return result;
    }

    public Task<TodoBoardRecord> GetBoardAsync(CancellationToken cancellationToken = default)
        => store.GetBoardAsync(cancellationToken);

    public Task<IReadOnlyList<TodoListRecord>> ListListsAsync(string boardId, bool includeArchived = false, CancellationToken cancellationToken = default)
        => store.ListListsAsync(boardId, includeArchived, cancellationToken);

    public async Task<TodoListRecord?> AddListAsync(TodoListRecord list, CancellationToken cancellationToken = default)
        => await store.AddListAsync(list, cancellationToken);

    public async Task<TodoListRecord?> UpdateListAsync(TodoListRecord list, CancellationToken cancellationToken = default)
        => await store.UpdateListAsync(list, cancellationToken);

    public Task<TodoListRecord?> MoveListAsync(string id, string boardId, int position, CancellationToken cancellationToken = default)
        => store.MoveListAsync(id, boardId, position, cancellationToken);

    public Task<bool> ArchiveListAsync(string id, bool isArchived, CancellationToken cancellationToken = default)
        => store.ArchiveListAsync(id, isArchived, cancellationToken);

    public Task<bool> DeleteListAsync(string id, CancellationToken cancellationToken = default)
        => store.DeleteListAsync(id, cancellationToken);

    private async Task<TodoRecord?> UpdateStatusAsync(string id, string status, CancellationToken cancellationToken)
    {
        var result = await store.UpdateStatusAsync(id, status, cancellationToken);
        if (result is not null) TodoChanged?.Invoke(this, result);
        return result;
    }

    private static bool ShouldRefreshIncompleteAnalysisCandidate(TodoRecord existing, TodoRecord candidate)
        => string.Equals(existing.Source, "wechat-analysis", StringComparison.OrdinalIgnoreCase)
           && string.Equals(candidate.Source, "wechat-analysis", StringComparison.OrdinalIgnoreCase)
           && string.Equals(existing.Status, "pending_confirmation", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(candidate.Summary)
           && (string.IsNullOrWhiteSpace(existing.Summary)
               || string.Equals(existing.Summary, "请查看关联微信消息，补充具体工作内容。", StringComparison.Ordinal));
}
