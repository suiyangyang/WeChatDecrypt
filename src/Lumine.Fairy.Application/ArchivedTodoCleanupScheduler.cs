using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Application;

/// <summary>
/// Periodically deletes tasks that stayed archived longer than the configured retention days.
/// </summary>
public sealed class ArchivedTodoCleanupScheduler : IAsyncDisposable
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly TodoService _todos;
    private readonly NotificationService _notifications;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _delayCts;
    private Task? _worker;
    private WorkSettings _settings = new();

    public ArchivedTodoCleanupScheduler(TodoService todos, NotificationService notifications)
    {
        _todos = todos;
        _notifications = notifications;
    }

    public bool IsRunning => _worker is { IsCompleted: false };

    public WorkSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value ?? new WorkSettings();
            _delayCts?.Cancel();
        }
    }

    public event EventHandler<ArchivedTodoCleanupResult>? CleanupCompleted;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
    }

    public async Task<ArchivedTodoCleanupResult> RunNowAsync(CancellationToken cancellationToken = default)
    {
        var result = await _todos.CleanupArchivedAsync(_settings.ArchivedRetentionDays, cancellationToken: cancellationToken);
        if (result.DeletedCount == 0) return result;

        CleanupCompleted?.Invoke(this, result);
        await _notifications.PublishAsync(new NotificationRequest(
            "已自动清理归档任务",
            $"归档超过 {result.RetentionDays} 天的 {result.DeletedCount} 个任务已删除。",
            "todo-cleanup",
            "other",
            "info",
            "timeout",
            6,
            $"todo-cleanup:{DateTimeOffset.UtcNow:yyyyMMddHH}"), cancellationToken);
        return result;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var isStartupRound = true;
        while (true)
        {
            var delayCts = _delayCts = new CancellationTokenSource();
            using var delayToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, delayCts.Token);
            try
            {
                await Task.Delay(isStartupRound ? StartupDelay : CheckInterval, delayToken.Token);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) return;
                continue;
            }

            isStartupRound = false;
            if (!_settings.EnableArchivedAutoCleanup) continue;

            try
            {
                await RunNowAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                try
                {
                    await _notifications.PublishAsync(new NotificationRequest(
                        "归档任务自动清理失败",
                        ex.Message,
                        "todo-cleanup",
                        "other",
                        "warning",
                        "timeout",
                        8,
                        $"todo-cleanup-error:{DateTimeOffset.UtcNow:yyyyMMddHH}"), CancellationToken.None);
                }
                catch (Exception)
                {
                    // Cleanup keeps running; the next round retries.
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _delayCts?.Cancel();
        if (_worker is not null)
        {
            try { await _worker; } catch (OperationCanceledException) { }
        }
        _cts?.Dispose();
        _delayCts?.Dispose();
    }
}