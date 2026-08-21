namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ImportCommitResult
{
    public bool Success { get; init; }

    public string Summary { get; init; } = string.Empty;

    public ContactManagementStatistics Statistics { get; init; } = new();
}
