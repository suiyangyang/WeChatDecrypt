using System.Text.Json;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Infrastructure;

public sealed class AppSettings
{
    public string TimeZoneId { get; set; } = TimeZoneContext.DefaultTimeZoneId;
    public string? WeChatAnalysisAgentId { get; set; }
    public WeChatAnalysisSettings WeChatAnalysis { get; set; } = new();

    public AppSettings() { }

    public AppSettings(string timeZoneId)
    {
        TimeZoneId = timeZoneId;
    }
}

public sealed class AppSettingsStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public AppSettingsStore(string? path = null)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumine.Fairy");
        Directory.CreateDirectory(root);
        _path = path ?? Path.Combine(root, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), _json);
                if (settings is not null && !string.IsNullOrWhiteSpace(settings.TimeZoneId))
                {
                    settings.WeChatAnalysis ??= new WeChatAnalysisSettings();
                    return settings;
                }
            }
        }
        catch (IOException) { }
        catch (JsonException) { }

        return new AppSettings(TimeZoneContext.DefaultTimeZoneId);
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, _json));
    }
}
