using System.Text.Json;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Application;

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
    private readonly string _cursorPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumine.Fairy", "analysis-cursors.json");
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
    public string AgentId { get; set; } = "default";
    public WeChatAnalysisSettings Settings { get; set; } = new();
    public IReadOnlyList<WeChatWhitelistEntry> Whitelist { get; set; } = [];
    public Func<CancellationToken, Task<IReadOnlyList<WeChatWhitelistEntry>>>? WhitelistLoader { get; set; }

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
                if (WhitelistLoader is not null)
                    Whitelist = await WhitelistLoader(cancellationToken);
                var accounts = await _weChat.GetAccountsAsync(cancellationToken);
                foreach (var account in accounts)
                {
                    var sessions = await _weChat.GetSessionsAsync(account.Id, cancellationToken);
                    foreach (var session in sessions)
                    {
                        try
                        {
                            await ProcessSessionAsync(account, session, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            await _notifications.PublishAsync(new Lumine.Fairy.Protocol.NotificationRequest(
                                "微信增量分析失败",
                                ex.Message,
                                "wechat-analysis",
                                "wechat",
                                "warning",
                                "timeout",
                                8,
                                $"analysis-error:{account.Id}:{session.Username}:{DateTimeOffset.UtcNow:yyyyMMddHHmm}"), cancellationToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                await _notifications.PublishAsync(new Lumine.Fairy.Protocol.NotificationRequest(
                    "微信增量分析失败", ex.Message, "wechat-analysis", "wechat", "warning", "timeout", 8,
                    $"analysis-error:poll:{DateTimeOffset.UtcNow:yyyyMMddHHmm}"), cancellationToken);
            }
        }
    }

    private async Task ProcessSessionAsync(ChatAccount account, ChatSession session, CancellationToken cancellationToken)
    {
        if (!WeChatAnalysisSelection.IsSessionAllowed(session, Settings, Whitelist)) return;

        var messages = await _weChat.GetLatestMessagesAsync(account.Id, session.Username, 50, cancellationToken);
        var latest = messages.LastOrDefault();
        if (latest is null) return;

        var cursorKey = $"{account.Id}:{session.Username}";
        var cursor = WeChatAnalysisSelection.GetCursor(latest);
        _cursors.TryGetValue(cursorKey, out var previous);
        var newMessages = WeChatAnalysisSelection.SelectMessages(messages, Settings.AutomaticMode, previous);
        if (newMessages.Count == 0)
        {
            if (Settings.AutomaticMode == WeChatAutomaticAnalysisMode.Unanalyzed && previous is null)
            {
                _cursors[cursorKey] = cursor;
                SaveCursors();
            }
            return;
        }

        await _coordinator.AnalyzeMessagesAsync(account, session, newMessages, AgentId, cancellationToken);
        _cursors[cursorKey] = cursor;
        SaveCursors();
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
