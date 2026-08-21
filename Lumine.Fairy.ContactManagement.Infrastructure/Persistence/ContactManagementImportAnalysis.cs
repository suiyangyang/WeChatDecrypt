using Lumine.Fairy.ContactManagement.Core.Models;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

internal static class ContactManagementImportAnalysis
{
    public static ImportPreviewResult BuildPreviewResult(
        ContactManagementSnapshot currentSnapshot,
        ContactManagementSnapshot previewSnapshot,
        ContactManagementValidationService validationService,
        IEnumerable<ImportConflictItem>? initialConflicts = null)
    {
        var conflicts = initialConflicts?.ToList() ?? [];

        try
        {
            validationService.Validate(ContactManagementDataSet.FromSnapshot(previewSnapshot));
        }
        catch (InvalidOperationException ex)
        {
            conflicts.Add(new ImportConflictItem
            {
                Code = "import.validation.failed",
                Message = ex.Message,
                Severity = ImportConflictSeverity.Error,
                EntityType = "Import",
                RecommendedAction = ImportConflictResolutionAction.Skip
            });
        }

        conflicts.AddRange(AnalyzeDuplicates(currentSnapshot, previewSnapshot));
        var statistics = validationService.BuildStatistics(previewSnapshot);
        var isValid = conflicts.All(static item => item.Severity != ImportConflictSeverity.Error);

        return new ImportPreviewResult
        {
            IsValid = isValid,
            Summary = isValid
                ? $"预检通过：联系人 {statistics.ContactCount}，公司 {statistics.CompanyCount}，项目 {statistics.ProjectCount}，关系 {statistics.RelationshipCount}，Todo {statistics.TodoCount}，客户问题 {statistics.ClientIssueCount}，日报 {statistics.DailyReportCount}，随手记 {statistics.ScratchNoteCount}。"
                : "预检未通过，请先处理错误项。",
            PreviewSnapshot = previewSnapshot,
            Statistics = statistics,
            Conflicts = conflicts
        };
    }

    public static ImportPreviewResult BuildInvalidResult(string summary, string code, string message)
    {
        return new ImportPreviewResult
        {
            IsValid = false,
            Summary = summary,
            Conflicts =
            [
                new ImportConflictItem
                {
                    Code = code,
                    Message = message,
                    Severity = ImportConflictSeverity.Error,
                    EntityType = "Import",
                    RecommendedAction = ImportConflictResolutionAction.Skip
                }
            ]
        };
    }

    private static IReadOnlyList<ImportConflictItem> AnalyzeDuplicates(ContactManagementSnapshot currentSnapshot, ContactManagementSnapshot previewSnapshot)
    {
        var conflicts = new List<ImportConflictItem>();

        foreach (var contact in previewSnapshot.Contacts)
        {
            var duplicate = currentSnapshot.Contacts.FirstOrDefault(existing =>
                string.Equals(existing.FullName, contact.FullName, StringComparison.OrdinalIgnoreCase)
                && (existing.Phones.Intersect(contact.Phones, StringComparer.OrdinalIgnoreCase).Any()
                    || existing.Emails.Intersect(contact.Emails, StringComparer.OrdinalIgnoreCase).Any()));

            if (duplicate is not null)
            {
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.contact.duplicate",
                    Message = $"联系人“{contact.FullName}”与现有联系人重复。",
                    Severity = ImportConflictSeverity.Warning,
                    EntityType = nameof(ContactProfile),
                    EntityIdentifier = contact.FullName,
                    RecommendedAction = ImportConflictResolutionAction.Merge
                });
            }
        }

        foreach (var company in previewSnapshot.Companies)
        {
            if (currentSnapshot.Companies.Any(existing => string.Equals(existing.Name, company.Name, StringComparison.OrdinalIgnoreCase)))
            {
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.company.duplicate",
                    Message = $"公司“{company.Name}”与现有公司重名。",
                    Severity = ImportConflictSeverity.Warning,
                    EntityType = nameof(CompanyProfile),
                    EntityIdentifier = company.Name,
                    RecommendedAction = ImportConflictResolutionAction.Merge
                });
            }
        }

        foreach (var project in previewSnapshot.Projects)
        {
            if (currentSnapshot.Projects.Any(existing =>
                string.Equals(existing.Name, project.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.CompanyId, project.CompanyId, StringComparison.OrdinalIgnoreCase)))
            {
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.project.duplicate",
                    Message = $"项目“{project.Name}”与现有项目重复。",
                    Severity = ImportConflictSeverity.Warning,
                    EntityType = nameof(ProjectProfile),
                    EntityIdentifier = project.Name,
                    RecommendedAction = ImportConflictResolutionAction.Merge
                });
            }
        }

        foreach (var link in previewSnapshot.RelationshipLinks)
        {
            if (currentSnapshot.RelationshipLinks.Any(existing =>
                string.Equals(existing.SourceEntityId, link.SourceEntityId, StringComparison.OrdinalIgnoreCase)
                && existing.SourceEntityKind == link.SourceEntityKind
                && string.Equals(existing.TargetEntityId, link.TargetEntityId, StringComparison.OrdinalIgnoreCase)
                && existing.TargetEntityKind == link.TargetEntityKind
                && existing.LinkType == link.LinkType
                && string.Equals(existing.DepartmentName, link.DepartmentName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Title, link.Title, StringComparison.OrdinalIgnoreCase)))
            {
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.relationship.duplicate",
                    Message = $"关系“{link.LinkType} / {link.SourceEntityKind}->{link.TargetEntityKind}”已存在。",
                    Severity = ImportConflictSeverity.Warning,
                    EntityType = nameof(RelationshipLink),
                    EntityIdentifier = link.Id,
                    RecommendedAction = ImportConflictResolutionAction.Skip
                });
            }
        }

        foreach (var todoEntry in previewSnapshot.TodoEntries)
        {
            if (currentSnapshot.TodoEntries.Any(existing => IsDuplicate(existing, todoEntry)))
            {
                var entityIdentifier = BuildTodoIdentifier(todoEntry);
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.todo.duplicate",
                    Message = $"Todo“{entityIdentifier}”与现有记录重复。",
                    Severity = ImportConflictSeverity.Warning,
                    EntityType = nameof(TodoEntry),
                    EntityIdentifier = entityIdentifier,
                    RecommendedAction = ImportConflictResolutionAction.Merge
                });
            }
        }

        foreach (var clientIssue in previewSnapshot.ClientIssues)
        {
            if (currentSnapshot.ClientIssues.Any(existing => IsDuplicate(existing, clientIssue)))
            {
                var entityIdentifier = BuildClientIssueIdentifier(clientIssue);
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.clientissue.duplicate",
                    Message = $"客户问题“{entityIdentifier}”与现有记录重复。",
                    Severity = ImportConflictSeverity.Warning,
                    EntityType = nameof(ClientIssueEntry),
                    EntityIdentifier = entityIdentifier,
                    RecommendedAction = ImportConflictResolutionAction.Merge
                });
            }
        }

        foreach (var dailyReport in previewSnapshot.DailyReports)
        {
            if (currentSnapshot.DailyReports.Any(existing => IsDuplicate(existing, dailyReport)))
            {
                var entityIdentifier = BuildDailyReportIdentifier(dailyReport);
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.dailyreport.duplicate",
                    Message = $"日报“{entityIdentifier}”与现有记录重复。",
                    Severity = ImportConflictSeverity.Warning,
                    EntityType = nameof(DailyReport),
                    EntityIdentifier = entityIdentifier,
                    RecommendedAction = ImportConflictResolutionAction.Merge
                });
            }
        }

        foreach (var scratchNoteDocument in previewSnapshot.ScratchNoteDocuments)
        {
            if (currentSnapshot.ScratchNoteDocuments.Any(existing => IsDuplicate(existing, scratchNoteDocument)))
            {
                var entityIdentifier = BuildScratchNoteIdentifier(scratchNoteDocument);
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.scratchnote.duplicate",
                    Message = $"随手记“{entityIdentifier}”与现有记录重复。",
                    Severity = ImportConflictSeverity.Warning,
                    EntityType = nameof(ScratchNoteDocument),
                    EntityIdentifier = entityIdentifier,
                    RecommendedAction = ImportConflictResolutionAction.Merge
                });
            }
        }

        return conflicts;
    }

    private static bool IsDuplicate(TodoEntry existing, TodoEntry incoming)
        => string.Equals(existing.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
            || (existing.WorkDate == incoming.WorkDate
                && string.Equals(existing.ProjectName, incoming.ProjectName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Title, incoming.Title, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Content, incoming.Content, StringComparison.OrdinalIgnoreCase)
                && existing.SortOrder == incoming.SortOrder);

    private static bool IsDuplicate(ClientIssueEntry existing, ClientIssueEntry incoming)
        => string.Equals(existing.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
            || (existing.WorkDate == incoming.WorkDate
                && string.Equals(existing.ProjectName, incoming.ProjectName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.ClientName, incoming.ClientName, StringComparison.OrdinalIgnoreCase));

    private static bool IsDuplicate(DailyReport existing, DailyReport incoming)
        => existing.Date == incoming.Date;

    private static bool IsDuplicate(ScratchNoteDocument existing, ScratchNoteDocument incoming)
        => string.Equals(existing.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)
            || existing.WorkDate == incoming.WorkDate;

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
