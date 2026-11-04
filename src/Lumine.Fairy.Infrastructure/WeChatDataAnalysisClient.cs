using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Infrastructure;

public sealed class WeChatDataAnalysisClient : IWeChatService, IDisposable
{
    private const string DefaultBaseUrl = "http://127.0.0.1:10492";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/LifeArchiveProject/WeChatDataAnalysis/releases/latest";
    private const int BackendStartupTimeoutSeconds = 60;
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(60) };
    private readonly HttpClient _versionCheckHttp = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly string _backendPath;
    private readonly string _dataRoot;
    private readonly string _accountKeysPath;
    private readonly Action<string>? _trace;
    private readonly SemaphoreSlim _backendGate = new(1, 1);
    private readonly SemaphoreSlim _versionCheckGate = new(1, 1);
    private Process? _backend;
    private string _lastBackendReadinessIssue = "后端尚未就绪";
    private string? _lastBackendProcessIssue;
    private string? _cachedBackendVersionStatus;
    private DateTimeOffset _backendVersionCheckedAt;

    public WeChatDataAnalysisClient(
        string? backendPath = null,
        TimeSpan? requestTimeout = null,
        Action<string>? trace = null,
        string? dataRoot = null)
    {
        _backendPath = backendPath ?? ResolveBackendPath();
        _dataRoot = ResolveDataRoot(dataRoot);
        _accountKeysPath = Path.Combine(_dataRoot, "output", "account_keys.json");
        _trace = trace;
        _http.Timeout = requestTimeout ?? TimeSpan.FromSeconds(60);
        _versionCheckHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Lumine.Fairy/1.0");
    }

    public string DataRoot => _dataRoot;
    public string AccountKeysPath => _accountKeysPath;

    private static string ResolveDataRoot(string? configuredDataRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuredDataRoot))
            return Path.GetFullPath(configuredDataRoot);

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

    private bool HasDecryptedDatabases()
    {
        var databasesPath = Path.Combine(_dataRoot, "output", "databases");
        try
        {
            return Directory.Exists(databasesPath)
                   && Directory.EnumerateFiles(databasesPath, "*.db", SearchOption.AllDirectories)
                       .Any(path => new FileInfo(path).Length > 0);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public async Task<WeChatDataAccessAuthorizationStatus> GetDataAccessAuthorizationStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var databasesPath = Path.Combine(_dataRoot, "output", "databases");
        var hasDatabaseFiles = HasDecryptedDatabases();

        var backendHealthy = false;
        var accountCount = 0;
        string? backendVersionStatus = null;
        string? backendLogPath = null;
        bool? backendUsesCurrentDataRoot = null;
        using var inspectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        inspectionTimeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            backendHealthy = await IsBackendHealthyAsync(inspectionTimeout.Token);
            if (backendHealthy)
            {
                accountCount = await GetBackendAccountCountAsync(inspectionTimeout.Token);
                backendLogPath = await GetBackendLogPathAsync(inspectionTimeout.Token);
                if (!string.IsNullOrWhiteSpace(backendLogPath))
                {
                    var expectedLogRoot = Path.GetFullPath(Path.Combine(_dataRoot, "output"));
                    var actualLogPath = Path.GetFullPath(backendLogPath);
                    backendUsesCurrentDataRoot = actualLogPath.StartsWith(
                        expectedLogRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }

        var accountKeysPresent = HasAccountKeys(_accountKeysPath);
        if (!backendHealthy && accountKeysPresent)
            backendVersionStatus = await CheckBackendVersionAsync(cancellationToken);

        return new WeChatDataAccessAuthorizationStatus(
            _dataRoot,
            _accountKeysPath,
            databasesPath,
            accountKeysPresent,
            hasDatabaseFiles,
            backendHealthy,
            backendUsesCurrentDataRoot,
            accountCount,
            backendLogPath,
            backendVersionStatus);
    }

    public int OpenDataAccessAuthorizationTool()
    {
        var toolPath = ResolveDataAccessToolPath();
        if (!File.Exists(toolPath))
            throw new FileNotFoundException("未找到微信数据工具 WeChatDataAnalysis.exe。", toolPath);

        var startInfo = new ProcessStartInfo(toolPath)
        {
            UseShellExecute = false,
            WorkingDirectory = _dataRoot,
        };
        startInfo.Environment["WECHAT_TOOL_DATA_DIR"] = _dataRoot;
        startInfo.Environment["WECHAT_TOOL_OUTPUT_DIR"] = Path.Combine(_dataRoot, "output");
        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("微信数据工具启动失败：未能创建进程。");
        return process.Id;
    }

    private string BuildMissingAccountKeysMessage()
        => $"微信增量分析无法读取账号：运行目录缺少微信数据库密钥文件。"
           + $"请按以下步骤准备数据：1. 保持当前微信账号在本机登录；"
           + "2. 打开 WeChatDataAnalysisRelease，在解密页面完成微信检测并获取对应账号的数据库密钥；"
           + "3. 使用该账号对应的 db_storage 路径执行解密；"
           + $"4. 确认 account_keys.json 已保存到：{_accountKeysPath}，并确认 output\\databases 下存在解密数据库；"
           + "5. 完全退出 Fairy（包括系统托盘实例）后重新启动。请勿将数据库密钥粘贴到聊天、日志或通知中。";

    private async Task<string> BuildBackendInitializationMessageAsync(CancellationToken cancellationToken)
    {
        var hasAccountKeys = HasAccountKeys(_accountKeysPath);
        var hasDatabaseFiles = HasDecryptedDatabases();
        var keysStatus = hasAccountKeys ? "已找到" : "未找到";
        var databasesStatus = hasDatabaseFiles ? "已找到解密数据库" : "未找到解密数据库";
        var versionStatus = await CheckBackendVersionAsync(cancellationToken);

        return $"微信解析后端在 {BackendStartupTimeoutSeconds} 秒内未完成初始化；就绪检查要求健康检查通过、检测到账号且日志目录与当前数据目录一致。"
               + $"最近检查结果：{_lastBackendReadinessIssue}。当前数据目录：{_dataRoot}；account_keys.json：{keysStatus}；{databasesStatus}。"
               + $"版本检查：{versionStatus}请在“设置 > 微信分析”检查数据目录和后端状态，并查看该目录 output 下的后端日志后重试。";
    }

    private async Task<string> CheckBackendVersionAsync(CancellationToken cancellationToken)
    {
        await _versionCheckGate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedBackendVersionStatus is not null
                && DateTimeOffset.UtcNow - _backendVersionCheckedAt < TimeSpan.FromMinutes(1))
                return _cachedBackendVersionStatus;

            _cachedBackendVersionStatus = await CheckBackendVersionCoreAsync(cancellationToken);
            _backendVersionCheckedAt = DateTimeOffset.UtcNow;
            return _cachedBackendVersionStatus;
        }
        finally
        {
            _versionCheckGate.Release();
        }
    }

    private async Task<string> CheckBackendVersionCoreAsync(CancellationToken cancellationToken)
    {
        var localVersion = File.Exists(_backendPath)
            ? FileVersionInfo.GetVersionInfo(_backendPath).ProductVersion
              ?? FileVersionInfo.GetVersionInfo(_backendPath).FileVersion
            : null;
        var nativeBuild = ReadNativeBuildMetadata();
        var nativeStatus = nativeBuild switch
        {
            { ExpiresAt: { } expiresAt } when expiresAt <= DateTimeOffset.UtcNow
                => $"随包原生组件（构建 {FormatVersion(nativeBuild.BuildId)}）已于 {expiresAt.ToLocalTime():yyyy-MM-dd HH:mm} 过期，需要更新后端资源。",
            { ExpiresAt: { } expiresAt }
                => $"随包原生组件（构建 {FormatVersion(nativeBuild.BuildId)}）有效期至 {expiresAt.ToLocalTime():yyyy-MM-dd HH:mm}。",
            _ => "未能读取随包原生组件版本有效期。",
        };

        try
        {
            using var response = await _versionCheckHttp.GetAsync(LatestReleaseApiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return $"当前后端版本 {FormatVersion(localVersion)}；无法查询上游最新版本（HTTP {(int)response.StatusCode}）。{nativeStatus}";

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var latestTag = document.RootElement.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()
                : null;
            var latestVersionText = latestTag?.Trim().TrimStart('v', 'V');
            var localVersionText = localVersion?.Trim().TrimStart('v', 'V');
            if (Version.TryParse(latestVersionText, out var latestVersion)
                && Version.TryParse(localVersionText, out var currentVersion))
            {
                var releaseUrl = document.RootElement.TryGetProperty("html_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;
                var updateStatus = latestVersion > currentVersion
                    ? $"发现新版本 {latestTag}（当前 {localVersion}），建议更新后端；发行页：{releaseUrl ?? "https://github.com/LifeArchiveProject/WeChatDataAnalysis/releases/latest"}。"
                    : $"当前后端版本 {localVersion}，未发现需要更新的正式版本。";
                return updateStatus + nativeStatus;
            }

            return $"无法比较当前版本 {FormatVersion(localVersion)} 与上游版本 {FormatVersion(latestTag)}。{nativeStatus}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return $"检查上游版本超时；当前后端版本 {FormatVersion(localVersion)}。{nativeStatus}";
        }
        catch (HttpRequestException)
        {
            return $"无法连接上游查询最新版本；当前后端版本 {FormatVersion(localVersion)}。{nativeStatus}";
        }
        catch (JsonException)
        {
            return $"上游版本信息格式异常；当前后端版本 {FormatVersion(localVersion)}。{nativeStatus}";
        }
    }

    private (string? BuildId, DateTimeOffset? ExpiresAt) ReadNativeBuildMetadata()
    {
        try
        {
            var backendDirectory = Path.GetDirectoryName(_backendPath);
            if (string.IsNullOrWhiteSpace(backendDirectory)) return (null, null);
            var metadataPath = Path.Combine(backendDirectory, "native", "wechatdb_native_build.json");
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = document.RootElement;
            var buildId = root.TryGetProperty("buildId", out var idElement) ? idElement.GetString() : null;
            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("buildExpiresAtUnix", out var expiryElement)
                && expiryElement.TryGetInt64(out var expiryUnix))
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiryUnix);
            return (buildId, expiresAt);
        }
        catch (IOException) { return (null, null); }
        catch (UnauthorizedAccessException) { return (null, null); }
        catch (JsonException) { return (null, null); }
        catch (ArgumentOutOfRangeException) { return (null, null); }
    }

    private static string FormatVersion(string? version)
        => string.IsNullOrWhiteSpace(version) ? "未知" : version;

    private static string? ClassifyBackendDiagnostic(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        if (line.Contains("ModuleNotFoundError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ImportError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("DLL load failed", StringComparison.OrdinalIgnoreCase))
            return "后端依赖加载失败";
        if (line.Contains("FileNotFoundError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)
            || line.Contains("The system cannot find the file", StringComparison.OrdinalIgnoreCase))
            return "后端启动依赖或必需文件缺失";
        if (line.Contains("PermissionError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || line.Contains("WinError 5", StringComparison.OrdinalIgnoreCase))
            return "后端启动时遇到文件访问权限错误";
        if (line.Contains("JSONDecodeError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("JSON parse", StringComparison.OrdinalIgnoreCase))
            return "后端读取 JSON 配置失败";
        if (line.Contains("sqlite3.", StringComparison.OrdinalIgnoreCase)
            || line.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
            || line.Contains("not a database", StringComparison.OrdinalIgnoreCase))
            return "后端读取数据库失败";
        var separator = line.IndexOf(':');
        if (separator > 0)
        {
            var exceptionType = line[..separator].Trim().Split('.').Last();
            var looksLikeExceptionType = exceptionType.EndsWith("Error", StringComparison.Ordinal)
                                         || exceptionType.EndsWith("Exception", StringComparison.Ordinal)
                                         || exceptionType.EndsWith("Exit", StringComparison.Ordinal);
            if (looksLikeExceptionType
                && exceptionType.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
                && exceptionType.Length <= 80)
                return $"后端异常类型为 {exceptionType}，详细内容已隐藏以保护本地数据";
        }
        if (line.Contains("Traceback", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Fatal error", StringComparison.OrdinalIgnoreCase)
            || line.Contains("panic:", StringComparison.OrdinalIgnoreCase))
            return "后端报告了启动异常，详细内容已隐藏以保护本地数据";
        return null;
    }

    private void CaptureBackendDiagnostic(object? sender, DataReceivedEventArgs args)
    {
        var issue = ClassifyBackendDiagnostic(args.Data);
        if (issue is not null)
            Volatile.Write(ref _lastBackendProcessIssue, issue);
    }

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

    private static string ResolveDataAccessToolPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "WeChatDataAnalysisRelease", "WeChatDataAnalysis", "WeChatDataAnalysis.exe"),
            Path.Combine(Environment.CurrentDirectory, "WeChatDataAnalysisRelease", "WeChatDataAnalysis", "WeChatDataAnalysis.exe"),
            @"E:\Work\Code\Tools\Lumine.Fairy\WeChatDataAnalysisRelease\WeChatDataAnalysis\WeChatDataAnalysis.exe",
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
        var effectiveLimit = Math.Clamp(limit, 1, 200);
        using var document = await GetJsonAsync($"/api/chat/messages?account={a}&username={c}&limit={effectiveLimit}&order=desc&source=realtime", cancellationToken);
        var result = new List<ChatMessage>();
        var fallbackOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!document.RootElement.TryGetProperty("messages", out var messages)) return result;
        foreach (var item in messages.EnumerateArray().Take(effectiveLimit))
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
            var senderIdentity = !string.IsNullOrWhiteSpace(senderUsername) ? senderUsername.Trim()
                : !string.IsNullOrWhiteSpace(senderDisplayName) ? senderDisplayName.Trim()
                : isSent ? "我" : sender;
            var timestamp = GetLong(item, "createTime") ?? GetLong(item, "create_time") ?? 0;
            var id = GetMessageId(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                var fingerprint = $"{timestamp}\u001f{senderIdentity}\u001f{content}";
                var ordinal = fallbackOrdinals.TryGetValue(fingerprint, out var previousOrdinal)
                    ? previousOrdinal
                    : 0;
                fallbackOrdinals[fingerprint] = ordinal + 1;
                id = BuildStableFallbackMessageId(accountId, conversationId, senderIdentity, content, timestamp, ordinal);
            }

            result.Add(new ChatMessage(id, sender, content, timestamp));
        }
        result.Reverse();
        return result;
    }

    internal static string BuildStableFallbackMessageId(
        string accountId,
        string conversationId,
        string sender,
        string content,
        long timestamp,
        int ordinal)
    {
        var input = string.Join('\u001f',
            accountId.Trim(),
            conversationId.Trim(),
            timestamp.ToString(CultureInfo.InvariantCulture),
            sender.Trim(),
            content,
            ordinal.ToString(CultureInfo.InvariantCulture));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"fallback-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string? GetMessageId(JsonElement item)
    {
        foreach (var property in new[]
        {
            "serverId", "server_id", "messageServerId", "message_server_id", "messageId", "message_id",
            "msgSvrId", "msg_svr_id", "svrId", "svr_id", "serverIdStr"
        })
        {
            var value = GetIdentifier(item, property);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        foreach (var property in new[]
        {
            "localId", "local_id", "messageLocalId", "message_local_id", "msgLocalId", "msg_local_id",
            "msgId", "msg_id"
        })
        {
            var value = GetIdentifier(item, property);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return null;
    }

    private static string? GetIdentifier(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
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
                    throw new InvalidOperationException(await BuildBackendInitializationMessageAsync(cancellationToken));
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
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.Environment["WECHAT_TOOL_PORT"] = "10492";
            startInfo.Environment["WECHAT_TOOL_DATA_DIR"] = _dataRoot;
            startInfo.Environment["WECHAT_TOOL_OUTPUT_DIR"] = Path.Combine(_dataRoot, "output");
            _lastBackendProcessIssue = null;
            _backend = Process.Start(startInfo);
            if (_backend is not null)
            {
                _backend.OutputDataReceived += CaptureBackendDiagnostic;
                _backend.ErrorDataReceived += CaptureBackendDiagnostic;
                _backend.BeginOutputReadLine();
                _backend.BeginErrorReadLine();
            }
            for (var i = 0; i < BackendStartupTimeoutSeconds * 2; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(500, cancellationToken);
                if (_backend is { HasExited: true } exitedBackend)
                {
                    exitedBackend.WaitForExit();
                    var processIssue = Volatile.Read(ref _lastBackendProcessIssue);
                    var versionStatus = await CheckBackendVersionAsync(cancellationToken);
                    throw new InvalidOperationException(
                        $"微信解析后端启动后已退出（退出码 {exitedBackend.ExitCode}）。"
                        + (processIssue is null ? string.Empty : $"诊断：{processIssue}。")
                        + $"数据目录：{_dataRoot}。版本检查：{versionStatus}请查看该目录 output 下的后端日志；原始输出不会写入通知。");
                }
                if (await IsBackendReadyAsync(cancellationToken)) return;
            }
            throw new TimeoutException(await BuildBackendInitializationMessageAsync(cancellationToken));
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
        if (!await IsBackendHealthyAsync(cancellationToken))
        {
            _lastBackendReadinessIssue = "健康检查未通过";
            return false;
        }

        try
        {
            _trace?.Invoke("HTTP GET 开始：/api/chat/accounts（就绪检查）");
            using var response = await _http.GetAsync(DefaultBaseUrl + "/api/chat/accounts", cancellationToken);
            _trace?.Invoke($"HTTP GET 完成：/api/chat/accounts（就绪检查） -> {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                _lastBackendReadinessIssue = $"账号接口返回 HTTP {(int)response.StatusCode}";
                return false;
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var hasAccounts = document.RootElement.TryGetProperty("accounts", out var accounts)
                && accounts.ValueKind == JsonValueKind.Array
                && accounts.GetArrayLength() > 0;
            if (!hasAccounts)
            {
                _lastBackendReadinessIssue = "账号接口尚未返回可用账号";
                return false;
            }

            var logPath = await GetBackendLogPathAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(logPath))
            {
                _lastBackendReadinessIssue = "账号已加载，后端未提供日志位置";
                return true;
            }
            var expectedLogRoot = Path.GetFullPath(Path.Combine(_dataRoot, "output"));
            var actualLogPath = Path.GetFullPath(logPath);
            if (!actualLogPath.StartsWith(expectedLogRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                _lastBackendReadinessIssue = $"日志目录不匹配，当前为 {actualLogPath}，期望位于 {expectedLogRoot}";
                return false;
            }

            _lastBackendReadinessIssue = string.Empty;
            return true;
        }
        catch (HttpRequestException)
        {
            _lastBackendReadinessIssue = "账号接口连接失败";
            return false;
        }
        catch (JsonException)
        {
            _lastBackendReadinessIssue = "账号接口返回了无法解析的数据";
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

    private async Task<int> GetBackendAccountCountAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(DefaultBaseUrl + "/api/chat/accounts", cancellationToken);
            if (!response.IsSuccessStatusCode) return 0;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("accounts", out var accounts)
                   && accounts.ValueKind == JsonValueKind.Array
                ? accounts.GetArrayLength()
                : 0;
        }
        catch (HttpRequestException)
        {
            return 0;
        }
        catch (JsonException)
        {
            return 0;
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
        _versionCheckHttp.Dispose();
        _backendGate.Dispose();
        _versionCheckGate.Dispose();
        if (_backend is { HasExited: false })
        {
            try { _backend.Kill(entireProcessTree: true); } catch { }
        }
        _backend?.Dispose();
    }
}

public sealed record WeChatDataAccessAuthorizationStatus(
    string DataRoot,
    string AccountKeysPath,
    string DatabasesPath,
    bool AccountKeysPresent,
    bool DatabaseFilesPresent,
    bool BackendHealthy,
    bool? BackendUsesCurrentDataRoot,
    int AccountCount,
    string? BackendLogPath,
    string? BackendVersionStatus)
{
    public bool IsReady => AccountKeysPresent
                           && BackendHealthy
                           && BackendUsesCurrentDataRoot != false
                           && AccountCount > 0;
}
