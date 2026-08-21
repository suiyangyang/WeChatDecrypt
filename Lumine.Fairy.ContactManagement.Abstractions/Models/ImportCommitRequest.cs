namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ImportCommitRequest
{
    public required ImportPreviewResult PreviewResult { get; init; }

    public IReadOnlyList<ImportConflictResolutionDecision> ConflictResolutions { get; init; } = [];
}
