using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumine.Fairy.Domain;

namespace Lumine.Fairy.Infrastructure;

public sealed class ProtectedCredentialStore : IQwenPawCredentialStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ProtectedCredentialStore(string? path = null)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumine.Fairy");
        Directory.CreateDirectory(root);
        _path = path ?? Path.Combine(root, "qwenpaw.credential");
    }

    public async Task SaveAsync(QwenPawCredential credential, CancellationToken cancellationToken = default)
    {
        var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(credential, _json));
        var bytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_path, bytes, cancellationToken);
    }

    public async Task<QwenPawCredential?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(_path, cancellationToken);
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
            var credential = JsonSerializer.Deserialize<QwenPawCredential>(json, _json);
            if (credential is null
                || string.IsNullOrWhiteSpace(credential.ServerUrl)
                || string.IsNullOrWhiteSpace(credential.Username)
                || string.IsNullOrWhiteSpace(credential.Password))
            {
                return null;
            }
            return credential;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }
}
