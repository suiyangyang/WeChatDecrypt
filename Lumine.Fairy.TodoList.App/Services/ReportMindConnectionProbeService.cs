using System.Net.Http.Json;
using System.Text.Json;

namespace Lumine.Fairy.TodoList.App.Services;

public interface IReportMindConnectionProbeService
{
    Task<ReportMindConnectionProbeResult> ProbeAsync(
        string serverBaseUrl,
        string userName,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record ReportMindConnectionProbeResult(
    string ServerBaseUrl,
    string UserName,
    int ExpiresIn);

public sealed record ReportMindLoginResult(
    string ServerBaseUrl,
    string AccessToken,
    int ExpiresIn,
    string UserName);

public static class ReportMindAuthentication
{
    public static async Task<ReportMindLoginResult> LoginAsync(
        HttpClient httpClient,
        string serverBaseUrl,
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var normalizedServerBaseUrl = NormalizeServerBaseUrl(serverBaseUrl);
        var normalizedUserName = userName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedUserName))
        {
            throw new InvalidOperationException("请先填写 ReportMind 用户名。");
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("请先填写 ReportMind 密码。");
        }

        var loginUri = new Uri(new Uri(normalizedServerBaseUrl, UriKind.Absolute), "api/auth/login");
        using var response = await httpClient.PostAsJsonAsync(
            loginUri,
            new LoginRequest(normalizedUserName, password),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var errorMessage = TryReadErrorMessage(errorContent);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException(errorMessage ?? "用户名或密码错误。");
            }

            throw new InvalidOperationException(
                $"登录失败（{(int)response.StatusCode} {response.StatusCode}）：{errorMessage ?? errorContent}");
        }

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("ReportMind 登录接口返回了空响应。");
        if (!payload.Success || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw new InvalidOperationException("ReportMind 登录接口未返回有效 token。");
        }

        return new ReportMindLoginResult(
            normalizedServerBaseUrl,
            payload.AccessToken,
            payload.ExpiresIn,
            string.IsNullOrWhiteSpace(payload.UserName) ? normalizedUserName : payload.UserName.Trim());
    }

    public static string NormalizeServerBaseUrl(string serverBaseUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(serverBaseUrl)
            ? ReportMindRecentDailyReportUploadService.DefaultServerBaseUrl
            : serverBaseUrl.Trim();

        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }

    private static string? TryReadErrorMessage(string errorContent)
    {
        if (string.IsNullOrWhiteSpace(errorContent))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(errorContent);
            return document.RootElement.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record LoginRequest(string Name, string Password);

    private sealed record LoginResponse(
        bool Success,
        string AccessToken,
        int ExpiresIn,
        string? UserName);
}

public sealed class ReportMindConnectionProbeService : IReportMindConnectionProbeService
{
    private readonly HttpClient _httpClient;

    public ReportMindConnectionProbeService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ReportMindConnectionProbeResult> ProbeAsync(
        string serverBaseUrl,
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var loginResult = await ReportMindAuthentication.LoginAsync(
            _httpClient,
            serverBaseUrl,
            userName,
            password,
            cancellationToken);

        return new ReportMindConnectionProbeResult(
            loginResult.ServerBaseUrl,
            loginResult.UserName,
            loginResult.ExpiresIn);
    }
}
