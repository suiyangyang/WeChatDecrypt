using System.Net;
using System.Text;
using System.Text.Json;
using Lumine.Fairy.Application;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Infrastructure;

public sealed class NotificationHttpServer : IAsyncDisposable
{
    private readonly NotificationService _notifications;
    private readonly string _token;
    private readonly string _bindAddress;
    private readonly string _advertisedHost;
    private readonly WeChatAnalysisCoordinator? _analysis;
    private readonly IncrementalAnalysisScheduler? _scheduler;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;

    public NotificationHttpServer(
        NotificationService notifications,
        int port = 18765,
        string token = "local-dev-notification-token",
        WeChatAnalysisCoordinator? analysis = null,
        IncrementalAnalysisScheduler? scheduler = null,
        string? bindAddress = null)
    {
        _notifications = notifications;
        _token = token;
        _bindAddress = NormalizeBindAddress(bindAddress);
        _advertisedHost = ResolveAdvertisedHost(_bindAddress);
        _analysis = analysis;
        _scheduler = scheduler;
        Port = port;
        _listener.Prefixes.Add($"http://{_bindAddress}:{port}/");
    }

    public int Port { get; }
    public string BindAddress => _bindAddress;
    public string Endpoint => $"http://{_advertisedHost}:{Port}/api/v1/notifications";
    public bool IsRunning => _listener.IsListening;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_listener.IsListening) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener.Start();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleAsync(context), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { }
            catch (ObjectDisposedException) { break; }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            if (!IsAuthorized(context.Request))
            {
                await WriteAsync(context.Response, 401, new { error = "unauthorized" });
                return;
            }

            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            if (context.Request.HttpMethod == "POST" && path.Equals("/api/v1/notifications", StringComparison.OrdinalIgnoreCase))
            {
                var request = await JsonSerializer.DeserializeAsync<NotificationRequest>(context.Request.InputStream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (request is null
                    || string.IsNullOrWhiteSpace(request.Title)
                    || request.Body is null)
                {
                    await WriteAsync(context.Response, 400, new { error = "title and body are required" });
                    return;
                }
                var notification = await _notifications.PublishAsync(request);
                await WriteAsync(context.Response, 201, new { notificationId = notification.Id, status = "accepted", createdAt = notification.CreatedAt });
                return;
            }

            if (context.Request.HttpMethod == "POST" && path.Equals("/api/v1/analysis/latest", StringComparison.OrdinalIgnoreCase))
            {
                if (_analysis is null)
                {
                    await WriteAsync(context.Response, 503, new { error = "analysis_unavailable" });
                    return;
                }
                var analysisRequest = await JsonSerializer.DeserializeAsync<AnalysisTriggerRequest>(context.Request.InputStream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var text = _scheduler is not null
                    ? await _scheduler.AnalyzeNowAsync()
                    : await _analysis.AnalyzeLatestAsync(analysisRequest?.AgentId ?? "default");
                await WriteAsync(context.Response, 200, new { status = "completed", responseLength = text.Length });
                return;
            }

            if (context.Request.HttpMethod == "POST" && path.StartsWith("/api/v1/notifications/", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = path["/api/v1/notifications/".Length..];
                var actionMarker = "/actions/";
                var responseMarker = "/responses";
                var notificationId = suffix.Contains(actionMarker, StringComparison.OrdinalIgnoreCase)
                    ? suffix[..suffix.IndexOf(actionMarker, StringComparison.OrdinalIgnoreCase)]
                    : suffix.EndsWith(responseMarker, StringComparison.OrdinalIgnoreCase)
                        ? suffix[..^responseMarker.Length]
                        : string.Empty;
                var notification = string.IsNullOrWhiteSpace(notificationId) ? null : await _notifications.GetAsync(notificationId);
                if (notification is null)
                {
                    await WriteAsync(context.Response, 404, new { error = "not_found" });
                    return;
                }

                if (suffix.Contains(actionMarker, StringComparison.OrdinalIgnoreCase))
                {
                    var actionId = suffix[(suffix.IndexOf(actionMarker, StringComparison.OrdinalIgnoreCase) + actionMarker.Length)..];
                    if (_analysis is null)
                    {
                        await WriteAsync(context.Response, 503, new { error = "analysis_unavailable" });
                        return;
                    }
                    var result = await _analysis.HandleNotificationActionAsync(notification, actionId, "default");
                    await WriteAsync(context.Response, 200, new { status = "completed", notificationId = notification.Id, message = result });
                    return;
                }

                if (suffix.EndsWith(responseMarker, StringComparison.OrdinalIgnoreCase))
                {
                    var response = await JsonSerializer.DeserializeAsync<NotificationResponse>(context.Request.InputStream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (response is null)
                    {
                        await WriteAsync(context.Response, 400, new { error = "invalid_response" });
                        return;
                    }
                    if (_analysis is null)
                    {
                        await WriteAsync(context.Response, 503, new { error = "analysis_unavailable" });
                        return;
                    }
                    var result = await _analysis.HandleQuestionResponseAsync(notification, response, "default");
                    await WriteAsync(context.Response, 200, new { status = "submitted", responseLength = result.Length });
                    return;
                }
            }

            if (context.Request.HttpMethod == "GET" && path.StartsWith("/api/v1/notifications/", StringComparison.OrdinalIgnoreCase))
            {
                var id = path[("/api/v1/notifications/".Length)..];
                var notification = await _notifications.GetAsync(id);
                if (notification is null) await WriteAsync(context.Response, 404, new { error = "not_found" });
                else await WriteAsync(context.Response, 200, notification);
                return;
            }

            await WriteAsync(context.Response, 404, new { error = "not_found" });
        }
        catch (JsonException)
        {
            await WriteAsync(context.Response, 400, new { error = "invalid_json" });
        }
        catch (Exception ex)
        {
            await WriteAsync(context.Response, 500, new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"] ?? string.Empty;
        return header.Equals($"Bearer {_token}", StringComparison.Ordinal);
    }

    private static string NormalizeBindAddress(string? bindAddress)
    {
        var value = string.IsNullOrWhiteSpace(bindAddress) ? "+" : bindAddress.Trim();
        if (value is "*" or "+" or "0.0.0.0") return "+";

        if (IPAddress.TryParse(value, out var address))
        {
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("通知接口当前只支持 IPv4 绑定地址。", nameof(bindAddress));
            return address.ToString();
        }

        if (Uri.CheckHostName(value) is UriHostNameType.Dns or UriHostNameType.Basic)
            return value;

        throw new ArgumentException("通知接口绑定地址无效。", nameof(bindAddress));
    }

    private static string ResolveAdvertisedHost(string bindAddress)
    {
        if (!string.Equals(bindAddress, "+", StringComparison.Ordinal)) return bindAddress;

        try
        {
            var address = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .FirstOrDefault(item => item.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                        && !IPAddress.IsLoopback(item));
            return address?.ToString() ?? "127.0.0.1";
        }
        catch (System.Net.Sockets.SocketException)
        {
            return "127.0.0.1";
        }
    }

    private static async Task WriteAsync(HttpListenerResponse response, int statusCode, object value)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record AnalysisTriggerRequest(string? AgentId);
}
