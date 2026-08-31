using System.Text.Json;

namespace Lumine.Fairy.Infrastructure;

/// <summary>
/// Local-only settings shared by the Fairy HTTP endpoint and the stdio MCP adapter.
/// </summary>
public sealed class WeChatContextSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 18766;
    public string Token { get; set; } = string.Empty;
    public double TimeoutSeconds { get; set; } = 10;
}

public static class WeChatContextSettingsLoader
{
    public const string FileName = "wechat-context.local.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static WeChatContextSettings Load(string? filePath = null)
    {
        var path = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(AppContext.BaseDirectory, FileName)
            : filePath;

        try
        {
            if (!File.Exists(path)) return new WeChatContextSettings();
            var settings = JsonSerializer.Deserialize<WeChatContextSettings>(File.ReadAllText(path), JsonOptions);
            return Normalize(settings ?? new WeChatContextSettings());
        }
        catch (IOException)
        {
            return new WeChatContextSettings();
        }
        catch (JsonException)
        {
            return new WeChatContextSettings();
        }
    }

    private static WeChatContextSettings Normalize(WeChatContextSettings settings)
    {
        settings.Host = string.IsNullOrWhiteSpace(settings.Host) ? "127.0.0.1" : settings.Host.Trim();
        settings.Port = settings.Port is >= 1 and <= 65535 ? settings.Port : 18766;
        settings.Token = settings.Token?.Trim() ?? string.Empty;
        settings.TimeoutSeconds = double.IsFinite(settings.TimeoutSeconds)
            ? Math.Clamp(settings.TimeoutSeconds, 1, 60)
            : 10;
        return settings;
    }
}
