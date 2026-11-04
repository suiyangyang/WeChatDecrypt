using Lumine.Fairy.Application;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;
using Microsoft.Data.Sqlite;

namespace Lumine.Fairy.Domain.Tests;

public sealed class WeChatAnalysisNotificationActionTests
{
    [Fact]
    public async Task ConfirmReportsAlreadyHandledWhenCandidateWasAlreadyConfirmedInWorkList()
    {
        var notificationPath = CreateDatabasePath("notifications");
        var todoPath = CreateDatabasePath("todos");
        try
        {
            using var notificationStore = new SqliteNotificationStore(notificationPath);
            using var todoStore = new SqliteTodoStore(todoPath);
            var notifications = new NotificationService(notificationStore);
            var todos = new TodoService(todoStore);
            var coordinator = new WeChatAnalysisCoordinator(null!, null!, notifications, todos);
            var candidate = (await todos.AddCandidateAsync(CreateCandidate("todo-confirmed-in-work-list")))!;
            var notification = await PublishCandidateNotificationAsync(notifications, candidate.Id);

            await todos.ConfirmAsync(candidate.Id);
            var result = await coordinator.HandleNotificationActionAsync(notification, "confirm-todo", "default");
            var reloaded = await notifications.GetAsync(notification.Id);

            Assert.True(result.AlreadyHandled);
            Assert.Contains("已在工作列表处理", result.Message);
            Assert.NotNull(reloaded);
            Assert.True(reloaded!.IsResolved);
        }
        finally
        {
            ReleaseDatabases(notificationPath, todoPath);
        }
    }

    [Fact]
    public async Task RejectReportsAlreadyHandledWhenCandidateWasDeleted()
    {
        var notificationPath = CreateDatabasePath("notifications");
        var todoPath = CreateDatabasePath("todos");
        try
        {
            using var notificationStore = new SqliteNotificationStore(notificationPath);
            using var todoStore = new SqliteTodoStore(todoPath);
            var notifications = new NotificationService(notificationStore);
            var todos = new TodoService(todoStore);
            var coordinator = new WeChatAnalysisCoordinator(null!, null!, notifications, todos);
            var candidate = (await todos.AddCandidateAsync(CreateCandidate("todo-deleted")))!;
            var notification = await PublishCandidateNotificationAsync(notifications, candidate.Id);

            await todos.DeleteAsync(candidate.Id);
            var result = await coordinator.HandleNotificationActionAsync(notification, "reject-todo", "default");
            var reloaded = await notifications.GetAsync(notification.Id);

            Assert.True(result.AlreadyHandled);
            Assert.Contains("已不存在", result.Message);
            Assert.NotNull(reloaded);
            Assert.True(reloaded!.IsResolved);
        }
        finally
        {
            ReleaseDatabases(notificationPath, todoPath);
        }
    }

    [Fact]
    public async Task ActionOnResolvedNotificationReportsAlreadyHandled()
    {
        var notificationPath = CreateDatabasePath("notifications");
        var todoPath = CreateDatabasePath("todos");
        try
        {
            using var notificationStore = new SqliteNotificationStore(notificationPath);
            using var todoStore = new SqliteTodoStore(todoPath);
            var notifications = new NotificationService(notificationStore);
            var todos = new TodoService(todoStore);
            var coordinator = new WeChatAnalysisCoordinator(null!, null!, notifications, todos);
            var candidate = (await todos.AddCandidateAsync(CreateCandidate("todo-resolved")))!;
            var published = await PublishCandidateNotificationAsync(notifications, candidate.Id);
            await notifications.ResolveAsync(published.Id);
            var resolved = (await notifications.GetAsync(published.Id))!;

            var result = await coordinator.HandleNotificationActionAsync(resolved, "confirm-todo", "default");

            Assert.True(result.AlreadyHandled);
            Assert.Contains("已经处理过", result.Message);
            var untouched = await todos.GetAsync(candidate.Id);
            Assert.Equal("pending_confirmation", untouched!.Status);
        }
        finally
        {
            ReleaseDatabases(notificationPath, todoPath);
        }
    }

    [Fact]
    public async Task ConfirmAnnotatesNotificationAndDoesNotPublishFollowUpNotification()
    {
        var notificationPath = CreateDatabasePath("notifications");
        var todoPath = CreateDatabasePath("todos");
        try
        {
            using var notificationStore = new SqliteNotificationStore(notificationPath);
            using var todoStore = new SqliteTodoStore(todoPath);
            var notifications = new NotificationService(notificationStore);
            var todos = new TodoService(todoStore);
            var coordinator = new WeChatAnalysisCoordinator(null!, null!, notifications, todos);
            var candidate = (await todos.AddCandidateAsync(CreateCandidate("todo-confirm-label")))!;
            var notification = await PublishCandidateNotificationAsync(notifications, candidate.Id);

            var result = await coordinator.HandleNotificationActionAsync(notification, "confirm-todo", "default");
            var reloaded = (await notifications.GetAsync(notification.Id))!;

            Assert.False(result.AlreadyHandled);
            Assert.StartsWith("已加入 TODO", result.Message);
            Assert.True(reloaded.IsResolved);
            Assert.Equal("已加入TODO", reloaded.ResolvedLabel);
            var untouched = await todos.GetAsync(candidate.Id);
            Assert.Equal("pending", untouched!.Status);
            Assert.Single(await notifications.ListAsync());
        }
        finally
        {
            ReleaseDatabases(notificationPath, todoPath);
        }
    }

    [Fact]
    public async Task RejectAnnotatesNotificationAsHandledAndKeepsCandidateOutOfTodo()
    {
        var notificationPath = CreateDatabasePath("notifications");
        var todoPath = CreateDatabasePath("todos");
        try
        {
            using var notificationStore = new SqliteNotificationStore(notificationPath);
            using var todoStore = new SqliteTodoStore(todoPath);
            var notifications = new NotificationService(notificationStore);
            var todos = new TodoService(todoStore);
            var coordinator = new WeChatAnalysisCoordinator(null!, null!, notifications, todos);
            var candidate = (await todos.AddCandidateAsync(CreateCandidate("todo-reject-label")))!;
            var notification = await PublishCandidateNotificationAsync(notifications, candidate.Id);

            var result = await coordinator.HandleNotificationActionAsync(notification, "reject-todo", "default");
            var reloaded = (await notifications.GetAsync(notification.Id))!;

            Assert.False(result.AlreadyHandled);
            Assert.StartsWith("已处理", result.Message);
            Assert.True(reloaded.IsResolved);
            Assert.Equal("已处理", reloaded.ResolvedLabel);
            var untouched = await todos.GetAsync(candidate.Id);
            Assert.Equal("rejected", untouched!.Status);
            Assert.Single(await notifications.ListAsync());
        }
        finally
        {
            ReleaseDatabases(notificationPath, todoPath);
        }
    }

    private static TodoRecord CreateCandidate(string id)
        => new(
            id,
            "下个版本同步仪莱配置文件修复",
            "请查看关联微信消息，补充具体工作内容。",
            "medium",
            null,
            null,
            0.8,
            ["wechat-message-1"],
            "pending_confirmation",
            DateTimeOffset.UtcNow,
            null,
            null,
            "wechat-analysis",
            "conversation-1");

    private static Task<NotificationRecord> PublishCandidateNotificationAsync(NotificationService notifications, string todoId)
        => notifications.PublishAsync(new NotificationRequest(
            "发现工作内容：下个版本同步仪莱配置文件修复",
            "请在通知中确认或忽略该工作候选。",
            "wechat-analysis",
            "work",
            "info",
            "close_or_confirm",
            86400,
            $"analysis-candidate:{todoId}",
            "action",
            [
                new NotificationAction("confirm-todo", "确认加入 TODO", "confirm-todo"),
                new NotificationAction("reject-todo", "忽略", "reject-todo"),
            ],
            null,
            new Dictionary<string, object?> { ["todoId"] = todoId }));

    private static string CreateDatabasePath(string name)
        => Path.Combine(Path.GetTempPath(), $"lumine-fairy-notification-action-{name}-{Guid.NewGuid():N}.db");

    private static void ReleaseDatabases(params string[] paths)
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in paths)
            if (File.Exists(path)) File.Delete(path);
    }
}
