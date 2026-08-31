using System.Text.Json;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;
using Microsoft.Data.Sqlite;

namespace Lumine.Fairy.Domain.Tests;

public sealed class NotificationStoreTests
{
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
