namespace Lumine.Fairy.Services;

public static class LumineFairyAppDataPaths
{
    public static string GetAppRootDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "Lumine.Fairy");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, ".lumine-fairy");
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Lumine.Fairy"));
    }

    public static string GetAppDataDirectory() => Path.Combine(GetAppRootDirectory(), "AppData");

    public static string GetContactManagementDirectory() => Path.Combine(GetAppDataDirectory(), "contact-management");

    public static string GetMemoryDirectory() => Path.Combine(GetAppDataDirectory(), "memory");

    public static string GetMemoryDatabasePath() => Path.Combine(GetMemoryDirectory(), "memory.db");

    public static string GetChatHistoryDirectory() => Path.Combine(GetAppDataDirectory(), "chat-history");

    public static string GetChatHistoryDatabasePath() => Path.Combine(GetChatHistoryDirectory(), "chat.db");

    public static string GetLogsDirectory() => Path.Combine(GetAppDataDirectory(), "logs");

    public static string GetDraftAttachmentsDirectory() => Path.Combine(GetAppDataDirectory(), "attachments", "pasted-images");

    public static string GetConfigFilePath() => Path.Combine(GetAppDataDirectory(), "assistant-config.json");
}
