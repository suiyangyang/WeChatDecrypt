namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ImportConflictResolutionDecision
{
    public string Code { get; init; } = string.Empty;

    public string EntityType { get; init; } = string.Empty;

    public string EntityIdentifier { get; init; } = string.Empty;

    public ImportConflictResolutionAction Action { get; init; } = ImportConflictResolutionAction.Skip;
}
