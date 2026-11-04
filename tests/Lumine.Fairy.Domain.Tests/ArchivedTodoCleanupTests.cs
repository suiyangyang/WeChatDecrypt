using Lumine.Fairy.Application;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Domain.Tests;

public sealed class ArchivedTodoCleanupTests
{
    [Fact]
    public void WorkSettingsDefaultToEnabledCleanupWith180DayRetention()
    {
        var settings = new WorkSettings();

        Assert.True(settings.EnableArchivedAutoCleanup);
        Assert.Equal(180, settings.ArchivedRetentionDays);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(180, 180)]
    [InlineData(3650, 3650)]
    [InlineData(99999, 3650)]
    public void NormalizesRetentionDaysToSupportedRange(int value, int expected)
        => Assert.Equal(expected, WorkSettings.NormalizeArchivedRetentionDays(value));

    [Fact]
    public void SelectsOnlyArchivedTasksPastRetention()
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = ArchivedTodoCleanupPolicy.SelectCleanupCandidates(
            [
                CreateTodo("old", isArchived: true, archivedAt: now.AddDays(-181)),
                CreateTodo("fresh", isArchived: true, archivedAt: now.AddDays(-179)),
                CreateTodo("active", isArchived: false, archivedAt: now.AddDays(-400)),
            ],
            ArchivedTodoCleanupPolicy.GetCutoff(now, 180));

        Assert.Single(candidates);
        Assert.Equal("old", candidates[0].Id);
    }

    [Fact]
    public void FallsBackToUpdatedAtWhenArchivedTimeIsMissing()
    {
        var now = DateTimeOffset.UtcNow;
        var todo = CreateTodo("legacy", isArchived: true) with { UpdatedAt = now.AddDays(-200) };

        Assert.True(ArchivedTodoCleanupPolicy.IsCleanupCandidate(todo, ArchivedTodoCleanupPolicy.GetCutoff(now, 180)));
    }

    [Fact]
    public async Task ArchivingRemembersArchiveTimeUntilRestored()
    {
        var path = TempDatabasePath("todos");
        try
        {
            TodoRecord? archived = null;
            TodoRecord? edited = null;
            TodoRecord? restored = null;
            using (var store = new SqliteTodoStore(path))
            {
                await store.AddCandidateAsync(CreateTodo("card", isArchived: false));

                archived = await store.UpdateDetailsAsync((await store.GetAsync("card"))! with { IsArchived = true });
                Assert.NotNull(archived!.ArchivedAt);

                edited = await store.UpdateDetailsAsync(archived with { Title = "重新编辑的标题" });
                restored = await store.UpdateDetailsAsync(edited! with { IsArchived = false });
            }

            Assert.NotNull(archived!.ArchivedAt);
            Assert.Equal(archived.ArchivedAt!.Value.ToUnixTimeSeconds(), edited!.ArchivedAt!.Value.ToUnixTimeSeconds());
            Assert.Null(restored!.ArchivedAt);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task SchedulerCleansExpiredArchivedTasksAndPublishesNotification()
    {
        var todoPath = TempDatabasePath("todos");
        var notificationPath = TempDatabasePath("notifications");
        try
        {
            ArchivedTodoCleanupResult result;
            ArchivedTodoCleanupResult? completed = null;
            IReadOnlyList<NotificationRecord> published;
            using (var todoStore = new SqliteTodoStore(todoPath))
            {
                using var notificationStore = new SqliteNotificationStore(notificationPath);
                var notifications = new NotificationService(notificationStore);
                var todos = new TodoService(todoStore);
                var now = DateTimeOffset.UtcNow;
                await todos.AddCandidateAsync(CreateTodo("old", isArchived: true, archivedAt: now.AddDays(-400)));
                await todos.AddCandidateAsync(CreateTodo("fresh", isArchived: true, archivedAt: now.AddDays(-10)));
                await todos.AddCandidateAsync(CreateTodo("active", isArchived: false, archivedAt: now.AddDays(-400)));

                await using var scheduler = new ArchivedTodoCleanupScheduler(todos, notifications);
                scheduler.Settings = new WorkSettings { ArchivedRetentionDays = 180 };
                scheduler.CleanupCompleted += (_, value) => completed = value;

                result = await scheduler.RunNowAsync();
                published = await notifications.ListAsync();

                Assert.Null(await todos.GetAsync("old"));
                Assert.NotNull(await todos.GetAsync("fresh"));
                Assert.NotNull(await todos.GetAsync("active"));
            }

            Assert.Equal(1, result.DeletedCount);
            Assert.Equal(1, completed?.DeletedCount);
            Assert.Contains(published, item => item.Title.Contains("归档"));
        }
        finally
        {
            TryDelete(todoPath);
            TryDelete(notificationPath);
        }
    }

    [Fact]
    public void WorkSettingsRoundTripThroughSettingsStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumine-fairy-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new AppSettingsStore(path);
            var defaults = store.Load();
            Assert.True(defaults.Work.EnableArchivedAutoCleanup);
            Assert.Equal(180, defaults.Work.ArchivedRetentionDays);

            defaults.Work.EnableArchivedAutoCleanup = false;
            defaults.Work.ArchivedRetentionDays = 30;
            store.Save(defaults);

            var reloaded = new AppSettingsStore(path).Load();
            Assert.False(reloaded.Work.EnableArchivedAutoCleanup);
            Assert.Equal(30, reloaded.Work.ArchivedRetentionDays);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempDatabasePath(string prefix)
        => Path.Combine(Path.GetTempPath(), $"lumine-fairy-{prefix}-{Guid.NewGuid():N}.db");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // SQLite can keep the temp file a little longer; the OS temp folder cleans it up.
        }
    }

    private static TodoRecord CreateTodo(string id, bool isArchived, DateTimeOffset? archivedAt = null)
        => new(
            id,
            $"任务 {id}",
            "用于归档清理测试的任务。",
            "medium",
            null,
            null,
            1,
            Array.Empty<string>(),
            "pending",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            "manual",
            "conversation-1",
            IsArchived: isArchived,
            ArchivedAt: archivedAt);
}