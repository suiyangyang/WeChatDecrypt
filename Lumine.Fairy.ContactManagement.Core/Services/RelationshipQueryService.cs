using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Core.Services;

public sealed class RelationshipQueryService : IRelationshipQueryService
{
    private readonly IContactManagementStore _store;

    public RelationshipQueryService(IContactManagementStore store)
    {
        _store = store;
    }

    public async Task<(CompanyProfile? Company, string DepartmentName)> GetPrimaryCompanyForContactAsync(string contactId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contactId))
        {
            return (null, string.Empty);
        }

        var snapshot = await _store.LoadAsync(cancellationToken);
        var contact = snapshot.Contacts.FirstOrDefault(x => string.Equals(x.Id, contactId.Trim(), StringComparison.OrdinalIgnoreCase));
        var directCompanyId = contact?.CompanyIds.FirstOrDefault(static id => !string.IsNullOrWhiteSpace(id));
        if (!string.IsNullOrWhiteSpace(directCompanyId))
        {
            var directCompany = snapshot.Companies.FirstOrDefault(x => string.Equals(x.Id, directCompanyId, StringComparison.OrdinalIgnoreCase));
            if (directCompany is not null)
            {
                return (directCompany, string.Empty);
            }
        }

        var link = snapshot.RelationshipLinks.FirstOrDefault(x =>
            x.SourceEntityKind == EntityKind.Contact &&
            x.TargetEntityKind == EntityKind.Company &&
            string.Equals(x.SourceEntityId, contactId.Trim(), StringComparison.OrdinalIgnoreCase) &&
            x.LinkType == RelationshipLinkType.WorksAt &&
            x.IsPrimary);

        if (link is null)
        {
            return (null, string.Empty);
        }

        var company = snapshot.Companies.FirstOrDefault(x => string.Equals(x.Id, link.TargetEntityId, StringComparison.OrdinalIgnoreCase));
        return (company, link.DepartmentName);
    }

    public async Task<IReadOnlyList<RelationshipLink>> GetLinksForEntityAsync(string entityId, EntityKind entityKind, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return [];
        }

        var snapshot = await _store.LoadAsync(cancellationToken);
        return snapshot.RelationshipLinks
            .Where(x =>
                (x.SourceEntityKind == entityKind && string.Equals(x.SourceEntityId, entityId.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (x.TargetEntityKind == entityKind && string.Equals(x.TargetEntityId, entityId.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task<IReadOnlyList<ProjectProfile>> GetProjectsForContactAsync(string contactId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contactId))
        {
            return [];
        }

        var snapshot = await _store.LoadAsync(cancellationToken);
        var contact = snapshot.Contacts.FirstOrDefault(x => string.Equals(x.Id, contactId.Trim(), StringComparison.OrdinalIgnoreCase));
        var relatedProjectIds = snapshot.RelationshipLinks
            .Where(x =>
                x.SourceEntityKind == EntityKind.Contact &&
                x.TargetEntityKind == EntityKind.Project &&
                string.Equals(x.SourceEntityId, contactId.Trim(), StringComparison.OrdinalIgnoreCase) &&
                (x.LinkType == RelationshipLinkType.OwnsProject || x.LinkType == RelationshipLinkType.ParticipatesInProject))
            .Select(x => x.TargetEntityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (contact is not null)
        {
            foreach (var projectId in contact.ProjectIds)
            {
                relatedProjectIds.Add(projectId);
            }
        }

        foreach (var project in snapshot.Projects.Where(project => string.Equals(project.OwnerContactId, contactId.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            relatedProjectIds.Add(project.Id);
        }

        return snapshot.Projects
            .Where(project => relatedProjectIds.Contains(project.Id))
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
