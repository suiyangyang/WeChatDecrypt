using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Core.Services;

public sealed class ContactManagementWorkspaceService
{
    private readonly IContactManagementStore _store;
    private readonly ContactManagementValidationService _validationService;

    public ContactManagementWorkspaceService(IContactManagementStore store, ContactManagementValidationService? validationService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _validationService = validationService ?? new ContactManagementValidationService();
    }

    public Task<ContactManagementSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        => _store.LoadAsync(cancellationToken);

    public ContactManagementStatistics BuildStatistics(ContactManagementSnapshot snapshot)
        => _validationService.BuildStatistics(snapshot);

    public async Task<ContactManagementSnapshot> SaveSnapshotAsync(ContactManagementSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _store.SaveAsync(snapshot, cancellationToken);
        return await _store.LoadAsync(cancellationToken);
    }

    public async Task<ContactManagementSnapshot> UpsertContactAsync(ContactProfile contact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contact);

        var snapshot = await _store.LoadAsync(cancellationToken);
        EnsureEntitiesExist(snapshot.Companies, contact.CompanyIds, "所属公司");
        EnsureEntitiesExist(snapshot.Projects, contact.ProjectIds, "参与项目");

        var contacts = snapshot.Contacts.ToList();
        var index = contacts.FindIndex(x => string.Equals(x.Id, contact.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            contacts[index] = contact;
        }
        else
        {
            contacts.Add(contact);
        }

        return await SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts = contacts,
            Companies = snapshot.Companies,
            Projects = snapshot.Projects,
            WeChatGroupChats = snapshot.WeChatGroupChats,
            RelationshipLinks = RebuildContactAffiliationLinks(snapshot.RelationshipLinks, contact),
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        }, cancellationToken);
    }

    public async Task<ContactManagementSnapshot> DeleteContactAsync(string contactId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contactId))
        {
            return await _store.LoadAsync(cancellationToken);
        }

        var snapshot = await _store.LoadAsync(cancellationToken);
        var normalizedId = contactId.Trim();

        return await SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts = snapshot.Contacts.Where(x => !string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase)).ToList(),
            Companies = snapshot.Companies,
            Projects = snapshot.Projects
                .Select(project => string.Equals(project.OwnerContactId, normalizedId, StringComparison.OrdinalIgnoreCase)
                    ? new ProjectProfile
                    {
                        Id = project.Id,
                        Name = project.Name,
                        CompanyId = project.CompanyId,
                        OwnerContactId = null,
                        DeliveryDate = project.DeliveryDate,
                        Tags = [.. project.Tags],
                        GroupChats = [.. project.GroupChats],
                        Notes = project.Notes,
                        IsActive = project.IsActive
                    }
                    : project)
                .ToList(),
            WeChatGroupChats = snapshot.WeChatGroupChats,
            RelationshipLinks = snapshot.RelationshipLinks
                .Where(x => !(x.SourceEntityKind == EntityKind.Contact && string.Equals(x.SourceEntityId, normalizedId, StringComparison.OrdinalIgnoreCase))
                         && !(x.TargetEntityKind == EntityKind.Contact && string.Equals(x.TargetEntityId, normalizedId, StringComparison.OrdinalIgnoreCase)))
                .ToList(),
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        }, cancellationToken);
    }

    public async Task<ContactManagementSnapshot> UpsertCompanyAsync(CompanyProfile company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

        var snapshot = await _store.LoadAsync(cancellationToken);
        var companies = snapshot.Companies.ToList();
        var index = companies.FindIndex(x => string.Equals(x.Id, company.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            companies[index] = company;
        }
        else
        {
            companies.Add(company);
        }

        return await SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts = snapshot.Contacts,
            Companies = companies,
            Projects = snapshot.Projects,
            WeChatGroupChats = snapshot.WeChatGroupChats,
            RelationshipLinks = snapshot.RelationshipLinks,
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        }, cancellationToken);
    }

    public async Task<ContactManagementSnapshot> DeleteCompanyAsync(string companyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyId))
        {
            return await _store.LoadAsync(cancellationToken);
        }

        var snapshot = await _store.LoadAsync(cancellationToken);
        var normalizedId = companyId.Trim();

        return await SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts = snapshot.Contacts
                .Select(contact => new ContactProfile
                {
                    Id = contact.Id,
                    FullName = contact.FullName,
                    Nicknames = [.. contact.Nicknames],
                    WeChatIds = [.. contact.WeChatIds],
                    Phones = [.. contact.Phones],
                    Emails = [.. contact.Emails],
                    CompanyIds = contact.CompanyIds.Where(id => !string.Equals(id, normalizedId, StringComparison.OrdinalIgnoreCase)).ToList(),
                    ProjectIds = [.. contact.ProjectIds],
                    RelationshipType = contact.RelationshipType,
                    Notes = contact.Notes,
                    IsActive = contact.IsActive
                })
                .ToList(),
            Companies = snapshot.Companies.Where(x => !string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase)).ToList(),
            Projects = snapshot.Projects
                .Select(project => string.Equals(project.CompanyId, normalizedId, StringComparison.OrdinalIgnoreCase)
                    ? new ProjectProfile
                    {
                        Id = project.Id,
                        Name = project.Name,
                        CompanyId = null,
                        OwnerContactId = project.OwnerContactId,
                        DeliveryDate = project.DeliveryDate,
                        Tags = [.. project.Tags],
                        GroupChats = [.. project.GroupChats],
                        Notes = project.Notes,
                        IsActive = project.IsActive
                    }
                    : project)
                .ToList(),
            WeChatGroupChats = snapshot.WeChatGroupChats
                .Select(groupChat => string.Equals(groupChat.CompanyId, normalizedId, StringComparison.OrdinalIgnoreCase)
                    ? new WeChatGroupChatProfile
                    {
                        Id = groupChat.Id,
                        AccountId = groupChat.AccountId,
                        AccountDisplayName = groupChat.AccountDisplayName,
                        WeChatUsername = groupChat.WeChatUsername,
                        IsGroupChat = groupChat.IsGroupChat,
                        DisplayName = groupChat.DisplayName,
                        LastMessageAt = groupChat.LastMessageAt,
                        CompanyId = null,
                        ProjectId = groupChat.ProjectId,
                        IsActive = groupChat.IsActive,
                        LastSyncedAt = groupChat.LastSyncedAt
                    }
                    : groupChat)
                .ToList(),
            RelationshipLinks = snapshot.RelationshipLinks
                .Where(x => !(x.SourceEntityKind == EntityKind.Company && string.Equals(x.SourceEntityId, normalizedId, StringComparison.OrdinalIgnoreCase))
                         && !(x.TargetEntityKind == EntityKind.Company && string.Equals(x.TargetEntityId, normalizedId, StringComparison.OrdinalIgnoreCase)))
                .ToList(),
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        }, cancellationToken);
    }

    public async Task<ContactManagementSnapshot> UpsertProjectAsync(ProjectProfile project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var snapshot = await _store.LoadAsync(cancellationToken);
        EnsureEntityExists(snapshot.Companies, project.CompanyId, "公司");
        EnsureEntityExists(snapshot.Contacts, project.OwnerContactId, "负责人");

        var projects = snapshot.Projects.ToList();
        var index = projects.FindIndex(x => string.Equals(x.Id, project.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            projects[index] = project;
        }
        else
        {
            projects.Add(project);
        }

        return await SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts = snapshot.Contacts,
            Companies = snapshot.Companies,
            Projects = projects,
            WeChatGroupChats = snapshot.WeChatGroupChats,
            RelationshipLinks = RebuildProjectLinks(snapshot.RelationshipLinks, project),
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        }, cancellationToken);
    }

    public async Task<ContactManagementSnapshot> DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return await _store.LoadAsync(cancellationToken);
        }

        var snapshot = await _store.LoadAsync(cancellationToken);
        var normalizedId = projectId.Trim();

        return await SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts = snapshot.Contacts
                .Select(contact => new ContactProfile
                {
                    Id = contact.Id,
                    FullName = contact.FullName,
                    Nicknames = [.. contact.Nicknames],
                    WeChatIds = [.. contact.WeChatIds],
                    Phones = [.. contact.Phones],
                    Emails = [.. contact.Emails],
                    CompanyIds = [.. contact.CompanyIds],
                    ProjectIds = contact.ProjectIds.Where(id => !string.Equals(id, normalizedId, StringComparison.OrdinalIgnoreCase)).ToList(),
                    RelationshipType = contact.RelationshipType,
                    Notes = contact.Notes,
                    IsActive = contact.IsActive
                })
                .ToList(),
            Companies = snapshot.Companies,
            Projects = snapshot.Projects.Where(x => !string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase)).ToList(),
            WeChatGroupChats = snapshot.WeChatGroupChats
                .Select(groupChat => string.Equals(groupChat.ProjectId, normalizedId, StringComparison.OrdinalIgnoreCase)
                    ? new WeChatGroupChatProfile
                    {
                        Id = groupChat.Id,
                        AccountId = groupChat.AccountId,
                        AccountDisplayName = groupChat.AccountDisplayName,
                        WeChatUsername = groupChat.WeChatUsername,
                        IsGroupChat = groupChat.IsGroupChat,
                        DisplayName = groupChat.DisplayName,
                        LastMessageAt = groupChat.LastMessageAt,
                        CompanyId = groupChat.CompanyId,
                        ProjectId = null,
                        IsActive = groupChat.IsActive,
                        LastSyncedAt = groupChat.LastSyncedAt
                    }
                    : groupChat)
                .ToList(),
            RelationshipLinks = snapshot.RelationshipLinks
                .Where(x => !(x.SourceEntityKind == EntityKind.Project && string.Equals(x.SourceEntityId, normalizedId, StringComparison.OrdinalIgnoreCase))
                         && !(x.TargetEntityKind == EntityKind.Project && string.Equals(x.TargetEntityId, normalizedId, StringComparison.OrdinalIgnoreCase)))
                .ToList(),
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        }, cancellationToken);
    }

    public async Task<ContactManagementSnapshot> UpsertRelationshipLinkAsync(RelationshipLink relationshipLink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relationshipLink);

        var snapshot = await _store.LoadAsync(cancellationToken);
        EnsureEntityExists(snapshot, relationshipLink.SourceEntityId, relationshipLink.SourceEntityKind, "关系源");
        EnsureEntityExists(snapshot, relationshipLink.TargetEntityId, relationshipLink.TargetEntityKind, "关系目标");

        var links = snapshot.RelationshipLinks.ToList();
        var index = links.FindIndex(x => string.Equals(x.Id, relationshipLink.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            links[index] = relationshipLink;
        }
        else
        {
            links.Add(relationshipLink);
        }

        return await SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts = snapshot.Contacts,
            Companies = snapshot.Companies,
            Projects = snapshot.Projects,
            WeChatGroupChats = snapshot.WeChatGroupChats,
            RelationshipLinks = links,
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        }, cancellationToken);
    }

    public async Task<ContactManagementSnapshot> DeleteRelationshipLinkAsync(string relationshipLinkId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relationshipLinkId))
        {
            return await _store.LoadAsync(cancellationToken);
        }

        var snapshot = await _store.LoadAsync(cancellationToken);
        var normalizedId = relationshipLinkId.Trim();

        return await SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts = snapshot.Contacts,
            Companies = snapshot.Companies,
            Projects = snapshot.Projects,
            WeChatGroupChats = snapshot.WeChatGroupChats,
            RelationshipLinks = snapshot.RelationshipLinks.Where(x => !string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase)).ToList(),
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        }, cancellationToken);
    }

    private static void EnsureEntityExists<TModel>(IReadOnlyList<TModel> items, string? id, string label)
        where TModel : class
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var property = typeof(TModel).GetProperty("Id");
        if (property is null)
        {
            return;
        }

        var exists = items.Any(item => string.Equals(property.GetValue(item)?.ToString(), id.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            throw new InvalidOperationException($"选定的{label}不存在或已被删除。");
        }
    }

    private static void EnsureEntityExists(ContactManagementSnapshot snapshot, string entityId, EntityKind entityKind, string label)
    {
        var normalizedId = entityId.Trim();
        var exists = entityKind switch
        {
            EntityKind.Contact => snapshot.Contacts.Any(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase)),
            EntityKind.Company => snapshot.Companies.Any(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase)),
            EntityKind.Project => snapshot.Projects.Any(x => string.Equals(x.Id, normalizedId, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };

        if (!exists)
        {
            throw new InvalidOperationException($"选定的{label}不存在或已被删除。");
        }
    }

    private static void EnsureEntitiesExist<TModel>(IReadOnlyList<TModel> items, IEnumerable<string> ids, string label)
        where TModel : class
    {
        foreach (var id in ids)
        {
            EnsureEntityExists(items, id, label);
        }
    }

    private static List<RelationshipLink> RebuildContactAffiliationLinks(IReadOnlyList<RelationshipLink> existingLinks, ContactProfile contact)
    {
        var links = existingLinks
            .Where(link => !IsDerivedContactAffiliationLink(link, contact.Id))
            .ToList();

        var companyIds = contact.CompanyIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var index = 0; index < companyIds.Count; index++)
        {
            links.Add(new RelationshipLink
            {
                Id = BuildDerivedLinkId("works-at", contact.Id, companyIds[index]),
                SourceEntityId = contact.Id,
                SourceEntityKind = EntityKind.Contact,
                TargetEntityId = companyIds[index],
                TargetEntityKind = EntityKind.Company,
                LinkType = RelationshipLinkType.WorksAt,
                IsPrimary = index == 0
            });
        }

        foreach (var projectId in contact.ProjectIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            links.Add(new RelationshipLink
            {
                Id = BuildDerivedLinkId("participates", contact.Id, projectId),
                SourceEntityId = contact.Id,
                SourceEntityKind = EntityKind.Contact,
                TargetEntityId = projectId,
                TargetEntityKind = EntityKind.Project,
                LinkType = RelationshipLinkType.ParticipatesInProject
            });
        }

        return links;
    }

    private static List<RelationshipLink> RebuildProjectLinks(IReadOnlyList<RelationshipLink> existingLinks, ProjectProfile project)
    {
        var links = existingLinks
            .Where(link => !IsDerivedProjectLink(link, project.Id))
            .ToList();

        if (!string.IsNullOrWhiteSpace(project.CompanyId))
        {
            links.Add(new RelationshipLink
            {
                Id = BuildDerivedLinkId("project-company", project.Id, project.CompanyId),
                SourceEntityId = project.Id,
                SourceEntityKind = EntityKind.Project,
                TargetEntityId = project.CompanyId,
                TargetEntityKind = EntityKind.Company,
                LinkType = RelationshipLinkType.ProjectBelongsToCompany,
                IsPrimary = true
            });
        }

        if (!string.IsNullOrWhiteSpace(project.OwnerContactId))
        {
            links.Add(new RelationshipLink
            {
                Id = BuildDerivedLinkId("owns-project", project.OwnerContactId, project.Id),
                SourceEntityId = project.OwnerContactId,
                SourceEntityKind = EntityKind.Contact,
                TargetEntityId = project.Id,
                TargetEntityKind = EntityKind.Project,
                LinkType = RelationshipLinkType.OwnsProject,
                IsPrimary = true
            });
        }

        return links;
    }

    private static bool IsDerivedContactAffiliationLink(RelationshipLink link, string contactId)
        => link.SourceEntityKind == EntityKind.Contact
           && string.Equals(link.SourceEntityId, contactId, StringComparison.OrdinalIgnoreCase)
           && ((link.TargetEntityKind == EntityKind.Company && link.LinkType == RelationshipLinkType.WorksAt)
               || (link.TargetEntityKind == EntityKind.Project && link.LinkType == RelationshipLinkType.ParticipatesInProject));

    private static bool IsDerivedProjectLink(RelationshipLink link, string projectId)
        => (link.SourceEntityKind == EntityKind.Project
            && string.Equals(link.SourceEntityId, projectId, StringComparison.OrdinalIgnoreCase)
            && link.TargetEntityKind == EntityKind.Company
            && link.LinkType == RelationshipLinkType.ProjectBelongsToCompany)
           || (link.TargetEntityKind == EntityKind.Project
               && string.Equals(link.TargetEntityId, projectId, StringComparison.OrdinalIgnoreCase)
               && link.SourceEntityKind == EntityKind.Contact
               && link.LinkType == RelationshipLinkType.OwnsProject);

    private static string BuildDerivedLinkId(string prefix, string sourceId, string targetId)
        => $"{prefix}-{sourceId.Trim().ToLowerInvariant()}-{targetId.Trim().ToLowerInvariant()}";
}
