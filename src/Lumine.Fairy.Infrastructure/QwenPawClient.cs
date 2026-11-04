using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Infrastructure;

public sealed class QwenPawClient(IProtectedTokenStore tokenStore, HttpClient? http = null) : IQwenPawClient, IDisposable
{
    private static readonly TimeSpan InitialResponseTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TotalChatTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan[] ChatConflictRetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
    ];
    private readonly HttpClient _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(11) };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private string? _token;
    private string _baseUrl = string.Empty;

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_token);
    public string? Username { get; private set; }
    public string BaseUrl => _baseUrl;

    public async Task<QwenPawLoginResult> LoginAsync(string baseUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            throw new ArgumentException("QwenPaw 地址无效。", nameof(baseUrl));
        if (baseUri.Scheme is not ("http" or "https")) throw new ArgumentException("QwenPaw 地址必须使用 HTTP 或 HTTPS。", nameof(baseUrl));

        using var content = new StringContent(JsonSerializer.Serialize(new { username, password }, _json), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(new Uri(baseUri, "api/auth/login"), content, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"QwenPaw 登录失败（HTTP {(int)response.StatusCode}）。");
        var result = JsonSerializer.Deserialize<QwenPawLoginResult>(raw, _json) ?? throw new InvalidOperationException("QwenPaw 登录响应无效。");
        _baseUrl = baseUri.ToString().TrimEnd('/');
        _token = result.Token;
        Username = result.Username;
        await tokenStore.SaveAsync(result.Token, cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<QwenPawAgent>> GetAgentsAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        using var request = CreateRequest(HttpMethod.Get, "api/agents");
        using var response = await _http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"读取 QwenPaw Agent 失败（HTTP {(int)response.StatusCode}）。");
        using var document = JsonDocument.Parse(raw);
        var result = new List<QwenPawAgent>();
        if (document.RootElement.TryGetProperty("agents", out var agents) && agents.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in agents.EnumerateArray())
            {
                result.Add(new QwenPawAgent(
                    GetString(item, "id") ?? string.Empty,
                    GetString(item, "name") ?? GetString(item, "id") ?? "Agent",
                    GetString(item, "description") ?? string.Empty,
                    !item.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False));
            }
        }
        return result;
    }

    public async Task CreateSessionAsync(string agentId, string sessionId, string title, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        if (string.IsNullOrWhiteSpace(agentId)) throw new ArgumentException("QwenPaw Agent ID 不能为空。", nameof(agentId));
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("QwenPaw 会话 ID 不能为空。", nameof(sessionId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("QwenPaw 会话标题不能为空。", nameof(title));

        using var request = CreateRequest(HttpMethod.Post, "api/chats");
        request.Headers.Add("X-Agent-Id", agentId);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            session_id = sessionId,
            user_id = "default",
            channel = "console",
            name = title,
        }, _json), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"创建 QwenPaw 分析会话失败（HTTP {(int)response.StatusCode}）。");
    }

    public async Task<QwenPawChatResult> ChatAsync(string agentId, string input, CancellationToken cancellationToken = default)
        => await ChatAsync(agentId, input, null, cancellationToken);

    public async Task<QwenPawChatResult> ChatAsync(string agentId, string input, string? sessionId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        sessionId = string.IsNullOrWhiteSpace(sessionId) ? $"lumine-fairy-{Guid.NewGuid():N}" : sessionId;

        using var totalTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalTimeoutSource.CancelAfter(TotalChatTimeout);

        var payload = new
        {
            session_id = sessionId,
            user_id = "default",
            channel = "console",
            stream = true,
            input = new[]
            {
                new
                {
                    role = "user",
                    content = new[] { new { type = "text", text = input } },
                },
            },
        };
        var payloadJson = JsonSerializer.Serialize(payload, _json);
        using var response = await SendChatRequestAsync(agentId, payloadJson, cancellationToken, totalTimeoutSource.Token);

        Stream stream;
        try
        {
            stream = await response.Content.ReadAsStreamAsync(totalTimeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                 && totalTimeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException("QwenPaw 单次分析超过 10 分钟。");
        }
        await using var responseStream = stream;
        using var reader = new StreamReader(responseStream);
        var assistantText = new StringBuilder();
        string? responseSessionId = null;
        var assistantMessageIds = new HashSet<string>(StringComparer.Ordinal);
        var hasStreamActivity = false;
        var hasFirstContent = false;
        var hasCompletedResponse = false;
        var eventData = new StringBuilder();
        var eventName = string.Empty;
        using var streamIdleTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(totalTimeoutSource.Token);
        streamIdleTimeoutSource.CancelAfter(InitialResponseTimeout);
        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(streamIdleTimeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                     && totalTimeoutSource.IsCancellationRequested)
            {
                throw new TimeoutException("QwenPaw 单次分析超过 10 分钟。");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                     && streamIdleTimeoutSource.IsCancellationRequested)
            {
                throw new TimeoutException(hasFirstContent
                    ? "QwenPaw 流式响应连续 120 秒没有新数据。"
                    : "QwenPaw 在 120 秒内没有返回首个有效内容。\n");
            }

            if (line is not null && line.Length > 0)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                    eventData.AppendLine(line[5..].TrimStart(' '));
                else if (line.StartsWith("event:", StringComparison.Ordinal))
                    eventName = line[6..].Trim();
                continue;
            }

            var json = eventData.ToString().Trim();
            var currentEventName = eventName;
            eventData.Clear();
            eventName = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                if (line is null) break;
                continue;
            }
            if (string.Equals(currentEventName, "error", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("QwenPaw 服务端返回流式错误，请检查 Agent 的模型配置和服务端日志。");
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                responseSessionId ??= GetString(root, "session_id");
                var objectName = GetString(root, "object");
                var status = GetString(root, "status");
                if (root.TryGetProperty("error", out var modelError)
                    && modelError.ValueKind == JsonValueKind.Object
                    && string.Equals(GetString(modelError, "code"), "MODEL_TIMEOUT", StringComparison.OrdinalIgnoreCase))
                    throw new TimeoutException("QwenPaw 模型响应超时（MODEL_TIMEOUT），请检查当前 Agent 使用的模型服务是否可用。");
                if (string.Equals(objectName, "error", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(GetString(root, "type"), "error", StringComparison.OrdinalIgnoreCase)
                    || (root.TryGetProperty("error", out var error) && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))
                    || (string.Equals(objectName, "response", StringComparison.OrdinalIgnoreCase)
                        && status?.ToLowerInvariant() is "failed" or "error" or "cancelled" or "canceled" or "incomplete"))
                    throw new InvalidOperationException("QwenPaw 服务端分析失败或被中断，请检查 Agent 的模型配置和服务端日志。");
                var isResponseActivity = !string.Equals(objectName, "response", StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);
                if (isResponseActivity)
                {
                    hasStreamActivity = true;
                    streamIdleTimeoutSource.CancelAfter(hasFirstContent ? StreamIdleTimeout : InitialResponseTimeout);
                }

                if ((string.Equals(objectName, "message", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(GetString(root, "type"), "heartbeat", StringComparison.OrdinalIgnoreCase))
                    || string.Equals(objectName, "content", StringComparison.OrdinalIgnoreCase))
                {
                    hasFirstContent = true;
                    streamIdleTimeoutSource.CancelAfter(StreamIdleTimeout);
                }

                if (objectName == "message"
                    && string.Equals(GetString(root, "type"), "message", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(GetString(root, "role"), "assistant", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(GetString(root, "id")))
                {
                    assistantMessageIds.Add(GetString(root, "id")!);
                }

                if (objectName == "content"
                    && assistantMessageIds.Contains(GetString(root, "msg_id") ?? string.Empty))
                {
                    assistantText.Append(GetString(root, "text"));
                }

                if (string.Equals(objectName, "response", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    hasCompletedResponse = true;
                    var finalText = ExtractFinalAssistantText(root);
                    if (!string.IsNullOrWhiteSpace(finalText))
                    {
                        hasFirstContent = true;
                        assistantText.Clear();
                        assistantText.Append(finalText);
                    }
                    break;
                }
            }
            catch (JsonException)
            {
                // Ignore keepalive or malformed SSE fragments; the completed event is authoritative.
            }
            if (line is null) break;
        }

        if (!hasStreamActivity || !hasFirstContent)
            throw new TimeoutException("QwenPaw 在 120 秒内没有返回首个有效内容。");
        if (!hasCompletedResponse)
            throw new InvalidOperationException("QwenPaw 流式响应提前结束，未收到 completed 事件。");

        var text = assistantText.ToString().Trim();
        return new QwenPawChatResult(text, responseSessionId ?? sessionId, TryParseAnalysis(text));
    }

    private async Task<HttpResponseMessage> SendChatRequestAsync(
        string agentId,
        string payloadJson,
        CancellationToken cancellationToken,
        CancellationToken totalTimeoutToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = CreateRequest(HttpMethod.Post, "api/console/chat");
            request.Headers.Add("X-Agent-Id", agentId);
            request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using var initialResponseTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(totalTimeoutToken);
            initialResponseTimeoutSource.CancelAfter(InitialResponseTimeout);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, initialResponseTimeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                     && !totalTimeoutToken.IsCancellationRequested
                                                     && initialResponseTimeoutSource.IsCancellationRequested)
            {
                throw new TimeoutException("QwenPaw 在 120 秒内没有返回首个响应。");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                     && totalTimeoutToken.IsCancellationRequested)
            {
                throw new TimeoutException("QwenPaw 单次分析超过 10 分钟。");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var statusCode = response.StatusCode;
                string? detail;
                try
                {
                    detail = await ReadErrorDetailAsync(response, totalTimeoutToken);
                }
                finally
                {
                    response.Dispose();
                }

                if (attempt < ChatConflictRetryDelays.Length)
                {
                    try
                    {
                        await Task.Delay(ChatConflictRetryDelays[attempt], totalTimeoutToken);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
                                                             && totalTimeoutToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("QwenPaw 单次分析超过 10 分钟。");
                    }

                    continue;
                }

                throw new InvalidOperationException(
                    FormatChatFailure(statusCode, detail, ChatConflictRetryDelays.Length));
            }

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                string? detail;
                try
                {
                    detail = await ReadErrorDetailAsync(response, totalTimeoutToken);
                }
                finally
                {
                    response.Dispose();
                }

                throw new InvalidOperationException(FormatChatFailure(statusCode, detail));
            }

            return response;
        }
    }

    private static async Task<string?> ReadErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            using var document = JsonDocument.Parse(raw);
            var detail = GetString(document.RootElement, "detail");
            return string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatChatFailure(System.Net.HttpStatusCode statusCode, string? detail, int retryCount = 0)
    {
        var retrySuffix = retryCount > 0 ? $" 已重试 {retryCount} 次。" : string.Empty;
        var detailSuffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $"：{detail}";
        return $"QwenPaw 对话失败（HTTP {(int)statusCode}）{detailSuffix}{retrySuffix}";
    }

    public async Task RenameSessionAsync(string sessionId, string title, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("QwenPaw 会话 ID 不能为空。", nameof(sessionId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("QwenPaw 会话标题不能为空。", nameof(title));

        using var listRequest = CreateRequest(HttpMethod.Get, "api/chats");
        using var listResponse = await _http.SendAsync(listRequest, cancellationToken);
        var listRaw = await listResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!listResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"读取 QwenPaw 会话失败（HTTP {(int)listResponse.StatusCode}）。");

        using var document = JsonDocument.Parse(listRaw);
        var chatId = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
                .Where(item => string.Equals(GetString(item, "id"), sessionId, StringComparison.Ordinal)
                               || string.Equals(GetString(item, "session_id"), sessionId, StringComparison.Ordinal))
                .Select(item => GetString(item, "id"))
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
            : null;
        if (string.IsNullOrWhiteSpace(chatId))
            throw new InvalidOperationException("QwenPaw 未找到待命名的分析会话。");

        using var updateRequest = CreateRequest(HttpMethod.Put, $"api/chats/{Uri.EscapeDataString(chatId)}");
        updateRequest.Content = new StringContent(JsonSerializer.Serialize(new { name = title }, _json), Encoding.UTF8, "application/json");
        using var updateResponse = await _http.SendAsync(updateRequest, cancellationToken);
        if (!updateResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"更新 QwenPaw 会话标题失败（HTTP {(int)updateResponse.StatusCode}）。");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        EnsureAuthenticated();
        var request = new HttpRequestMessage(method, new Uri(new Uri(_baseUrl + "/"), relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return request;
    }

    private void EnsureAuthenticated()
    {
        if (!IsAuthenticated || string.IsNullOrWhiteSpace(_baseUrl)) throw new InvalidOperationException("尚未登录 QwenPaw。");
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string ExtractText(JsonElement element)
    {
        var parts = new List<string>();
        CollectText(element, parts);
        return string.Concat(parts);
    }

    private static string ExtractFinalAssistantText(JsonElement response)
    {
        if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var finalText = string.Empty;
        foreach (var item in output.EnumerateArray())
        {
            if (!string.Equals(GetString(item, "type"), "message", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(GetString(item, "role"), "assistant", StringComparison.OrdinalIgnoreCase))
                continue;

            finalText = ExtractText(item);
        }

        return finalText;
    }

    private static void CollectText(JsonElement element, List<string> parts)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("text") && property.Value.ValueKind == JsonValueKind.String)
                    parts.Add(property.Value.GetString() ?? string.Empty);
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    CollectText(property.Value, parts);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectText(item, parts);
        }
    }

    internal static AnalysisResult? TryParseAnalysis(string text)
    {
        for (var start = 0; start < text.Length; start++)
        {
            if (text[start] != '{') continue;

            var end = FindBalancedJsonEnd(text, start);
            if (end < 0) continue;

            try
            {
                var analysis = JsonSerializer.Deserialize<AnalysisResult>(
                    text[start..(end + 1)],
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (analysis is not null && IsValidAnalysis(analysis))
                    return analysis;
            }
            catch (JsonException)
            {
                // Try the next balanced JSON object. Reasoning or markdown may contain other braces.
            }
        }

        return null;
    }

    private static bool IsValidAnalysis(AnalysisResult analysis)
        => !string.IsNullOrWhiteSpace(analysis.Decision)
           && analysis.Todos is not null
           && analysis.Intents is not null
           && analysis.Questions is not null
           && analysis.SourceRefs is not null
           && analysis.Todos.All(IsValidTodo)
           && analysis.Questions.All(IsValidQuestion);

    private static bool IsValidTodo(TodoCandidate? todo)
        => todo is not null
           && !string.IsNullOrWhiteSpace(todo.Id)
           && !string.IsNullOrWhiteSpace(todo.Title)
           && !string.IsNullOrWhiteSpace(todo.Summary)
           && IsValidTodoPriority(todo.Priority)
           && todo.Confidence is >= 0 and <= 1
           && todo.SourceRefs is { Count: > 0 }
           && todo.SourceRefs.All(reference => !string.IsNullOrWhiteSpace(reference));

    private static bool IsValidTodoPriority(string? priority)
        => priority?.Trim().ToLowerInvariant() is "low" or "medium" or "high";

    private static bool IsValidQuestion(ClarificationQuestion? question)
    {
        if (question is null
            || string.IsNullOrWhiteSpace(question.Id)
            || string.IsNullOrWhiteSpace(question.Problem)
            || string.IsNullOrWhiteSpace(question.RecommendedSolutionId)
            || question.Source is null)
            return false;

        var source = question.Source;
        if (string.IsNullOrWhiteSpace(source.ConversationId)
            || string.IsNullOrWhiteSpace(source.From)
            || string.IsNullOrWhiteSpace(source.To)
            || source.Participants is not { Count: > 0 }
            || source.MessageRefs is not { Count: > 0 }
            || source.Participants.Any(string.IsNullOrWhiteSpace)
            || source.MessageRefs.Any(string.IsNullOrWhiteSpace))
            return false;

        var solutions = question.Solutions;
        if (solutions is not { Count: >= 2 }
            || solutions.Any(solution => !IsValidSolution(solution)))
            return false;

        var recommendedSolutions = solutions.Where(solution => solution.Recommended).ToArray();
        return recommendedSolutions.Length == 1
               && string.Equals(
                   question.RecommendedSolutionId,
                   recommendedSolutions[0].Id,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidSolution(QuestionSolution? solution)
        => solution is not null
           && !string.IsNullOrWhiteSpace(solution.Id)
           && !string.IsNullOrWhiteSpace(solution.Label)
           && !string.IsNullOrWhiteSpace(solution.Instruction);

    private static int FindBalancedJsonEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') inString = false;
                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (character == '{') depth++;
            else if (character == '}' && --depth == 0) return index;
        }

        return -1;
    }

    public void Dispose() => _http.Dispose();
}
