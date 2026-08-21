using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

internal static class ContactManagementImportCommitter
{
    public static ContactManagementSnapshot BuildCommittedSnapshot(
        ContactManagementSnapshot currentSnapshot,
        ContactManagementSnapshot importSnapshot,
        IReadOnlyList<ImportConflictResolutionDecision> resolutions)
    {
        var resolutionMap = resolutions.ToDictionary(CreateDecisionKey, static item => item.Action, StringComparer.OrdinalIgnoreCase);

        var contacts = currentSnapshot.Contacts.Select(Clone).ToList();
        foreach (var contact in importSnapshot.Contacts)
        {
            if (ShouldSkipContact(contact, resolutionMap))
            {
                continue;
            }

            UpsertContact(contacts, contact);
        }

        var companies = currentSnapshot.Companies.Select(Clone).ToList();
        foreach (var company in importSnapshot.Companies)
        {
            if (ShouldSkipCompany(company, resolutionMap))
            {
                continue;
            }

            UpsertCompany(companies, company);
        }

        var projects = currentSnapshot.Projects.Select(Clone).ToList();
        foreach (var project in importSnapshot.Projects)
        {
            if (ShouldSkipProject(project, resolutionMap))
            {
                continue;
            }

            UpsertProject(projects, project);
        }

        var relationships = currentSnapshot.RelationshipLinks.Select(Clone).ToList();
        foreach (var relationship in importSnapshot.RelationshipLinks)
        {
            if (ShouldSkipRelationship(relationship, resolutionMap))
            {
                continue;
            }

            UpsertRelationship(relationships, relationship);
        }

        var todoEntries = currentSnapshot.TodoEntries.Select(Clone).ToList();
        foreach (var todoEntry in importSnapshot.TodoEntries)
        {
            if (ShouldSkipTodoEntry(todoEntry, resolutionMap))
            {
                continue;
            }

            UpsertTodoEntry(todoEntries, todoEntry);
        }

        var clientIssues = currentSnapshot.ClientIssues.Select(Clone).ToList();
        foreach (var clientIssue in importSnapshot.ClientIssues)
        {
            if (ShouldSkipClientIssue(clientIssue, resolutionMap))
            {
                continue;
            }

            UpsertClientIssue(clientIssues, clientIssue);
        }

        var dailyReports = currentSnapshot.DailyReports.Select(Clone).ToList();
        foreach (var dailyReport in importSnapshot.DailyReports)
        {
            if (ShouldSkipDailyReport(dailyReport, resolutionMap))
            {
                continue;
            }

            UpsertDailyReport(dailyReports, dailyReport);
        }

        var scratchNoteDocuments = currentSnapshot.ScratchNoteDocuments.Select(Clone).ToList();
        foreach (var scratchNoteDocument in importSnapshot.ScratchNoteDocuments)
        {
            if (ShouldSkipScratchNoteDocument(scratchNoteDocument, resolutionMap))
            {
                continue;
            }

            UpsertScratchNoteDocument(scratchNoteDocuments, scratchNoteDocument);
        }

        return new ContactManagementSnapshot
        {
            Contacts = contacts,
            Companies = companies,
            Projects = projects,
            RelationshipLinks = relationships,
            TodoEntries = todoEntries,
            ClientIssues = clientIssues,
            DailyReports = dailyReports,
            ScratchNoteDocuments = scratchNoteDocuments
        };
    }

    public static string CreateDecisionKey(ImportConflictResolutionDecision item)
        => $"{item.Code}|{item.EntityType}|{item.EntityIdentifier}";

    public static string CreateDecisionKey(ImportConflictItem item)
        => $"{item.Code}|{item.EntityType}|{item.EntityIdentifier}";

    private static bool ShouldSkipContact(ContactProfile contact, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.contact.duplicate|{nameof(ContactProfile)}|{contact.FullName}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static bool ShouldSkipCompany(CompanyProfile company, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.company.duplicate|{nameof(CompanyProfile)}|{company.Name}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static bool ShouldSkipProject(ProjectProfile project, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.project.duplicate|{nameof(ProjectProfile)}|{project.Name}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static bool ShouldSkipRelationship(RelationshipLink relationship, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.relationship.duplicate|{nameof(RelationshipLink)}|{relationship.Id}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static bool ShouldSkipTodoEntry(TodoEntry todoEntry, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.todo.duplicate|{nameof(TodoEntry)}|{BuildTodoIdentifier(todoEntry)}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static bool ShouldSkipClientIssue(ClientIssueEntry clientIssue, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.clientissue.duplicate|{nameof(ClientIssueEntry)}|{BuildClientIssueIdentifier(clientIssue)}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static bool ShouldSkipDailyReport(DailyReport dailyReport, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.dailyreport.duplicate|{nameof(DailyReport)}|{BuildDailyReportIdentifier(dailyReport)}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static bool ShouldSkipScratchNoteDocument(ScratchNoteDocument scratchNoteDocument, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.scratchnote.duplicate|{nameof(ScratchNoteDocument)}|{BuildScratchNoteIdentifier(scratchNoteDocument)}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static void UpsertContact(List<ContactProfile> items, ContactProfile incoming)
    {
        var index = items.FindIndex(existing =>
            string.Equals(existing.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(existing.FullName, incoming.FullName, StringComparison.OrdinalIgnoreCase)
                && (existing.Phones.Intersect(incoming.Phones, StringComparer.OrdinalIgnoreCase).Any()
                    || existing.Emails.Intersect(incoming.Emails, StringComparer.OrdinalIgnoreCase).Any())));

        if (index >= 0)
        {
            items[index] = Clone(incoming);
            return;
        }

        items.Add(Clone(incoming));
    }

    private static void UpsertCompany(List<CompanyProfile> items, CompanyProfile incoming)
    {
        var index = items.FindIndex(existing =>
            string.Equals(existing.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(existing.Name, incoming.Name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            items[index] = Clone(incoming);
            return;
        }

        items.Add(Clone(incoming));
    }

    private static void UpsertProject(List<ProjectProfile> items, ProjectProfile incoming)
    {
        var index = items.FindIndex(existing =>
            string.Equals(existing.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(existing.Name, incoming.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.CompanyId, incoming.CompanyId, StringComparison.OrdinalIgnoreCase)));

        if (index >= 0)
        {
            items[index] = Clone(incoming);
            return;
        }

        items.Add(Clone(incoming));
    }

    private static void UpsertRelationship(List<RelationshipLink> items, RelationshipLink incoming)
    {
        var index = items.FindIndex(existing =>
            string.Equals(existing.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(existing.SourceEntityId, incoming.SourceEntityId, StringComparison.OrdinalIgnoreCase)
                && existing.SourceEntityKind == incoming.SourceEntityKind
                && string.Equals(existing.TargetEntityId, incoming.TargetEntityId, StringComparison.OrdinalIgnoreCase)
                && existing.TargetEntityKind == incoming.TargetEntityKind
                && existing.LinkType == incoming.LinkType
                && string.Equals(existing.DepartmentName, incoming.DepartmentName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Title, incoming.Title, StringComparison.OrdinalIgnoreCase)));

        if (index >= 0)
        {
            items[index] = Clone(incoming);
            return;
        }

        items.Add(Clone(incoming));
    }

    private static void UpsertTodoEntry(List<TodoEntry> items, TodoEntry incoming)
    {
        var index = items.FindIndex(existing =>
            string.Equals(existing.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
            || (existing.WorkDate == incoming.WorkDate
                && string.Equals(existing.ProjectName, incoming.ProjectName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Title, incoming.Title, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Content, incoming.Content, StringComparison.OrdinalIgnoreCase)
                && existing.SortOrder == incoming.SortOrder));

        if (index >= 0)
        {
            items[index] = Clone(incoming);
            return;
        }

        items.Add(Clone(incoming));
    }

    private static void UpsertClientIssue(List<ClientIssueEntry> items, ClientIssueEntry incoming)
    {
        var index = items.FindIndex(existing =>
            string.Equals(existing.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
            || (existing.WorkDate == incoming.WorkDate
                && string.Equals(existing.ProjectName, incoming.ProjectName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.ClientName, incoming.ClientName, StringComparison.OrdinalIgnoreCase)));

        if (index >= 0)
        {
            items[index] = Clone(incoming);
            return;
        }

        items.Add(Clone(incoming));
    }

    private static void UpsertDailyReport(List<DailyReport> items, DailyReport incoming)
    {
        var index = items.FindIndex(existing => existing.Date == incoming.Date);
        if (index >= 0)
        {
            items[index] = Clone(incoming);
            return;
        }

        items.Add(Clone(incoming));
    }

    private static void UpsertScratchNoteDocument(List<ScratchNoteDocument> items, ScratchNoteDocument incoming)
    {
        var index = items.FindIndex(existing =>
            string.Equals(existing.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
            || existing.WorkDate == incoming.WorkDate);

        if (index >= 0)
        {
            items[index] = Clone(incoming);
            return;
        }

        items.Add(Clone(incoming));
    }

    private static ContactProfile Clone(ContactProfile item) => new()
    {
        Id = item.Id,
        FullName = item.FullName,
        Nicknames = [.. item.Nicknames],
        Phones = [.. item.Phones],
        Emails = [.. item.Emails],
        CompanyIds = [.. item.CompanyIds],
        ProjectIds = [.. item.ProjectIds],
        RelationshipType = item.RelationshipType,
        Notes = item.Notes,
        IsActive = item.IsActive
    };

    private static CompanyProfile Clone(CompanyProfile item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Addresses = [.. item.Addresses],
        Departments = [.. item.Departments],
        Notes = item.Notes,
        IsActive = item.IsActive
    };

    private static ProjectProfile Clone(ProjectProfile item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        CompanyId = item.CompanyId,
        OwnerContactId = item.OwnerContactId,
        DeliveryDate = item.DeliveryDate,
        Tags = [.. item.Tags],
        GroupChats = [.. item.GroupChats],
        Notes = item.Notes,
        IsActive = item.IsActive
    };

    private static RelationshipLink Clone(RelationshipLink item) => new()
    {
        Id = item.Id,
        SourceEntityId = item.SourceEntityId,
        SourceEntityKind = item.SourceEntityKind,
        TargetEntityId = item.TargetEntityId,
        TargetEntityKind = item.TargetEntityKind,
        LinkType = item.LinkType,
        DepartmentName = item.DepartmentName,
        Title = item.Title,
        IsPrimary = item.IsPrimary,
        Notes = item.Notes
    };

    private static TodoEntry Clone(TodoEntry item) => new()
    {
        Id = item.Id,
        WorkDate = item.WorkDate,
        ProjectName = item.ProjectName,
        Title = item.Title,
        Content = item.Content,
        Status = item.Status,
        WorkHours = item.WorkHours,
        IsDaily = item.IsDaily,
        IsFromScratchNote = item.IsFromScratchNote,
        SourceText = item.SourceText,
        SortOrder = item.SortOrder,
        Notes = item.Notes
    };

    private static ClientIssueEntry Clone(ClientIssueEntry item) => new()
    {
        Id = item.Id,
        WorkDate = item.WorkDate,
        ProjectName = item.ProjectName,
        ClientName = item.ClientName,
        IssueLines = [.. item.IssueLines],
        SolutionLines = [.. item.SolutionLines],
        Notes = item.Notes
    };

    private static DailyReport Clone(DailyReport item) => new()
    {
        Date = item.Date,
        Items = item.Items.Select(Clone).ToList(),
        ClientIssues = item.ClientIssues.Select(Clone).ToList(),
        TotalHours = item.TotalHours,
        RawScratchText = item.RawScratchText,
        FormattedReportText = item.FormattedReportText
    };

    private static ScratchNoteDocument Clone(ScratchNoteDocument item) => new()
    {
        Id = item.Id,
        WorkDate = item.WorkDate,
        Title = item.Title,
        RawText = item.RawText,
        CreatedAtUtc = item.CreatedAtUtc,
        UpdatedAtUtc = item.UpdatedAtUtc,
        LastArrangedAtUtc = item.LastArrangedAtUtc
    };

    private static string BuildTodoIdentifier(TodoEntry item)
        => $"{FormatDate(item.WorkDate)}|{item.ProjectName}|{FirstNonEmpty(item.Title, item.Content)}|#{item.SortOrder}";

    private static string BuildClientIssueIdentifier(ClientIssueEntry item)
        => $"{FormatDate(item.WorkDate)}|{item.ProjectName}|{item.ClientName}";

    private static string BuildDailyReportIdentifier(DailyReport item)
        => item.Date.ToString("yyyy-MM-dd");

    private static string BuildScratchNoteIdentifier(ScratchNoteDocument item)
        => FirstNonEmpty(item.Id, FormatDate(item.WorkDate), item.Title, "未命名随手记");

    private static string FormatDate(DateOnly? value)
        => value?.ToString("yyyy-MM-dd") ?? "未指定日期";

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
