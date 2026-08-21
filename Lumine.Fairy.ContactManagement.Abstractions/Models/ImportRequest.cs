namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ImportRequest
{
    public ImportSourceKind SourceKind { get; init; } = ImportSourceKind.Json;

    public string FilePath { get; init; } = string.Empty;
}
