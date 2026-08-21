using System.Diagnostics;
using System.Text.Json;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Infrastructure;

public sealed class WeChatDataAnalysisClient : IWeChatService, IDisposable
{
    private const string DefaultBaseUrl = "http://127.0.0.1:10492";
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _backendPath;
    private readonly string _dataRoot;
    private readonly Action<string>? _trace;
    private readonly SemaphoreSlim _backendGate = new(1, 1);
    private Process? _backend;

    public WeChatDataAnalysisClient(string? backendPath = null, TimeSpan? requestTimeout = null, Action<string>? trace = null)
    {
        _backendPath = backendPath ?? ResolveBackendPath();
        _dataRoot = ResolveDataRoot();
        _trace = trace;
        _http.Timeout = requestTimeout ?? TimeSpan.FromSeconds(60);
    }

    private static string ResolveDataRoot()
    {
        var configured = Environment.GetEnvironmentVariable("WECHAT_TOOL_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);

        var startPaths = new[] { Environment.CurrentDirectory, AppContext.BaseDirectory };
        foreach (var startPath in startPaths)
        {
            var directory = new DirectoryInfo(startPath);
            for (var i = 0; directory is not null && i < 10; i++, directory = directory.Parent)
            {
                var output = Path.Combine(directory.FullName, "output");
                if (File.Exists(Path.Combine(output, "account_keys.json"))
                    || Directory.Exists(Path.Combine(output, "databases")))
                    return directory.FullName;
            }
        }

        return Environment.CurrentDirectory;
    }

    private static string ResolveBackendPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "WeChatDataAnalysisRelease", "WeChatDataAnalysis", "resources", "backend", "wechat-backend.exe"),
            Path.Combine(Environment.CurrentDirectory, "WeChatDataAnalysisRelease", "WeChatDataAnalysis", "resources", "backend", "wechat-backend.exe"),
            @"E:\Work\Code\Tools\Lumine.Fairy\WeChatDataAnalysisRelease\WeChatDataAnalysis\resources\backend\wechat-backend.exe",
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public async Task<IReadOnlyList<ChatAccount>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureBackendAsync(cancellationToken);
        using var document = await GetJsonAsync("/api/chat/accounts", cancellationToken);
        var result = new List<ChatAccount>();
        if (!document.RootElement.TryGetProperty("accounts", out var accounts) || accounts.ValueKind != JsonValueKind.Array)
            return result;

        var accountInfos = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.TryGetProperty("accountInfos", out var infos) && infos.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in infos.EnumerateArray())
            {
                var id = GetString(item, "account") ?? GetString(item, "username");
                if (!string.IsNullOrWhiteSpace(id)) accountInfos[id] = item;
            }
        }

        foreach (var item in accounts.EnumerateArray())
        {
            var accountId = item.ValueKind == JsonValueKind.String ? item.GetString() : GetString(item, "account");
            if (string.IsNullOrWhiteSpace(accountId)) continue;
            accountInfos.TryGetValue(accountId, out var info);
            if (info.ValueKind != JsonValueKind.Undefined
                && info.TryGetProperty("realtimeAvailable", out var realtime)
                && realtime.ValueKind == JsonValueKind.False)
                continue;

            result.Add(new ChatAccount(
                accountId,
                info.ValueKind == JsonValueKind.Undefined
                    ? accountId
                    : GetString(info, "name") ?? GetString(info, "displayName") ?? accountId,
                info.ValueKind == JsonValueKind.Undefined ? 0 : GetInt(info, "database_count")));
        }
        return result.Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
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
                GetString(item, "displayName") ?? GetString(item, "display_name") ?? GetString(item, "name") ?? GetString(item, "username") ?? "会话",
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
        _trace?.Invoke("EnsureBackend 开始");
        await _backendGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                if (await IsBackendReadyAsync(cancellationToken))
                {
                    _trace?.Invoke("EnsureBackend 完成：后端已就绪");
                    return;
                }

                if (await IsBackendHealthyAsync(cancellationToken))
                {
                    var logPath = await GetBackendLogPathAsync(cancellationToken);
                    var location = string.IsNullOrWhiteSpace(logPath) ? "未知目录" : logPath;
                    throw new InvalidOperationException(
                        $"检测到已有微信解析后端占用 10492，但它没有加载当前微信数据目录。后端日志位置：{location}。"
                        + "请退出旧的 WeChatDataAnalysis/Remote PC 实例后再重试；不会自动终止未知实例。"
                        + $" 当前数据目录应为：{_dataRoot}");
                }
            }
            catch (HttpRequestException) { }

            if (!File.Exists(_backendPath))
                throw new FileNotFoundException("未找到 WeChatDataAnalysis 发布版后端。", _backendPath);
            if (_backend is { HasExited: false })
            {
                if (await IsBackendReadyAsync(cancellationToken)) return;
                throw new InvalidOperationException("已启动的微信解析后端尚未完成数据目录初始化。");
            }

            var startInfo = new ProcessStartInfo(_backendPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _dataRoot,
            };
            startInfo.Environment["WECHAT_TOOL_PORT"] = "10492";
            startInfo.Environment["WECHAT_TOOL_DATA_DIR"] = _dataRoot;
            startInfo.Environment["WECHAT_TOOL_OUTPUT_DIR"] = Path.Combine(_dataRoot, "output");
            _backend = Process.Start(startInfo);
            for (var i = 0; i < 30; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(500, cancellationToken);
                if (await IsBackendReadyAsync(cancellationToken)) return;
            }
            throw new TimeoutException($"WeChatDataAnalysis 后端启动超时或未加载数据目录：{_dataRoot}");
        }
        finally
        {
            _backendGate.Release();
        }
    }

    private async Task<bool> IsBackendHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            _trace?.Invoke("HTTP GET 开始：/api/health");
            using var response = await _http.GetAsync(DefaultBaseUrl + "/api/health", cancellationToken);
            _trace?.Invoke($"HTTP GET 完成：/api/health -> {(int)response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private async Task<bool> IsBackendReadyAsync(CancellationToken cancellationToken)
    {
        if (!await IsBackendHealthyAsync(cancellationToken)) return false;

        try
        {
            _trace?.Invoke("HTTP GET 开始：/api/chat/accounts（就绪检查）");
            using var response = await _http.GetAsync(DefaultBaseUrl + "/api/chat/accounts", cancellationToken);
            _trace?.Invoke($"HTTP GET 完成：/api/chat/accounts（就绪检查） -> {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode) return false;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var hasAccounts = document.RootElement.TryGetProperty("accounts", out var accounts)
                && accounts.ValueKind == JsonValueKind.Array
                && accounts.GetArrayLength() > 0;
            if (!hasAccounts) return false;

            var logPath = await GetBackendLogPathAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(logPath)) return true;
            var expectedLogRoot = Path.GetFullPath(Path.Combine(_dataRoot, "output"));
            var actualLogPath = Path.GetFullPath(logPath);
            return actualLogPath.StartsWith(expectedLogRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<string?> GetBackendLogPathAsync(CancellationToken cancellationToken)
    {
        try
        {
            _trace?.Invoke("HTTP GET 开始：/api/admin/log-file");
            using var response = await _http.GetAsync(DefaultBaseUrl + "/api/admin/log-file", cancellationToken);
            _trace?.Invoke($"HTTP GET 完成：/api/admin/log-file -> {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode) return null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return GetString(document.RootElement, "path");
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        _trace?.Invoke($"HTTP GET 开始：{path}");
        using var response = await _http.GetAsync(DefaultBaseUrl + path, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        _trace?.Invoke($"HTTP GET 完成：{path} -> {(int)response.StatusCode}");
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
        _backendGate.Dispose();
        if (_backend is { HasExited: false })
        {
            try { _backend.Kill(entireProcessTree: true); } catch { }
        }
        _backend?.Dispose();
    }
}
