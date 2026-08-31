using System.Net;
using System.Text;
using System.Text.Json;
using Lumine.Fairy.Domain;

namespace Lumine.Fairy.Infrastructure;

/// <summary>
/// Read-only LAN/loopback endpoint for the QwenPaw MCP adapter.
/// It exposes resolved display context only and never exposes the local
/// SQLite database or chat messages.
/// </summary>
public sealed class WeChatContextHttpServer : IAsyncDisposable
{
    private const int MaxRequestBodyLength = 8 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<WeChatContextResolveRequest, CancellationToken, Task<ConversationCompanyContext?>> _resolver;
    private readonly string _token;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;

    public WeChatContextHttpServer(
        string bindAddress,
        int port,
        string token,
        Func<WeChatContextResolveRequest, CancellationToken, Task<ConversationCompanyContext?>> resolver)
    {
        if (string.IsNullOrWhiteSpace(bindAddress)) throw new ArgumentException("绑定地址不能为空。", nameof(bindAddress));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("上下文 API Token 不能为空。", nameof(token));

        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _token = token;
        BindAddress = bindAddress.Trim();
        Port = port;
        _listener.Prefixes.Add($"http://{BindAddress}:{Port}/");
    }

    public string BindAddress { get; }
    public int Port { get; }
    public string Endpoint => $"http://{BindAddress}:{Port}/api/v1/wechat-context/resolve";
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
                _ = Task.Run(() => HandleAsync(context, cancellationToken), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { }
            catch (ObjectDisposedException) { break; }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsAuthorized(context.Request))
            {
                await WriteAsync(context.Response, 401, new { error = "unauthorized" }, cancellationToken);
                return;
            }

            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            if (context.Request.HttpMethod == "GET" && path.Equals("/health", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(context.Response, 200, new { status = "ok", service = "lumine-fairy-wechat-context", version = 1 }, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "POST"
                && path.Equals("/api/v1/wechat-context/resolve", StringComparison.OrdinalIgnoreCase))
            {
                if (context.Request.ContentLength64 is > MaxRequestBodyLength)
                {
                    await WriteAsync(context.Response, 413, new { error = "request_too_large" }, cancellationToken);
                    return;
                }

                var request = await ReadRequestAsync(context.Request, cancellationToken);
                if (request is null
                    || string.IsNullOrWhiteSpace(request.AccountId)
                    || string.IsNullOrWhiteSpace(request.WeChatUsername)
                    || string.IsNullOrWhiteSpace(request.DisplayName))
                {
                    await WriteAsync(context.Response, 400, new { error = "accountId, weChatUsername and displayName are required" }, cancellationToken);
                    return;
                }

                var resolved = await _resolver(request, cancellationToken);
                await WriteAsync(context.Response, 200, ToPublicResponse(resolved), cancellationToken);
                return;
            }

            await WriteAsync(context.Response, 404, new { error = "not_found" }, cancellationToken);
        }
        catch (RequestTooLargeException)
        {
            await WriteAsync(context.Response, 413, new { error = "request_too_large" }, cancellationToken);
        }
        catch (JsonException)
        {
            await WriteAsync(context.Response, 400, new { error = "invalid_json" }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Response.Close();
        }
        catch (Exception ex)
        {
            // Do not expose exception details to a remote MCP client.
            await WriteAsync(context.Response, 500, new { error = "context_lookup_failed", type = ex.GetType().Name }, cancellationToken);
        }
    }

    private static async Task<WeChatContextResolveRequest?> ReadRequestAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var builder = new StringBuilder();
        var buffer = new char[1024];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (builder.Length + read > MaxRequestBodyLength)
                throw new RequestTooLargeException();
            builder.Append(buffer, 0, read);
        }

        return JsonSerializer.Deserialize<WeChatContextResolveRequest>(builder.ToString(), JsonOptions);
    }

    private bool IsAuthorized(HttpListenerRequest request)
        => (request.Headers["Authorization"] ?? string.Empty)
            .Equals($"Bearer {_token}", StringComparison.Ordinal);

    private static object ToPublicResponse(ConversationCompanyContext? context)
        => context is null
            ? new { matched = false }
            : new
            {
                matched = true,
                contactName = context.ContactName,
                companyName = context.CompanyName,
                projectName = context.ProjectName,
                projectTags = context.ProjectTags ?? [],
                matchMethod = context.MatchMethod
            };

    private static async Task WriteAsync(HttpListenerResponse response, int statusCode, object value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions));
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
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

    private sealed class RequestTooLargeException : Exception;
}

public sealed record WeChatContextResolveRequest(
    string AccountId,
    string WeChatUsername,
    string DisplayName,
    bool IsGroupChat = false);
