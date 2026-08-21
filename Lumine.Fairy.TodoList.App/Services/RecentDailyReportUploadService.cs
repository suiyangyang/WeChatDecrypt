using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Models;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Lumine.Fairy.TodoList.App.Services;

public interface IRecentDailyReportUploadService
{
    Task<RecentDailyReportUploadResult> UploadAsync(
        IReadOnlyList<DailyReport> reports,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);
}

public sealed record RecentDailyReportUploadResult(
    string EmployeeName,
    string FileName,
    string RelativePath,
    string ServerBaseUrl,
    DateOnly StartDate,
    DateOnly EndDate,
    int UploadedReportCount);

public static class ReportMindDailyReportTextFormatter
{
    public static string BuildText(IReadOnlyList<DailyReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var formatter = new DailyReportFormatter();
        var sections = reports
            .OrderBy(static report => report.Date)
            .Select(report => BuildSection(report, formatter))
            .Where(static section => !string.IsNullOrWhiteSpace(section))
            .ToList();

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string BuildSection(DailyReport report, DailyReportFormatter formatter)
    {
        var reportText = ResolveReportText(report, formatter);
        if (string.IsNullOrWhiteSpace(reportText))
        {
            return string.Empty;
        }

        return $"{report.Date:M/d}{Environment.NewLine}{Environment.NewLine}{NormalizeLineEndings(reportText).Trim()}";
    }

    private static string ResolveReportText(DailyReport report, DailyReportFormatter formatter)
    {
        if (!string.IsNullOrWhiteSpace(report.FormattedReportText))
        {
            return report.FormattedReportText;
        }

        var dailyItems = report.Items
            .Where(static item => item.IsDaily)
            .OrderBy(static item => item.SortOrder)
            .ThenBy(static item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return dailyItems.Count == 0 ? string.Empty : formatter.Format(dailyItems);
    }

    private static string NormalizeLineEndings(string text)
    {
        return string.Join(
            Environment.NewLine,
            text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.None));
    }
}

public sealed class ReportMindRecentDailyReportUploadService : IRecentDailyReportUploadService
{
    public const string DefaultServerBaseUrl = "http://localhost:10545";

    private readonly HttpClient _httpClient;
    private readonly ITodoListSettingsService _settingsService;

    public ReportMindRecentDailyReportUploadService(ITodoListSettingsService? settingsService = null, HttpClient? httpClient = null)
    {
        _settingsService = settingsService ?? new JsonTodoListSettingsService();
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<RecentDailyReportUploadResult> UploadAsync(
        IReadOnlyList<DailyReport> reports,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reports);

        var uploadReports = reports
            .Where(static report => !string.IsNullOrWhiteSpace(report.FormattedReportText) || report.Items.Count > 0)
            .OrderBy(static report => report.Date)
            .ToList();
        if (uploadReports.Count == 0)
        {
            throw new InvalidOperationException("所选时间段内没有可上传的日报内容。");
        }

        var settings = _settingsService.Load();
        var loginResult = await ReportMindAuthentication.LoginAsync(
            _httpClient,
            settings.ReportMindServerBaseUrl,
            settings.ReportMindUserName,
            settings.ReportMindPassword,
            cancellationToken);
        var serverBaseUrl = loginResult.ServerBaseUrl;
        var employeeName = loginResult.UserName;
        var reportText = ReportMindDailyReportTextFormatter.BuildText(uploadReports);
        if (string.IsNullOrWhiteSpace(reportText))
        {
            throw new InvalidOperationException("所选时间段日报为空，暂时无法上传。");
        }

        var fileName = BuildTransportFileName(startDate, endDate);
        var bytes = Encoding.UTF8.GetBytes(reportText);

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "\"file\"",
            FileName = $"\"{fileName}\""
        };

        form.Add(fileContent);
        form.Add(new StringContent(employeeName, Encoding.UTF8), "employeeName");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(serverBaseUrl, UriKind.Absolute), "api/reports/upload"))
        {
            Content = form
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"上传失败（{(int)response.StatusCode} {response.StatusCode}）：{errorContent}");
        }

        var payload = await response.Content.ReadFromJsonAsync<UploadReportResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("ReportMind 返回了空响应。");

        return new RecentDailyReportUploadResult(
            employeeName,
            payload.FileName,
            payload.RelativePath,
            serverBaseUrl,
            startDate,
            endDate,
            uploadReports.Count);
    }

    private static string BuildTransportFileName(DateOnly startDate, DateOnly endDate)
        => $"daily-report_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.txt";

    private sealed record UploadReportResponse(string FileName, string FolderName, string RelativePath);
}
