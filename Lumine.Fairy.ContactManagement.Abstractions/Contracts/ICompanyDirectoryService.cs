using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Contracts;

public interface ICompanyDirectoryService
{
    Task<IReadOnlyList<CompanyProfile>> SearchCompaniesAsync(string keyword, CancellationToken cancellationToken = default);
}
