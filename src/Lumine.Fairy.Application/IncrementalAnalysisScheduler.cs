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
    private static readonly TimeSpan RoundHandoffTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CollectionReportInterval = TimeSpan.FromMilliseconds(500);

    private readonly Dictionary<string, string> _cursors;
    private readonly object _cursorGate = new();
    private readonly object _delayGate = new();
    private readonly SemaphoreSlim _cursorOperationGate = new(1, 1);
    private readonly string _cursorPath;
    private int _roundRunningCount;
    private volatile bool _immediateRunRequested;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public IncrementalAnalysisScheduler(
        IWeChatService weChat,
        IQwenPawClient qwenPaw,
        WeChatAnalysisCoordinator coordinator,
        NotificationService notifications,
        string? cursorPath = null)
    {
        _weChat = weChat;
        _qwenPaw = qwenPaw;
        _coordinator = coordinator;
        _notifications = notifications;
        _cursorPath = cursorPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumine.Fairy", "analysis-cursors.json");
        _cursors = LoadCursors(_cursorPath);
    }

    public bool IsRunning => _worker is { IsCompleted: false };

    /// <summary>是否有一轮微信分析（手动或自动）正在执行。</summary>
    public bool IsRoundRunning => Volatile.Read(ref _roundRunningCount) > 0;

    /// <summary>是否已有请求排队，等待当前轮次结束后立即补跑。</summary>
    public bool IsRunQueued => _immediateRunRequested;
    private WeChatAnalysisSettings _settings = new();
    private bool _usesConfiguredInterval = true;
    private CancellationTokenSource? _delayCts;

    public TimeSpan Interval { get; private set; } = TimeSpan.FromMinutes(WeChatAnalysisSettings.DefaultAnalysisIntervalMinutes);
    public string AgentId { get; set; } = "default";
    public WeChatAnalysisSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value ?? new WeChatAnalysisSettings();
            if (_usesConfiguredInterval)
                ApplyInterval(GetConfiguredInterval(_settings));
        }
    }

    private void ApplyInterval(TimeSpan interval)
    {
        if (Interval == interval) return;
        Interval = interval;
        CancelPendingDelay();
    }

    /// <summary>中断当前等待：间隔变更时只重新计时，存在排队请求时立即补跑一轮。</summary>
    private void CancelPendingDelay()
    {
        lock (_delayGate)
        {
            try { _delayCts?.Cancel(); }
            catch (ObjectDisposedException) { }
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

    /// <summary>
    /// 请求立即补跑一轮：打断正在等待的间隔，多次请求合并为一轮。调度器未运行时返回 false。
    /// </summary>
    public bool RequestImmediateRun()
    {
        if (!IsRunning) return false;
        _immediateRunRequested = true;
        CancelPendingDelay();
        return true;
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

    public async Task<string> AnalyzeNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_qwenPaw.IsAuthenticated) throw new InvalidOperationException("尚未登录 QwenPaw。");

        if (!await TryEnterRoundAsync(cancellationToken))
        {
            var queued = RequestImmediateRun();
            throw new WeChatAnalysisRoundInProgressException(queued
                ? "已有微信分析正在执行，本次请求已排队，当前轮次结束后会立即自动补跑。"
                : "已有微信分析正在执行，请稍后再试。");
        }

        try
        {
            var progress = _coordinator.BeginProgress("微信账号", "最近会话");
            if (WhitelistLoader is not null)
                Whitelist = await WhitelistLoader(cancellationToken);

            var accounts = await _weChat.GetAccountsAsync(cancellationToken);
            return await RunRoundAsync(accounts, progress, cancellationToken);
        }
        finally
        {
            ExitRound();
        }
    }

    private async Task<bool> TryEnterRoundAsync(CancellationToken cancellationToken)
    {
        if (!await _cursorOperationGate.WaitAsync(RoundHandoffTimeout, cancellationToken))
            return false;

        Interlocked.Increment(ref _roundRunningCount);
        return true;
    }

    private void ExitRound()
    {
        Interlocked.Decrement(ref _roundRunningCount);
        _cursorOperationGate.Release();
    }

    private async Task<string> RunRoundAsync(
        IReadOnlyList<ChatAccount> accounts,
        AnalysisProgressTracker progress,
        CancellationToken cancellationToken)
    {
        progress.Complete("read-account", $"已读取 {accounts.Count} 个微信账号");
        var workItems = await CollectWorkItemsAsync(accounts, progress, cancellationToken);
        if (workItems.Count == 0)
        {
            progress.Finish("白名单会话没有待分析的新消息。");
            throw new NoPendingWeChatMessagesException("匹配的微信会话没有可分析的新消息。");
        }

        return await AnalyzeWorkItemsAsync(workItems, progress, cancellationToken);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!_immediateRunRequested && !await WaitForNextRoundAsync(cancellationToken)) return;
            _immediateRunRequested = false;

            try
            {
                if (_qwenPaw.IsAuthenticated)
                    await ProcessRoundAsync(cancellationToken);
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

    /// <summary>等待下一轮间隔；返回 false 表示调度器已停止，排队请求会跳过等待立即执行。</summary>
    private async Task<bool> WaitForNextRoundAsync(CancellationToken cancellationToken)
    {
        while (!_immediateRunRequested)
        {
            CancellationTokenSource delayCts;
            lock (_delayGate)
            {
                _delayCts?.Dispose();
                delayCts = _delayCts = new CancellationTokenSource();
            }
            using var delayToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, delayCts.Token);
            try
            {
                await Task.Delay(Interval, delayToken.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) return false;
            }
        }

        return true;
    }

    private static TimeSpan GetConfiguredInterval(WeChatAnalysisSettings settings)
        => TimeSpan.FromMinutes(WeChatAnalysisSettings.NormalizeAnalysisIntervalMinutes(settings.AnalysisIntervalMinutes));

    private async Task ProcessRoundAsync(CancellationToken cancellationToken)
    {
        if (!await TryEnterRoundAsync(cancellationToken))
        {
            // 手动分析占用中，等它结束后立即补跑，不丢掉这一轮。
            _immediateRunRequested = true;
            return;
        }

        try
        {
            var progress = _coordinator.BeginProgress("微信账号", "自动增量分析");
            if (WhitelistLoader is not null)
                Whitelist = await WhitelistLoader(cancellationToken);

            var accounts = await _weChat.GetAccountsAsync(cancellationToken);
            await RunRoundAsync(accounts, progress, cancellationToken);
        }
        catch (NoPendingWeChatMessagesException)
        {
            // 自动轮询没有新消息属于正常情况，不产生失败通知。
        }
        finally
        {
            ExitRound();
        }
    }

    private async Task<string> AnalyzeWorkItemsAsync(
        IReadOnlyList<WeChatAnalysisWorkItem> workItems,
        AnalysisProgressTracker progress,
        CancellationToken cancellationToken)
        => await _coordinator.AnalyzeIncrementalRoundAsync(
            workItems,
            AgentId,
            item =>
            {
                if (!string.IsNullOrWhiteSpace(item.CursorKey) && !string.IsNullOrWhiteSpace(item.Cursor))
                    SaveCursor(item.CursorKey, item.Cursor);
                return Task.CompletedTask;
            },
            progress,
            cancellationToken);

    private async Task<IReadOnlyList<WeChatAnalysisWorkItem>> CollectWorkItemsAsync(
        IReadOnlyList<ChatAccount> accounts,
        AnalysisProgressTracker progress,
        CancellationToken cancellationToken)
    {
        var targets = new List<(ChatAccount Account, ChatSession Session)>();
        progress.Start("read-session", "正在读取微信会话并按白名单筛选");
        foreach (var account in accounts)
        {
            var sessions = await _weChat.GetSessionsAsync(account.Id, cancellationToken);
            progress.SetContext(account.DisplayName, $"共 {sessions.Count} 个会话");
            targets.AddRange(sessions
                .Where(session => WeChatAnalysisSelection.IsSessionAllowed(session, Settings, Whitelist))
                .Select(session => (account, session)));
        }
        progress.Complete("read-session", $"白名单命中 {targets.Count} 个会话");

        var workItems = new List<WeChatAnalysisWorkItem>();
        var lastReportAt = DateTimeOffset.MinValue;
        for (var index = 0; index < targets.Count; index++)
        {
            var (account, session) = targets[index];
            var workItem = await PrepareSessionAsync(account, session, cancellationToken);
            if (workItem is not null)
                workItems.Add(workItem);

            var isLast = index == targets.Count - 1;
            if (isLast || DateTimeOffset.UtcNow - lastReportAt >= CollectionReportInterval)
            {
                lastReportAt = DateTimeOffset.UtcNow;
                progress.SetContext(account.DisplayName, session.DisplayName);
                progress.Start(
                    "read-messages",
                    $"正在比对分析点（{index + 1}/{targets.Count}，{workItems.Count} 个会话有新消息）");
            }
        }

        if (targets.Count > 0)
            progress.Complete("read-messages", $"已比对 {targets.Count} 个会话，{workItems.Count} 个会话有新消息");

        return workItems;
    }

    private async Task<WeChatAnalysisWorkItem?> PrepareSessionAsync(ChatAccount account, ChatSession session, CancellationToken cancellationToken)
    {
        if (!WeChatAnalysisSelection.IsSessionAllowed(session, Settings, Whitelist)) return null;

        var readLimit = Settings.AutomaticMode == WeChatAutomaticAnalysisMode.Unanalyzed
            ? WeChatAnalysisSelection.UnanalyzedMessageReadLimit
            : WeChatAnalysisSelection.DefaultMessageReadLimit;
        var messages = await _weChat.GetLatestMessagesAsync(account.Id, session.Username, readLimit, cancellationToken);
        var latest = messages.LastOrDefault();
        if (latest is null) return null;

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
            return null;
        }

        return new WeChatAnalysisWorkItem(account, session, newMessages, cursorKey, cursor);
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
        lock (_delayGate)
        {
            _delayCts?.Dispose();
            _delayCts = null;
        }
    }
}
