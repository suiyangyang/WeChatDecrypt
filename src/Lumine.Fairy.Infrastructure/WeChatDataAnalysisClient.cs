using System.Diagnostics;
using System.Globalization;
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
    private readonly string _accountKeysPath;
    private readonly Action<string>? _trace;
    private readonly SemaphoreSlim _backendGate = new(1, 1);
    private Process? _backend;

    public WeChatDataAnalysisClient(string? backendPath = null, TimeSpan? requestTimeout = null, Action<string>? trace = null)
    {
        _backendPath = backendPath ?? ResolveBackendPath();
        _dataRoot = ResolveDataRoot();
        _accountKeysPath = Path.Combine(_dataRoot, "output", "account_keys.json");
        _trace = trace;
        _http.Timeout = requestTimeout ?? TimeSpan.FromSeconds(60);
    }

    private static string ResolveDataRoot()
    {
        var configured = ReadEnvironmentVariable("WECHAT_TOOL_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        // The backend's persistent data belongs beside the running application.
        // Keeping this deterministic prevents a debug build from silently picking
        // up another checkout's account keys or databases.
        return Path.GetFullPath(AppContext.BaseDirectory);
    }

    private static string ReadEnvironmentVariable(string name)
    {
        foreach (var target in new[]
        {
            EnvironmentVariableTarget.Process,
            EnvironmentVariableTarget.User,
            EnvironmentVariableTarget.Machine,
        })
        {
            var value = Environment.GetEnvironmentVariable(name, target);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return string.Empty;
    }

    private static bool HasAccountKeys(string path)
        => File.Exists(path) && new FileInfo(path).Length > 0;

    private string BuildMissingAccountKeysMessage()
        => $"微信增量分析无法读取账号：运行目录缺少微信数据库密钥文件。"
           + $"请按以下步骤准备数据：1. 保持当前微信账号在本机登录；"
           + "2. 打开 WeChatDataAnalysisRelease，在解密页面完成微信检测并获取对应账号的数据库密钥；"
           + "3. 使用该账号对应的 db_storage 路径执行解密；"
           + $"4. 确认 account_keys.json 已保存到：{_accountKeysPath}，并确认 output\\databases 下存在解密数据库；"
           + "5. 完全退出 Fairy（包括系统托盘实例）后重新启动。请勿将数据库密钥粘贴到聊天、日志或通知中。";

    private string BuildBackendInitializationMessage()
        => $"微信解析后端已由当前 Fairy 实例启动，但尚未完成数据目录初始化：{_dataRoot}。"
           + $"请确认 {_accountKeysPath} 存在且已完成数据库解密，然后重启 Fairy。";

    private string BuildExternalBackendMessage(string? logPath)
    {
        var location = string.IsNullOrWhiteSpace(logPath) ? "未知目录" : logPath;
        return $"检测到已有微信解析后端占用 10492，但它没有加载当前微信数据目录。后端日志位置：{location}。"
               + "请退出旧的 WeChatDataAnalysis/Remote PC 实例后再重试；不会自动终止未知实例。"
               + $" 当前数据目录应为：{_dataRoot}";
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
        using var document = await GetJsonAsync($"/api/chat/sessions?account={encoded}&limit=200&preview=latest&source=realtime", cancellationToken);
        var result = new List<ChatSession>();
        if (!document.RootElement.TryGetProperty("sessions", out var sessions)) return result;
        foreach (var item in sessions.EnumerateArray())
        {
            result.Add(new ChatSession(
                GetString(item, "username") ?? string.Empty,
                GetString(item, "displayName") ?? GetString(item, "display_name") ?? GetString(item, "name") ?? GetString(item, "username") ?? "会话",
                GetString(item, "preview") ?? GetString(item, "lastMessage") ?? string.Empty,
                GetTimestamp(item, "lastMessageTime", "last_message_time"),
                GetConversationKind(item, GetString(item, "username"))));
        }
        return result.Where(x => !string.IsNullOrWhiteSpace(x.Username)).ToList();
    }

    public async Task<IReadOnlyList<ChatContact>> GetChatContactsAsync(string accountId, bool includeGroups = true, CancellationToken cancellationToken = default)
    {
        await EnsureBackendAsync(cancellationToken);
        var encoded = Uri.EscapeDataString(accountId);
        var groups = includeGroups ? "true" : "false";
        using var document = await GetJsonAsync($"/api/chat/contacts?account={encoded}&include_groups={groups}&source=realtime", cancellationToken);
        var result = new List<ChatContact>();
        if (!document.RootElement.TryGetProperty("contacts", out var contacts) || contacts.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in contacts.EnumerateArray())
        {
            var username = GetString(item, "username") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username)) continue;
            result.Add(new ChatContact(
                username,
                GetString(item, "displayName") ?? GetString(item, "display_name") ?? string.Empty,
                GetString(item, "nickname") ?? string.Empty,
                GetString(item, "type") ?? string.Empty,
                GetString(item, "name") ?? string.Empty,
                GetString(item, "remark") ?? string.Empty));
        }

        return result;
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

    private static ChatConversationKind GetConversationKind(JsonElement item, string? username)
    {
        var isGroup = GetBoolean(item, "isGroup") ?? GetBoolean(item, "is_group")
                      ?? GetBoolean(item, "isChatRoom") ?? GetBoolean(item, "is_chatroom")
                      ?? GetBoolean(item, "isGroupChat") ?? GetBoolean(item, "is_group_chat");
        if (isGroup == true) return ChatConversationKind.Group;
        if (isGroup == false) return ChatConversationKind.Direct;

        var type = GetString(item, "type") ?? GetString(item, "conversationType") ?? GetString(item, "conversation_type");
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (type.Contains("group", StringComparison.OrdinalIgnoreCase)
                || type.Contains("chatroom", StringComparison.OrdinalIgnoreCase)
                || type.Contains("群", StringComparison.OrdinalIgnoreCase))
                return ChatConversationKind.Group;
            if (type.Contains("direct", StringComparison.OrdinalIgnoreCase)
                || type.Contains("private", StringComparison.OrdinalIgnoreCase)
                || type.Contains("单聊", StringComparison.OrdinalIgnoreCase))
                return ChatConversationKind.Direct;
        }

        // wxid group identifiers commonly start with @@. This is only a fallback
        // for backends that do not expose conversation type metadata.
        return username?.StartsWith("@@", StringComparison.Ordinal) == true
            || username?.EndsWith("@chatroom", StringComparison.OrdinalIgnoreCase) == true
            ? ChatConversationKind.Group
            : ChatConversationKind.Unknown;
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

                if (!HasAccountKeys(_accountKeysPath))
                {
                    _trace?.Invoke($"未找到账号密钥文件：{_accountKeysPath}");
                    throw new InvalidOperationException(BuildMissingAccountKeysMessage());
                }

                if (_backend is { HasExited: false })
                {
                    throw new InvalidOperationException(BuildBackendInitializationMessage());
                }

                if (await IsBackendHealthyAsync(cancellationToken))
                {
                    var logPath = await GetBackendLogPathAsync(cancellationToken);
                    throw new InvalidOperationException(BuildExternalBackendMessage(logPath));
                }
            }
            catch (HttpRequestException) { }

            if (!File.Exists(_backendPath))
                throw new FileNotFoundException("未找到 WeChatDataAnalysis 发布版后端。", _backendPath);

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
            throw new TimeoutException(BuildBackendInitializationMessage());
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

    private static bool? GetBoolean(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var result) => result,
                _ => null
            }
            : null;

    private static long? GetLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var text)) return text;
        return null;
    }

    private static long GetTimestamp(JsonElement element, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (!element.TryGetProperty(property, out var value)) continue;
            if (TryParseTimestamp(value, out var timestamp)) return timestamp;
        }

        return 0;
    }

    private static bool TryParseTimestamp(JsonElement value, out long timestamp)
    {
        timestamp = 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            timestamp = number;
            return true;
        }

        if (value.ValueKind != JsonValueKind.String) return false;
        var raw = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            timestamp = number;
            return true;
        }

        if (DateTime.TryParseExact(raw, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
        {
            var now = DateTime.Now;
            var local = new DateTime(now.Year, now.Month, now.Day, clock.Hour, clock.Minute, 0, DateTimeKind.Local);
            timestamp = new DateTimeOffset(local).ToUnixTimeMilliseconds();
            return true;
        }

        foreach (var culture in new[] { CultureInfo.GetCultureInfo("zh-CN"), CultureInfo.InvariantCulture })
        {
            if (!DateTimeOffset.TryParse(raw, culture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var parsed))
                continue;

            // Month/day-only values from the backend refer to the current year. If
            // that would place the message in the future, it belongs to last year.
            if (!raw.Contains('年') && parsed > DateTimeOffset.Now.AddDays(1))
                parsed = parsed.AddYears(-1);
            timestamp = parsed.ToUnixTimeMilliseconds();
            return true;
        }

        return false;
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
