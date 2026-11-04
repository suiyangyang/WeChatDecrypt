using Lumine.Fairy.Application;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;
using Microsoft.Data.Sqlite;

namespace Lumine.Fairy.Domain.Tests;

/// <summary>
/// 覆盖“分析最近会话”的可恢复性：无新消息要正常收尾，占用中要排队补跑。
/// </summary>
public sealed class IncrementalAnalysisSchedulerTests
{
    [Fact]
    public async Task AnalysisCreatesSessionForSelectedAgent()
    {
        var paths = CreatePaths();
        try
        {
            using var notificationStore = new SqliteNotificationStore(paths.notification);
            using var todoStore = new SqliteTodoStore(paths.todo);
            var qwenPaw = new FakeQwenPawClient(null);
            var coordinator = new WeChatAnalysisCoordinator(
                new FakeWeChatService([], [], new Dictionary<string, IReadOnlyList<ChatMessage>>()),
                qwenPaw,
                new NotificationService(notificationStore),
                new TodoService(todoStore));

            await coordinator.AnalyzeMessagesAsync(
                new ChatAccount("account-1", "账号一", 1),
                new ChatSession("wxid_customer", "客户甲", string.Empty, DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                [new ChatMessage("message-1", "wxid_customer", "客户提出新问题", DateTimeOffset.UtcNow.ToUnixTimeSeconds())],
                "WeChatAnalysist");

            Assert.Equal(["WeChatAnalysist"], qwenPaw.CreatedSessionAgentIds);
        }
        finally
        {
            Release(paths);
        }
    }

    [Fact]
    public async Task AnalyzeNowFinishesProgressWhenNoSessionHasNewMessages()
    {
        var paths = CreatePaths();
        var weChat = new FakeWeChatService(
            [new ChatAccount("account-1", "账号一", 1)],
            [new ChatSession("wxid_old", "客户甲", string.Empty, 0)],
            new Dictionary<string, IReadOnlyList<ChatMessage>>
            {
                ["wxid_old"] =
                [
                    new ChatMessage("m-old", "wxid_old", "一年前的消息", DateTimeOffset.UtcNow.AddYears(-1).ToUnixTimeSeconds()),
                ],
            });
        try
        {
            using var notificationStore = new SqliteNotificationStore(paths.notification);
            using var todoStore = new SqliteTodoStore(paths.todo);
            var qwenPaw = new FakeQwenPawClient(null);
            var coordinator = new WeChatAnalysisCoordinator(weChat, qwenPaw, new NotificationService(notificationStore), new TodoService(todoStore));
            await using var scheduler = new IncrementalAnalysisScheduler(
                weChat, qwenPaw, coordinator, new NotificationService(notificationStore), paths.cursor);

            AnalysisProgressSnapshot? lastSnapshot = null;
            coordinator.ProgressChanged += (_, snapshot) => lastSnapshot = snapshot;

            await Assert.ThrowsAsync<NoPendingWeChatMessagesException>(async () => await scheduler.AnalyzeNowAsync());

            Assert.NotNull(lastSnapshot);
            Assert.True(lastSnapshot!.IsFinished);
            Assert.DoesNotContain(lastSnapshot.Stages, stage => stage.Status == "error");
            Assert.DoesNotContain(lastSnapshot.Stages, stage => stage.Status is "running" or "pending");
            Assert.Equal("completed", lastSnapshot.Stages.Single(stage => stage.Id == "complete").Status);
            Assert.Contains("skipped", lastSnapshot.Stages.Select(stage => stage.Status));
            Assert.Equal("已比对 1 个会话，0 个会话有新消息", lastSnapshot.Stages.Single(stage => stage.Id == "read-messages").Detail);
            Assert.Equal(0, qwenPaw.ChatCallCount);
        }
        finally
        {
            Release(paths);
        }
    }

    [Fact]
    public async Task AnalyzeLatestFinishesProgressWhenNoSessionHasNewMessages()
    {
        var paths = CreatePaths();
        var weChat = new FakeWeChatService(
            [new ChatAccount("account-1", "账号一", 1)],
            [new ChatSession("wxid_old", "客户甲", string.Empty, 0)],
            new Dictionary<string, IReadOnlyList<ChatMessage>>
            {
                ["wxid_old"] =
                [
                    new ChatMessage("m-old", "wxid_old", "一年前的消息", DateTimeOffset.UtcNow.AddYears(-1).ToUnixTimeSeconds()),
                ],
            });
        try
        {
            using var notificationStore = new SqliteNotificationStore(paths.notification);
            using var todoStore = new SqliteTodoStore(paths.todo);
            var qwenPaw = new FakeQwenPawClient(null);
            var coordinator = new WeChatAnalysisCoordinator(weChat, qwenPaw, new NotificationService(notificationStore), new TodoService(todoStore));

            AnalysisProgressSnapshot? lastSnapshot = null;
            coordinator.ProgressChanged += (_, snapshot) => lastSnapshot = snapshot;

            await Assert.ThrowsAsync<NoPendingWeChatMessagesException>(async () => await coordinator.AnalyzeLatestAsync("default"));

            Assert.NotNull(lastSnapshot);
            Assert.True(lastSnapshot!.IsFinished);
            Assert.Equal("completed", lastSnapshot.Stages.Single(stage => stage.Id == "complete").Status);
            Assert.DoesNotContain(lastSnapshot.Stages, stage => stage.Status is "error" or "running");
            Assert.Equal(0, qwenPaw.ChatCallCount);
        }
        finally
        {
            Release(paths);
        }
    }

    [Fact]
    public async Task AnalyzeNowDuringRunningRoundQueuesAnImmediateFollowUpRound()
    {
        var paths = CreatePaths();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var chatGate = new TaskCompletionSource<QwenPawChatResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var weChat = new FakeWeChatService(
            [new ChatAccount("account-1", "账号一", 1)],
            [new ChatSession("wxid_a", "客户甲", string.Empty, now)],
            new Dictionary<string, IReadOnlyList<ChatMessage>>
            {
                ["wxid_a"] = [new ChatMessage("m-1", "wxid_a", "客户提出新问题", now)],
            });
        try
        {
            using var notificationStore = new SqliteNotificationStore(paths.notification);
            using var todoStore = new SqliteTodoStore(paths.todo);
            var notifications = new NotificationService(notificationStore);
            var qwenPaw = new FakeQwenPawClient(chatGate);
            var coordinator = new WeChatAnalysisCoordinator(weChat, qwenPaw, notifications, new TodoService(todoStore));
            var scheduler = new IncrementalAnalysisScheduler(weChat, qwenPaw, coordinator, notifications, paths.cursor);

            scheduler.Start(TimeSpan.FromMinutes(30));
            Assert.True(scheduler.RequestImmediateRun());
            await WaitUntilAsync(() => scheduler.IsRoundRunning);

            await Assert.ThrowsAsync<WeChatAnalysisRoundInProgressException>(async () => await scheduler.AnalyzeNowAsync());
            Assert.True(scheduler.IsRunQueued);

            chatGate.SetResult(new QwenPawChatResult(
                "{}",
                "session-1",
                new AnalysisResult("no_action", [], [], [], [])));

            // 间隔是 30 分钟，能很快进入第二轮说明请求确实被排队并立即补跑。
            await WaitUntilAsync(() => weChat.SessionReadCount >= 2);
            Assert.Equal(1, qwenPaw.ChatCallCount);
            await scheduler.DisposeAsync();
        }
        finally
        {
            chatGate.TrySetCanceled();
            Release(paths);
        }
    }

    private static (string notification, string todo, string cursor) CreatePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lumine-fairy-scheduler-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return (Path.Combine(root, "notifications.db"), Path.Combine(root, "todos.db"), Path.Combine(root, "cursors.json"));
    }

    private static void Release((string notification, string todo, string cursor) paths)
    {
        SqliteConnection.ClearAllPools();
        var root = Path.GetDirectoryName(paths.notification)!;
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 10000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }

        throw new TimeoutException("等待条件超时。");
    }

    private sealed class FakeWeChatService(
        IReadOnlyList<ChatAccount> accounts,
        IReadOnlyList<ChatSession> sessions,
        IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>> messagesByConversation) : IWeChatService
    {
        private int _sessionReadCount;

        public int SessionReadCount => Volatile.Read(ref _sessionReadCount);

        public Task<IReadOnlyList<ChatAccount>> GetAccountsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(accounts);

        public Task<IReadOnlyList<ChatSession>> GetSessionsAsync(string accountId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _sessionReadCount);
            return Task.FromResult(sessions);
        }

        public Task<IReadOnlyList<ChatContact>> GetChatContactsAsync(
            string accountId,
            bool includeGroups = true,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChatContact>>([]);

        public Task<IReadOnlyList<ChatMessage>> GetLatestMessagesAsync(
            string accountId,
            string conversationId,
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult(messagesByConversation.TryGetValue(conversationId, out var messages) ? messages : []);
    }

    private sealed class FakeQwenPawClient(TaskCompletionSource<QwenPawChatResult>? chatGate) : IQwenPawClient
    {
        private readonly List<string> _createdSessionAgentIds = [];
        private int _chatCallCount;

        public bool IsAuthenticated => true;
        public string? Username => "tester";
        public IReadOnlyList<string> CreatedSessionAgentIds => _createdSessionAgentIds;
        public int ChatCallCount => Volatile.Read(ref _chatCallCount);

        public Task<QwenPawLoginResult> LoginAsync(string baseUrl, string username, string password, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<QwenPawAgent>> GetAgentsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QwenPawAgent>>([new QwenPawAgent("default", "默认 Agent", string.Empty, true)]);

        public Task CreateSessionAsync(string agentId, string sessionId, string title, CancellationToken cancellationToken = default)
        {
            _createdSessionAgentIds.Add(agentId);
            return Task.CompletedTask;
        }

        public Task<QwenPawChatResult> ChatAsync(string agentId, string input, CancellationToken cancellationToken = default)
            => ChatAsync(agentId, input, null, cancellationToken);

        public Task<QwenPawChatResult> ChatAsync(string agentId, string input, string? sessionId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _chatCallCount);
            return chatGate is null
                ? Task.FromResult(new QwenPawChatResult("{}", sessionId, new AnalysisResult("no_action", [], [], [], [])))
                : chatGate.Task.WaitAsync(cancellationToken);
        }

        public Task RenameSessionAsync(string sessionId, string title, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
