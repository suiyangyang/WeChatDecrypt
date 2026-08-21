using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using QwenPawRemotePc.Domain;
using QwenPawRemotePc.Protocol;

namespace QwenPawRemotePc.Infrastructure;

public sealed class QwenPawClient(IProtectedTokenStore tokenStore) : IQwenPawClient, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
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

    public async Task<QwenPawChatResult> ChatAsync(string agentId, string input, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        using var request = CreateRequest(HttpMethod.Post, "api/console/chat");
        request.Headers.Add("X-Agent-Id", agentId);
        var payload = new
        {
            input = new[]
            {
                new
                {
                    role = "user",
                    content = new[] { new { type = "text", text = input } },
                },
            },
        };
        request.Content = new StringContent(JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"QwenPaw 对话失败（HTTP {(int)response.StatusCode}）。");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var partialText = new StringBuilder();
        string? finalText = null;
        string? sessionId = null;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var json = line[5..].Trim();
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                sessionId ??= GetString(root, "session_id");
                var objectName = GetString(root, "object");
                var status = GetString(root, "status");
                var extracted = ExtractText(root);
                if (objectName == "content" && !string.IsNullOrEmpty(extracted)) partialText.Append(extracted);
                if (objectName == "response" && status == "completed") finalText = extracted;
            }
            catch (JsonException)
            {
                // Ignore keepalive or malformed SSE fragments; the completed event is authoritative.
            }
        }

        var text = string.IsNullOrWhiteSpace(finalText) ? partialText.ToString() : finalText;
        return new QwenPawChatResult(text.Trim(), sessionId, TryParseAnalysis(text));
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

    private static AnalysisResult? TryParseAnalysis(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return JsonSerializer.Deserialize<AnalysisResult>(text[start..(end + 1)], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
