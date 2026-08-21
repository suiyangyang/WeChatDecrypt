using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Core.Services;

public sealed class ProjectDirectoryService : IProjectDirectoryService
{
    private readonly IContactManagementStore _store;

    public ProjectDirectoryService(IContactManagementStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<ProjectProfile>> SearchProjectsAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return snapshot.Projects.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        return snapshot.Projects
            .Where(project =>
                project.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                project.Tags.Any(tag => tag.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
