namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ImportConflictItem
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public ImportConflictSeverity Severity { get; init; } = ImportConflictSeverity.Warning;

    public string EntityType { get; init; } = string.Empty;

    public string EntityIdentifier { get; init; } = string.Empty;

    public ImportConflictResolutionAction RecommendedAction { get; init; } = ImportConflictResolutionAction.Skip;
}
