using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Core.Services;

public sealed class CompanyDirectoryService : ICompanyDirectoryService
{
    private readonly IContactManagementStore _store;

    public CompanyDirectoryService(IContactManagementStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<CompanyProfile>> SearchCompaniesAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return snapshot.Companies.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        return snapshot.Companies
            .Where(company =>
                company.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                company.Departments.Any(department => department.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                company.Addresses.Any(address => address.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
