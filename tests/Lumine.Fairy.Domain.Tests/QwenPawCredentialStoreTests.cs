using System.Text;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Infrastructure;

namespace Lumine.Fairy.Domain.Tests;

public sealed class QwenPawCredentialStoreTests
{
    [Fact]
    public async Task SavedCredentialRoundTripsWithoutPlainPasswordOnDisk()
    {
        var path = TempPath();
        try
        {
            var store = new ProtectedCredentialStore(path);
            await store.SaveAsync(new QwenPawCredential("http://192.168.1.100:18803", "suiy", "secret-pwd"));

            var loaded = await store.LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal("http://192.168.1.100:18803", loaded!.ServerUrl);
            Assert.Equal("suiy", loaded.Username);
            Assert.Equal("secret-pwd", loaded.Password);
            Assert.DoesNotContain("secret-pwd", Encoding.Latin1.GetString(await File.ReadAllBytesAsync(path)));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task LoadReturnsNullWhenCredentialIsMissingOrCorrupted()
    {
        var missing = TempPath();
        var corrupted = TempPath();
        try
        {
            Assert.Null(await new ProtectedCredentialStore(missing).LoadAsync());

            await File.WriteAllBytesAsync(corrupted, [1, 2, 3, 4, 5]);
            Assert.Null(await new ProtectedCredentialStore(corrupted).LoadAsync());
        }
        finally
        {
            Delete(missing);
            Delete(corrupted);
        }
    }

    [Fact]
    public async Task LoadReturnsNullWhenCredentialIsIncomplete()
    {
        var path = TempPath();
        try
        {
            await new ProtectedCredentialStore(path).SaveAsync(new QwenPawCredential("http://127.0.0.1:8088", "suiy", string.Empty));

            Assert.Null(await new ProtectedCredentialStore(path).LoadAsync());
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task ClearRemovesSavedCredential()
    {
        var path = TempPath();
        try
        {
            var store = new ProtectedCredentialStore(path);
            await store.SaveAsync(new QwenPawCredential("http://127.0.0.1:8088", "suiy", "secret-pwd"));

            await store.ClearAsync();

            Assert.False(File.Exists(path));
            Assert.Null(await store.LoadAsync());
        }
        finally
        {
            Delete(path);
        }
    }

    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"lumine-fairy-credential-{Guid.NewGuid():N}");

    private static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
