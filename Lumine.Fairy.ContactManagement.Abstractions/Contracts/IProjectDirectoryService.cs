using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Contracts;

public interface IProjectDirectoryService
{
    Task<IReadOnlyList<ProjectProfile>> SearchProjectsAsync(string keyword, CancellationToken cancellationToken = default);
}
