namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ExportRequest
{
    public ExportTargetKind TargetKind { get; init; } = ExportTargetKind.Json;

    public string FilePath { get; init; } = string.Empty;
}
