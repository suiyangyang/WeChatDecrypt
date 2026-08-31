using Lumine.Fairy.Domain;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;
using Microsoft.Data.Sqlite;

namespace Lumine.Fairy.Domain.Tests;

public sealed class TodoStoreTests
{
    [Fact]
    public async Task EnsureCandidateRefreshesMissingSummaryForPendingWechatCandidate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qwenpaw-todos-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteTodoStore(path);
            var existing = new TodoRecord(
                "wechat:test:todo-1",
                "微信工作事项",
                string.Empty,
                "medium",
                null,
                null,
                0.8,
                ["message-1"],
                "pending_confirmation",
                DateTimeOffset.UtcNow,
                null,
                null,
                "wechat-analysis",
                "conversation-1");
            await store.AddCandidateAsync(existing);

            var service = new TodoService(store);
            var refreshed = await service.EnsureCandidateAsync(existing with
            {
                Title = "确认交付时间",
                Summary = "确认当前排期并回复客户可交付日期。",
            });

            Assert.NotNull(refreshed);
            Assert.Equal("确认交付时间", refreshed!.Title);
            Assert.Equal("确认当前排期并回复客户可交付日期。", refreshed.Summary);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task PendingConfirmationIsStoredInConfirmationListUntilConfirmed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qwenpaw-todos-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new SqliteTodoStore(path))
            {
            var candidate = new TodoRecord(
                "wechat:test:todo-1",
                "整理会议纪要",
                "整理并发送本周会议纪要",
                "high",
                null,
                "我",
                0.95,
                ["message-1"],
                "pending_confirmation",
                DateTimeOffset.UtcNow,
                null,
                null,
                "wechat-analysis",
                "conversation-1");

            Assert.NotNull(await store.AddCandidateAsync(candidate));
            var pendingConfirmation = Assert.Single(await store.ListAsync());
            Assert.Equal("pending_confirmation", pendingConfirmation.Status);
            Assert.Equal("pending-confirmation", pendingConfirmation.ListId);

            var confirmed = await store.UpdateStatusAsync(candidate.Id, "pending");

            Assert.NotNull(confirmed);
            Assert.Equal("pending", confirmed!.Status);
            Assert.Single(await store.ListAsync());
                Assert.Null(await store.UpdateStatusAsync(candidate.Id, "rejected"));
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task UpdateDetailsChangesEditableFieldsAndKeepsTodoIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qwenpaw-todos-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteTodoStore(path);
            var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var candidate = new TodoRecord(
                "todo-edit-1",
                "原始标题",
                "原始摘要",
                "medium",
                null,
                null,
                0.8,
                ["message-1"],
                "pending",
                createdAt,
                createdAt,
                null,
                "wechat-analysis",
                "conversation-1");

            await store.AddCandidateAsync(candidate);
            var updated = await store.UpdateDetailsAsync(candidate with
            {
                Title = "修改后的标题",
                Summary = "修改后的摘要",
                Priority = "high",
                CompanyId = "company-1",
                CompanyName = "示例公司",
                DueAt = DateTimeOffset.UtcNow.AddDays(1),
                Assignee = "张三",
            });

            Assert.NotNull(updated);
            Assert.Equal("todo-edit-1", updated!.Id);
            Assert.Equal("修改后的标题", updated.Title);
            Assert.Equal("修改后的摘要", updated.Summary);
            Assert.Equal("high", updated.Priority);
            Assert.Equal("company-1", updated.CompanyId);
            Assert.Equal("示例公司", updated.CompanyName);
            Assert.Equal("张三", updated.Assignee);
            Assert.Equal("pending", updated.Status);
            Assert.Equal(createdAt, updated.CreatedAt);
            Assert.Equal("wechat-analysis", updated.Source);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task BoardSeedsListsAndMoveReordersCardsAcrossLists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qwenpaw-todos-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteTodoStore(path);
            var lists = await store.ListListsAsync("default");

            Assert.Equal(["pending-confirmation", "todo", "in-progress", "done"], lists.Select(list => list.Id).ToArray());
            Assert.Equal("待确认", lists[0].Name);
            Assert.Equal("pending_confirmation", lists[0].Status);
            Assert.False(await store.ArchiveListAsync("pending-confirmation", true));
            Assert.Null(await store.UpdateListAsync(lists[0] with { Name = "不应修改" }));
            Assert.False(await store.DeleteListAsync("pending-confirmation"));
            Assert.False(await store.ArchiveListAsync("todo", true));
            Assert.Equal("待处理", (await store.UpdateListAsync(lists[1] with { Name = "待处理" }))!.Name);

            var first = CreateTodo("todo-move-1", 0);
            var second = CreateTodo("todo-move-2", 1);
            await store.AddCandidateAsync(first);
            await store.AddCandidateAsync(second);

            var moved = await store.MoveAsync(first.Id, "default", "in-progress", 0, "in_progress");

            Assert.NotNull(moved);
            Assert.Equal("in-progress", moved!.ListId);
            Assert.Equal("in_progress", moved.Status);
            Assert.Equal(0, moved.Position);
            Assert.DoesNotContain(await store.ListAsync(), todo => todo.Id == first.Id && todo.ListId == "todo");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        static TodoRecord CreateTodo(string id, int position)
            => new(
                id,
                id,
                "摘要",
                "medium",
                null,
                null,
                1,
                [],
                "pending",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                "manual",
                "",
                BoardId: "default",
                ListId: "todo",
                Position: position);
    }

    [Fact]
    public async Task ArchivedTodoIsHiddenByDefaultAndCanBeListedExplicitly()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qwenpaw-todos-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteTodoStore(path);
            var todo = new TodoRecord(
                "todo-archive-1",
                "归档卡片",
                "摘要",
                "low",
                null,
                null,
                1,
                [],
                "pending",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                "manual",
                "");
            await store.AddCandidateAsync(todo);

            Assert.NotNull(await store.UpdateDetailsAsync(todo with { IsArchived = true }));
            Assert.DoesNotContain(await store.ListAsync(), item => item.Id == todo.Id);
            Assert.Contains(await store.ListAsync(includeArchived: true), item => item.Id == todo.Id && item.IsArchived);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ListsCanBeReorderedAndDeletedWithTheirCards()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qwenpaw-todos-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteTodoStore(path);
            await store.AddListAsync(new TodoListRecord("custom-a", "default", "自定义 A", 3));
            await store.AddListAsync(new TodoListRecord("custom-b", "default", "自定义 B", 4));
            var card = new TodoRecord(
                "todo-delete-with-list", "列表卡片", "摘要", "low", null, null, 1, [], "pending",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "manual", "", ListId: "custom-a");
            await store.AddCandidateAsync(card);

            Assert.True(await store.DeleteListAsync("custom-a"));
            var remaining = (await store.ListListsAsync("default", includeArchived: true))
                .Single(list => list.Id == "custom-b");
            Assert.Equal(4, remaining.Position);
            var added = await store.AddListAsync(new TodoListRecord("custom-c", "default", "自定义 C", 99));
            Assert.NotNull(added);
            Assert.Equal(5, added!.Position);
            Assert.Null(await store.GetAsync(card.Id));

            var moved = await store.MoveListAsync("custom-b", "default", 0);

            Assert.NotNull(moved);
            Assert.Equal(0, moved!.Position);
            Assert.DoesNotContain(await store.ListListsAsync("default", includeArchived: true), list => list.Id == "custom-a");
            Assert.False(await store.DeleteAsync(card.Id));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ReopeningDatabaseKeepsSystemListPositionsStable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qwenpaw-todos-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new SqliteTodoStore(path))
                Assert.Equal(new[] { 0, 1, 2, 3 }, (await store.ListListsAsync("default")).Select(list => list.Position).ToArray());

            using var reopenedStore = new SqliteTodoStore(path);
            var reopenedLists = await reopenedStore.ListListsAsync("default");

            Assert.Equal(["pending-confirmation", "todo", "in-progress", "done"], reopenedLists.Select(list => list.Id).ToArray());
            Assert.Equal(new[] { 0, 1, 2, 3 }, reopenedLists.Select(list => list.Position).ToArray());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LegacyDatabaseIsMigratedBeforeLayoutIndexIsCreated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qwenpaw-todos-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadWriteCreate;Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE todos (
                        id TEXT PRIMARY KEY,
                        title TEXT NOT NULL,
                        summary TEXT NOT NULL,
                        priority TEXT NOT NULL,
                        due_at TEXT NULL,
                        assignee TEXT NULL,
                        confidence REAL NOT NULL,
                        source_refs_json TEXT NOT NULL,
                        status TEXT NOT NULL,
                        created_at TEXT NOT NULL,
                        confirmed_at TEXT NULL,
                        completed_at TEXT NULL,
                        source TEXT NOT NULL,
                        conversation_id TEXT NOT NULL,
                        company_id TEXT NULL,
                        company_name TEXT NULL,
                        conversation_display_name TEXT NULL,
                        is_group_conversation INTEGER NOT NULL DEFAULT 0
                    );
                    INSERT INTO todos
                        (id,title,summary,priority,confidence,source_refs_json,status,created_at,source,conversation_id)
                    VALUES ('legacy-1','旧卡片','旧摘要','medium',1,'[]','pending','2026-08-29T00:00:00+00:00','manual','');
                    """;
                command.ExecuteNonQuery();
            }

            using var store = new SqliteTodoStore(path);
            var migrated = await store.GetAsync("legacy-1");

            Assert.NotNull(migrated);
            Assert.Equal("default", migrated!.BoardId);
            Assert.Equal("todo", migrated.ListId);
            Assert.False(migrated.IsArchived);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
