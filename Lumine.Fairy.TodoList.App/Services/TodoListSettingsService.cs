using System.Text.Json;

namespace Lumine.Fairy.TodoList.App.Services;

public sealed class TodoListAppSettings
{
    public string ReportMindServerBaseUrl { get; set; } = ReportMindRecentDailyReportUploadService.DefaultServerBaseUrl;

    public string ReportMindUserName { get; set; } = string.Empty;

    public string ReportMindPassword { get; set; } = string.Empty;
}

public interface ITodoListSettingsService
{
    string SettingsFilePath { get; }

    TodoListAppSettings Load();

    Task SaveAsync(TodoListAppSettings settings, CancellationToken cancellationToken = default);
}

public sealed class JsonTodoListSettingsService : ITodoListSettingsService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonTodoListSettingsService(string? settingsFilePath = null)
    {
        SettingsFilePath = string.IsNullOrWhiteSpace(settingsFilePath)
            ? GetDefaultSettingsFilePath()
            : settingsFilePath;
    }

    public string SettingsFilePath { get; }

    public TodoListAppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new TodoListAppSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<TodoListAppSettings>(json, JsonSerializerOptions);
            return Normalize(settings ?? new TodoListAppSettings());
        }
        catch
        {
            return new TodoListAppSettings();
        }
    }

    public async Task SaveAsync(TodoListAppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = Normalize(settings);
        var directoryPath = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var json = JsonSerializer.Serialize(normalized, JsonSerializerOptions);
        await File.WriteAllTextAsync(SettingsFilePath, json, cancellationToken);
    }

    private static TodoListAppSettings Normalize(TodoListAppSettings settings)
    {
        return new TodoListAppSettings
        {
            ReportMindServerBaseUrl = string.IsNullOrWhiteSpace(settings.ReportMindServerBaseUrl)
                ? ReportMindRecentDailyReportUploadService.DefaultServerBaseUrl
                : settings.ReportMindServerBaseUrl.Trim(),
            ReportMindUserName = settings.ReportMindUserName?.Trim() ?? string.Empty,
            ReportMindPassword = settings.ReportMindPassword ?? string.Empty
        };
    }

    private static string GetDefaultSettingsFilePath()
    {
        return Path.Combine(Lumine.Fairy.Services.LumineFairyAppDataPaths.GetAppDataDirectory(), "todolist", "settings.json");
    }
}
