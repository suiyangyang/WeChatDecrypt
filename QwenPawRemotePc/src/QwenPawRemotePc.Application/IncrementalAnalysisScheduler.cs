using System.Text.Json;
using QwenPawRemotePc.Domain;

namespace QwenPawRemotePc.Application;

/// <summary>
/// Runs the phase-one five-minute polling loop and persists one cursor per account/conversation.
/// </summary>
public sealed class IncrementalAnalysisScheduler : IAsyncDisposable
{
    private readonly IWeChatService _weChat;
    private readonly IQwenPawClient _qwenPaw;
    private readonly WeChatAnalysisCoordinator _coordinator;
    private readonly NotificationService _notifications;
    private readonly Dictionary<string, string> _cursors;
    private readonly string _cursorPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QwenPawRemotePc", "analysis-cursors.json");
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public IncrementalAnalysisScheduler(IWeChatService weChat, IQwenPawClient qwenPaw, WeChatAnalysisCoordinator coordinator, NotificationService notifications)
    {
        _weChat = weChat;
        _qwenPaw = qwenPaw;
        _coordinator = coordinator;
        _notifications = notifications;
        _cursors = LoadCursors(_cursorPath);
    }

    public bool IsRunning => _worker is { IsCompleted: false };
    public TimeSpan Interval { get; private set; } = TimeSpan.FromMinutes(5);

    public void Start(TimeSpan? interval = null)
    {
        if (IsRunning) return;
        Interval = interval ?? TimeSpan.FromMinutes(5);
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                if (!_qwenPaw.IsAuthenticated) continue;
                var accounts = await _weChat.GetAccountsAsync(cancellationToken);
                var account = accounts.OrderByDescending(x => x.DatabaseCount).FirstOrDefault();
                if (account is null) continue;
                var session = (await _weChat.GetSessionsAsync(account.Id, cancellationToken)).FirstOrDefault();
                if (session is null) continue;
                var messages = await _weChat.GetLatestMessagesAsync(account.Id, session.Username, 50, cancellationToken);
                var latest = messages.LastOrDefault();
                if (latest is null) continue;

                var cursorKey = $"{account.Id}:{session.Username}";
                var cursor = $"{latest.Id}:{latest.Timestamp}";
                if (!_cursors.TryGetValue(cursorKey, out var previous))
                {
                    _cursors[cursorKey] = cursor;
                    SaveCursors();
                    continue;
                }
                if (previous == cursor) continue;

                var previousId = previous.Split(':', 2)[0];
                var previousIndex = messages.ToList().FindIndex(x => x.Id == previousId);
                var newMessages = previousIndex >= 0 ? messages.Skip(previousIndex + 1).ToList() : messages.ToList();
                await _coordinator.AnalyzeMessagesAsync(account, session, newMessages, "default", cancellationToken);
                _cursors[cursorKey] = cursor;
                SaveCursors();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                await _notifications.PublishAsync(new QwenPawRemotePc.Protocol.NotificationRequest(
                    "微信增量分析失败", ex.Message, "wechat-analysis", "wechat", "warning", "timeout", 8,
                    $"analysis-error:{DateTimeOffset.UtcNow:yyyyMMddHHmm}"), cancellationToken);
            }
        }
    }

    private void SaveCursors()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cursorPath)!);
        File.WriteAllText(_cursorPath, JsonSerializer.Serialize(_cursors));
    }

    private static Dictionary<string, string> LoadCursors(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_worker is not null)
        {
            try { await _worker; } catch (OperationCanceledException) { }
        }
        _cts?.Dispose();
    }
}
