using System.Security.Cryptography;
using System.Text;
using QwenPawRemotePc.Domain;

namespace QwenPawRemotePc.Infrastructure;

public sealed class ProtectedTokenStore : IProtectedTokenStore
{
    private readonly string _path;

    public ProtectedTokenStore()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QwenPawRemotePc");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "qwenpaw.token");
    }

    public async Task SaveAsync(string token, CancellationToken cancellationToken = default)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_path, bytes, cancellationToken);
    }

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(_path, cancellationToken);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }
}
