using System.Text.Json;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Application;

/// <summary>
/// Runs the configurable polling loop and persists one cursor per account/conversation.
/// </summary>
public sealed class IncrementalAnalysisScheduler : IAsyncDisposable
{
    private readonly IWeChatService _weChat;
    private readonly IQwenPawClient _qwenPaw;
    private readonly WeChatAnalysisCoordinator _coordinator;
    private readonly NotificationService _notifications;
    private readonly Dictionary<string, string> _cursors;
    private readonly object _cursorGate = new();
    private readonly SemaphoreSlim _cursorOperationGate = new(1, 1);
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
    private WeChatAnalysisSettings _settings = new();
    private bool _usesConfiguredInterval = true;

    public TimeSpan Interval { get; private set; } = TimeSpan.FromMinutes(WeChatAnalysisSettings.DefaultAnalysisIntervalMinutes);
    public string AgentId { get; set; } = "default";
    public WeChatAnalysisSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value ?? new WeChatAnalysisSettings();
            if (_usesConfiguredInterval)
                Interval = GetConfiguredInterval(_settings);
        }
    }
    public IReadOnlyList<WeChatWhitelistEntry> Whitelist { get; set; } = [];
    public Func<CancellationToken, Task<IReadOnlyList<WeChatWhitelistEntry>>>? WhitelistLoader { get; set; }

    public void Start(TimeSpan? interval = null)
    {
        if (IsRunning) return;
        _usesConfiguredInterval = interval is null;
        Interval = interval ?? GetConfiguredInterval(Settings);
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
    }

    public async Task<int> SetLatestAnalysisPointAsync(CancellationToken cancellationToken = default)
    {
        await _cursorOperationGate.WaitAsync(cancellationToken);
        try
        {
            if (WhitelistLoader is not null)
                Whitelist = await WhitelistLoader(cancellationToken);

            var cursorUpdates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var accounts = await _weChat.GetAccountsAsync(cancellationToken);
            foreach (var account in accounts)
            {
                var sessions = await _weChat.GetSessionsAsync(account.Id, cancellationToken);
                foreach (var session in sessions)
                {
                    if (!WeChatAnalysisSelection.IsSessionAllowed(session, Settings, Whitelist)) continue;

                    var messages = await _weChat.GetLatestMessagesAsync(account.Id, session.Username, 1, cancellationToken);
                    var latest = messages.LastOrDefault();
                    if (latest is null) continue;

                    cursorUpdates[$"{account.Id}:{session.Username}"] = WeChatAnalysisSelection.GetCursor(latest);
                }
            }

            if (cursorUpdates.Count == 0) return 0;

            lock (_cursorGate)
            {
                foreach (var update in cursorUpdates)
                    _cursors[update.Key] = update.Value;
                SaveCursorsLocked();
            }

            return cursorUpdates.Count;
        }
        finally
        {
            _cursorOperationGate.Release();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(Interval, cancellationToken);
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

    private static TimeSpan GetConfiguredInterval(WeChatAnalysisSettings settings)
        => TimeSpan.FromMinutes(WeChatAnalysisSettings.NormalizeAnalysisIntervalMinutes(settings.AnalysisIntervalMinutes));

    private async Task ProcessSessionAsync(ChatAccount account, ChatSession session, CancellationToken cancellationToken)
    {
        await _cursorOperationGate.WaitAsync(cancellationToken);
        try
        {
            await ProcessSessionCoreAsync(account, session, cancellationToken);
        }
        finally
        {
            _cursorOperationGate.Release();
        }
    }

    private async Task ProcessSessionCoreAsync(ChatAccount account, ChatSession session, CancellationToken cancellationToken)
    {
        if (!WeChatAnalysisSelection.IsSessionAllowed(session, Settings, Whitelist)) return;

        var messages = await _weChat.GetLatestMessagesAsync(account.Id, session.Username, 50, cancellationToken);
        var latest = messages.LastOrDefault();
        if (latest is null) return;

        var cursorKey = $"{account.Id}:{session.Username}";
        var cursor = WeChatAnalysisSelection.GetCursor(latest);
        string? previous;
        lock (_cursorGate)
            _cursors.TryGetValue(cursorKey, out previous);
        var newMessages = WeChatAnalysisSelection.SelectMessages(messages, Settings.AutomaticMode, previous);
        if (newMessages.Count == 0)
        {
            if (Settings.AutomaticMode == WeChatAutomaticAnalysisMode.Unanalyzed && previous is null)
                SaveCursor(cursorKey, cursor);
            return;
        }

        await _coordinator.AnalyzeMessagesAsync(account, session, newMessages, AgentId, cancellationToken);
        SaveCursor(cursorKey, cursor);
    }

    private void SaveCursor(string cursorKey, string cursor)
    {
        lock (_cursorGate)
        {
            _cursors[cursorKey] = cursor;
            SaveCursorsLocked();
        }
    }

    private void SaveCursorsLocked()
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
