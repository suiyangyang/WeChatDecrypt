using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Contracts;

public interface IRelationshipQueryService
{
    Task<IReadOnlyList<RelationshipLink>> GetLinksForEntityAsync(string entityId, EntityKind entityKind, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectProfile>> GetProjectsForContactAsync(string contactId, CancellationToken cancellationToken = default);

    Task<(CompanyProfile? Company, string DepartmentName)> GetPrimaryCompanyForContactAsync(string contactId, CancellationToken cancellationToken = default);
}
