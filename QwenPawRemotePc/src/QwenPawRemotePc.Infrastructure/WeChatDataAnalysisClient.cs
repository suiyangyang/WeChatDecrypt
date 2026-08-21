using System.Diagnostics;
using System.Text.Json;
using QwenPawRemotePc.Domain;
using QwenPawRemotePc.Protocol;

namespace QwenPawRemotePc.Infrastructure;

public sealed class WeChatDataAnalysisClient : IWeChatService, IDisposable
{
    private const string DefaultBaseUrl = "http://127.0.0.1:10492";
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _backendPath;
    private Process? _backend;

    public WeChatDataAnalysisClient(string? backendPath = null)
    {
        _backendPath = backendPath ?? ResolveBackendPath();
    }

    private static string ResolveBackendPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "WeChatDataAnalysisRelease", "WeChatDataAnalysis", "resources", "backend", "wechat-backend.exe"),
            Path.Combine(Environment.CurrentDirectory, "WeChatDataAnalysisRelease", "WeChatDataAnalysis", "resources", "backend", "wechat-backend.exe"),
            @"E:\Work\Code\Tools\WeChatDecrypt\WeChatDataAnalysisRelease\WeChatDataAnalysis\resources\backend\wechat-backend.exe",
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public async Task<IReadOnlyList<ChatAccount>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureBackendAsync(cancellationToken);
        using var document = await GetJsonAsync("/api/wechat-detection", cancellationToken);
        var result = new List<ChatAccount>();
        if (!document.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("accounts", out var accounts)) return result;
        foreach (var item in accounts.EnumerateArray())
        {
            result.Add(new ChatAccount(
                GetString(item, "account_name") ?? string.Empty,
                GetString(item, "account_name") ?? "微信账号",
                GetInt(item, "database_count")));
        }
        return result.Where(x => !string.IsNullOrWhiteSpace(x.Id) && x.DatabaseCount > 1).ToList();
    }

    public async Task<IReadOnlyList<ChatSession>> GetSessionsAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await EnsureBackendAsync(cancellationToken);
        var encoded = Uri.EscapeDataString(accountId);
        using var document = await GetJsonAsync($"/api/chat/sessions?account={encoded}&limit=20&preview=latest&source=realtime", cancellationToken);
        var result = new List<ChatSession>();
        if (!document.RootElement.TryGetProperty("sessions", out var sessions)) return result;
        foreach (var item in sessions.EnumerateArray())
        {
            result.Add(new ChatSession(
                GetString(item, "username") ?? string.Empty,
                GetString(item, "displayName") ?? GetString(item, "display_name") ?? GetString(item, "username") ?? "会话",
                GetString(item, "preview") ?? GetString(item, "lastMessage") ?? string.Empty,
                GetLong(item, "lastMessageTime") ?? GetLong(item, "last_message_time") ?? 0));
        }
        return result.Where(x => !string.IsNullOrWhiteSpace(x.Username)).ToList();
    }

    public async Task<IReadOnlyList<ChatMessage>> GetLatestMessagesAsync(string accountId, string conversationId, int limit, CancellationToken cancellationToken = default)
    {
        await EnsureBackendAsync(cancellationToken);
        var a = Uri.EscapeDataString(accountId);
        var c = Uri.EscapeDataString(conversationId);
        using var document = await GetJsonAsync($"/api/chat/messages?account={a}&username={c}&limit={Math.Clamp(limit, 1, 50)}&order=desc&source=realtime", cancellationToken);
        var result = new List<ChatMessage>();
        if (!document.RootElement.TryGetProperty("messages", out var messages)) return result;
        foreach (var item in messages.EnumerateArray())
        {
            var content = GetString(item, "messageContent") ?? GetString(item, "message_content") ?? GetString(item, "content") ?? string.Empty;
            var senderDisplayName = GetString(item, "senderDisplayName")
                                    ?? GetString(item, "sender_display_name")
                                    ?? GetString(item, "senderName")
                                    ?? GetString(item, "sender_name");
            var senderUsername = GetString(item, "senderUsername")
                                 ?? GetString(item, "sender_username")
                                 ?? GetString(item, "sender")
                                 ?? GetString(item, "username");
            var isSent = item.TryGetProperty("isSent", out var sent) && sent.ValueKind == JsonValueKind.True;
            var sender = BuildSenderLabel(senderDisplayName, senderUsername, isSent);
            var id = GetString(item, "serverId") ?? GetString(item, "server_id") ?? GetString(item, "localId") ?? GetString(item, "local_id") ?? Guid.NewGuid().ToString("N");
            result.Add(new ChatMessage(id, sender, content, GetLong(item, "createTime") ?? GetLong(item, "create_time") ?? 0));
        }
        result.Reverse();
        return result;
    }

    private static string BuildSenderLabel(string? displayName, string? username, bool isSent)
    {
        displayName = displayName?.Trim();
        username = username?.Trim();
        if (isSent && string.IsNullOrWhiteSpace(displayName)) return "我";
        if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(username)
            && !displayName.Equals(username, StringComparison.OrdinalIgnoreCase))
            return $"{displayName} ({username})";
        return !string.IsNullOrWhiteSpace(displayName) ? displayName
            : !string.IsNullOrWhiteSpace(username) ? username
            : isSent ? "我" : "未知发送者";
    }

    private async Task EnsureBackendAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(DefaultBaseUrl + "/api/health", cancellationToken);
            if (response.IsSuccessStatusCode) return;
        }
        catch (HttpRequestException) { }

        if (!File.Exists(_backendPath))
            throw new FileNotFoundException("未找到 WeChatDataAnalysis 发布版后端。", _backendPath);
        if (_backend is { HasExited: false }) return;

        var startInfo = new ProcessStartInfo(_backendPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // The backend resolves its output/key store relative to the workspace.
            // Keep that working directory so it reuses the verified local databases.
            WorkingDirectory = Environment.CurrentDirectory,
        };
        startInfo.Environment["WECHAT_TOOL_PORT"] = "10492";
        _backend = Process.Start(startInfo);
        for (var i = 0; i < 30; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(500, cancellationToken);
            try
            {
                using var response = await _http.GetAsync(DefaultBaseUrl + "/api/health", cancellationToken);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
        }
        throw new TimeoutException("WeChatDataAnalysis 后端启动超时。");
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(DefaultBaseUrl + path, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"微信解析服务请求失败（HTTP {(int)response.StatusCode}）：{DefaultBaseUrl}{path}");
        return JsonDocument.Parse(raw);
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int GetInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static long? GetLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var text)) return text;
        return null;
    }

    public void Dispose()
    {
        _http.Dispose();
        if (_backend is { HasExited: false })
        {
            try { _backend.Kill(entireProcessTree: true); } catch { }
        }
        _backend?.Dispose();
    }
}
