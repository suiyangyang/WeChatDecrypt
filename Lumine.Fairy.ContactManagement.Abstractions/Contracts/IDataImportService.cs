using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Contracts;

public interface IDataImportService
{
    Task<ImportPreviewResult> PreviewAsync(ImportRequest request, CancellationToken cancellationToken = default);

    Task<ImportCommitResult> CommitAsync(ImportCommitRequest request, CancellationToken cancellationToken = default);
}
