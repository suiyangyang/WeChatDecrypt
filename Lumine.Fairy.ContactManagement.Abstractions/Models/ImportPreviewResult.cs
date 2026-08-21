namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ImportPreviewResult
{
    public bool IsValid { get; init; }

    public string Summary { get; init; } = string.Empty;

    public ContactManagementSnapshot PreviewSnapshot { get; init; } = new();

    public ContactManagementStatistics Statistics { get; init; } = new();

    public IReadOnlyList<ImportConflictItem> Conflicts { get; init; } = [];
}
