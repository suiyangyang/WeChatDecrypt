using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.TodoList.App.Services;
using Lumine.Fairy.TodoList.App.ViewModels;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class TodoListSettingsServiceTests
{
    [Fact]
    public void TodoListSettingsWindowViewModel_TogglePasswordVisibility_UpdatesStateAndLabel()
    {
        var settingsService = new FakeTodoListSettingsService(new TodoListAppSettings
        {
            ReportMindUserName = "张三",
            ReportMindPassword = "test-password"
        });
        var sut = new TodoListSettingsWindowViewModel(settingsService, _ => { });

        Assert.False(sut.IsPasswordVisible);
        Assert.Equal("显示", sut.PasswordVisibilityButtonText);

        sut.TogglePasswordVisibilityCommand.Execute(null);

        Assert.True(sut.IsPasswordVisible);
        Assert.Equal("隐藏", sut.PasswordVisibilityButtonText);
    }

    [Fact]
    public async Task JsonTodoListSettingsService_SaveAndLoad_RoundTrips()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var settingsFilePath = Path.Combine(tempDirectory, "settings.json");
            var sut = new JsonTodoListSettingsService(settingsFilePath);

            await sut.SaveAsync(new TodoListAppSettings
            {
                ReportMindServerBaseUrl = "http://127.0.0.1:10545",
                ReportMindUserName = "张三",
                ReportMindPassword = "test-password"
            });

            var loaded = sut.Load();
            var savedJson = await File.ReadAllTextAsync(settingsFilePath);

            Assert.Equal("http://127.0.0.1:10545", loaded.ReportMindServerBaseUrl);
            Assert.Equal("张三", loaded.ReportMindUserName);
            Assert.Equal("test-password", loaded.ReportMindPassword);
            Assert.Contains("\"ReportMindPassword\": \"test-password\"", savedJson, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReportMindRecentDailyReportUploadService_UsesSavedSettings()
    {
        var handler = new FakeHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var settingsService = new FakeTodoListSettingsService(new TodoListAppSettings
        {
            ReportMindServerBaseUrl = "http://127.0.0.1:10545",
            ReportMindUserName = "张三",
            ReportMindPassword = "test-password"
        });
        var sut = new ReportMindRecentDailyReportUploadService(settingsService, httpClient);

        var result = await sut.UploadAsync(
        [
            new DailyReport
            {
                Date = new DateOnly(2026, 3, 24),
                FormattedReportText = "1、(Lumine.Fairy)整理上传设置,2;"
            }
        ],
        new DateOnly(2026, 3, 24),
        new DateOnly(2026, 3, 30));

        Assert.Equal("张三", result.EmployeeName);
        Assert.Equal("http://127.0.0.1:10545/", result.ServerBaseUrl);
        Assert.Equal("http://127.0.0.1:10545/api/auth/login", handler.LastLoginRequestUri?.AbsoluteUri);
        Assert.Equal("张三", handler.LastLoginUserName);
        Assert.Equal("test-password", handler.LastLoginPassword);
        Assert.NotNull(handler.LastUploadRequestUri);
        Assert.Equal("http://127.0.0.1:10545/api/reports/upload", handler.LastUploadRequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", handler.LastUploadAuthorizationScheme);
        Assert.Equal("test-token", handler.LastUploadAuthorizationParameter);
        Assert.Equal("text/plain", handler.LastUploadFileContentType);
        Assert.Contains("name=\"file\"", handler.LastUploadBody ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("filename=\"daily-report_20260324_20260330.txt\"", handler.LastUploadBody ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1、(Lumine.Fairy)整理上传设置,2;", handler.LastUploadBody ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("张三", handler.LastUploadBody ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportMindConnectionProbeService_ReturnsAuthenticatedUser()
    {
        var handler = new FakeHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var sut = new ReportMindConnectionProbeService(httpClient);

        var result = await sut.ProbeAsync("http://127.0.0.1:10545", "张三", "test-password");

        Assert.Equal("http://127.0.0.1:10545/", result.ServerBaseUrl);
        Assert.Equal("张三", result.UserName);
        Assert.Equal(3600, result.ExpiresIn);
    }

    [Fact]
    public async Task ReportMindConnectionProbeService_WhenCredentialsInvalid_ThrowsFriendlyError()
    {
        var handler = new FakeHttpMessageHandler
        {
            LoginStatusCode = HttpStatusCode.Unauthorized
        };
        var httpClient = new HttpClient(handler);
        var sut = new ReportMindConnectionProbeService(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ProbeAsync("http://127.0.0.1:10545", "李四", "wrong-password"));

        Assert.Equal("用户名或密码错误。", exception.Message);
    }

    private sealed class FakeTodoListSettingsService(TodoListAppSettings settings) : ITodoListSettingsService
    {
        public string SettingsFilePath => "fake-settings.json";

        public TodoListAppSettings Load() => settings;

        public Task SaveAsync(TodoListAppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode LoginStatusCode { get; init; } = HttpStatusCode.OK;

        public Uri? LastLoginRequestUri { get; private set; }

        public string? LastLoginUserName { get; private set; }

        public string? LastLoginPassword { get; private set; }

        public Uri? LastUploadRequestUri { get; private set; }

        public string? LastUploadAuthorizationScheme { get; private set; }

        public string? LastUploadAuthorizationParameter { get; private set; }

        public string? LastUploadFileContentType { get; private set; }

        public string? LastUploadBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/auth/login")
            {
                LastLoginRequestUri = request.RequestUri;
                var loginBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                using var loginJson = JsonDocument.Parse(loginBody);
                LastLoginUserName = loginJson.RootElement.GetProperty("name").GetString();
                LastLoginPassword = loginJson.RootElement.GetProperty("password").GetString();

                if (LoginStatusCode != HttpStatusCode.OK)
                {
                    return new HttpResponseMessage(LoginStatusCode)
                    {
                        Content = new StringContent("{\"error\":\"invalid_grant\",\"message\":\"用户名或密码错误。\"}", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true,\"accessToken\":\"test-token\",\"expiresIn\":3600,\"userName\":\"张三\",\"roles\":[],\"permissions\":[]}", Encoding.UTF8, "application/json")
                };
            }

            if (request.RequestUri?.AbsolutePath == "/api/reports/upload")
            {
                LastUploadRequestUri = request.RequestUri;
                LastUploadAuthorizationScheme = request.Headers.Authorization?.Scheme;
                LastUploadAuthorizationParameter = request.Headers.Authorization?.Parameter;
                LastUploadFileContentType = request.Content?.Headers?
                    .FirstOrDefault(static header => string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    .Value?
                    .FirstOrDefault();
                LastUploadBody = await request.Content!.ReadAsStringAsync(cancellationToken);

                if (request.Content is MultipartFormDataContent multipart)
                {
                    foreach (var part in multipart)
                    {
                        var disposition = part.Headers.ContentDisposition;
                        if (string.Equals(disposition?.Name?.Trim('"'), "file", StringComparison.OrdinalIgnoreCase))
                        {
                            LastUploadFileContentType = part.Headers.ContentType?.ToString();
                            break;
                        }
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"fileName\":\"week.txt\",\"folderName\":\"weekly\",\"relativePath\":\"WeeklyReports/week.txt\"}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
