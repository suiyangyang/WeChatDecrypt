using System.Text.Json;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;
using Microsoft.Data.Sqlite;

namespace Lumine.Fairy.Domain.Tests;

public sealed class NotificationStoreTests
{
    [Fact]
    public async Task CursorPageDoesNotShiftWhenNewNotificationIsInserted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumine-fairy-notifications-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteNotificationStore(path);
            for (var index = 0; index < 5; index++)
                await store.AddAsync(new NotificationRequest($"通知 {index}", $"正文 {index}"));

            var firstPage = await store.ListPageAsync(null, false, false, null, null, 2);
            var cursor = firstPage[^1];
            await store.AddAsync(new NotificationRequest("最新通知", "在翻页期间插入"));

            var secondPage = await store.ListPageAsync(null, false, false, cursor.CreatedAt, cursor.Id, 2);

            Assert.Equal(2, firstPage.Count);
            Assert.Equal(2, secondPage.Count);
            Assert.Empty(firstPage.Select(item => item.Id).Intersect(secondPage.Select(item => item.Id)));
            Assert.DoesNotContain(secondPage, item => item.Title == "最新通知");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task WorkPageOnlyReturnsUnreadOrActionableUnresolvedWorkNotifications()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumine-fairy-notifications-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteNotificationStore(path);
            var unreadWork = await store.AddAsync(new NotificationRequest("未读工作", "正文", Category: "work"));
            var actionableWork = await store.AddAsync(new NotificationRequest(
                "待操作工作",
                "正文",
                Category: "work",
                Actions: [new NotificationAction("confirm", "确认")]));
            var readWork = await store.AddAsync(new NotificationRequest("已读工作", "正文", Category: "work"));
            await store.MarkReadAsync(actionableWork.Id);
            await store.MarkReadAsync(readWork.Id);
            await store.AddAsync(new NotificationRequest("其他通知", "正文", Category: "other"));
            var resolvedWork = await store.AddAsync(new NotificationRequest(
                "已解决工作",
                "正文",
                Category: "work",
                Actions: [new NotificationAction("confirm", "确认")]));
            await store.ResolveAsync(resolvedWork.Id);

            var page = await store.ListPageAsync(null, false, false, null, null, 100, workPageOnly: true);

            Assert.Equal(2, page.Count);
            Assert.Contains(page, item => item.Id == unreadWork.Id);
            Assert.Contains(page, item => item.Id == actionableWork.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task AddAsyncRejectsNullBodyAfterJsonDeserialization()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lumine-fairy-notifications-{Guid.NewGuid():N}.db");
        try
        {
            var request = JsonSerializer.Deserialize<NotificationRequest>(
                "{\"title\":\"标题\",\"body\":null}",
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(request);

            using (var store = new SqliteNotificationStore(path))
            {
                var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => store.AddAsync(request!));

                Assert.Equal(nameof(NotificationRequest.Body), exception.ParamName);
                Assert.Empty(await store.ListAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
