using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Domain.Tests;

public sealed class TodoStoreTests
{
    [Fact]
    public async Task PendingConfirmationIsHiddenUntilConfirmedAndTransitionsAreOneWay()
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
            Assert.Empty(await store.ListAsync());

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
}
