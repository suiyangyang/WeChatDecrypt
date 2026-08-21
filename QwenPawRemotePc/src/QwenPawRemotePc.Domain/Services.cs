using QwenPawRemotePc.Protocol;

namespace QwenPawRemotePc.Domain;

public interface INotificationStore
{
    Task<NotificationRecord> AddAsync(NotificationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationRecord>> ListAsync(string? keyword = null, bool unreadOnly = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationRecord>> ListPageAsync(string? keyword, bool unreadOnly, bool readOnly, int offset, int limit, CancellationToken cancellationToken = default);
    Task<bool> MarkReadAsync(string id, CancellationToken cancellationToken = default);
    Task<int> MarkAllReadAsync(CancellationToken cancellationToken = default);
    Task<bool> ResolveAsync(string id, CancellationToken cancellationToken = default);
    Task<NotificationRecord?> GetAsync(string id, CancellationToken cancellationToken = default);
}

public interface IQwenPawClient
{
    bool IsAuthenticated { get; }
    string? Username { get; }
    Task<QwenPawLoginResult> LoginAsync(string baseUrl, string username, string password, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QwenPawAgent>> GetAgentsAsync(CancellationToken cancellationToken = default);
    Task<QwenPawChatResult> ChatAsync(string agentId, string input, CancellationToken cancellationToken = default);
}

public interface IWeChatService
{
    Task<IReadOnlyList<ChatAccount>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatSession>> GetSessionsAsync(string accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetLatestMessagesAsync(string accountId, string conversationId, int limit, CancellationToken cancellationToken = default);
}

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
}
