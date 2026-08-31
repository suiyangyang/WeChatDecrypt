namespace Lumine.Fairy.ContactManagement.Infrastructure.Options;

public sealed class ContactManagementStorageOptions
{
    public string BaseDirectoryPath { get; set; } = GetDefaultBaseDirectoryPath();

    public string DatabaseFileName { get; set; } = "contact-management.db";

    public int SchemaVersion { get; set; } = 10;

    public string DatabaseFilePath => Path.Combine(BaseDirectoryPath, DatabaseFileName);

    public static string GetDefaultBaseDirectoryPath() => Lumine.Fairy.Services.LumineFairyAppDataPaths.GetContactManagementDirectory();
}
