using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Contracts;

public interface IDataExportService
{
    Task ExportJsonAsync(ExportRequest request, CancellationToken cancellationToken = default);

    Task ExportExcelAsync(ExportRequest request, CancellationToken cancellationToken = default);
}
