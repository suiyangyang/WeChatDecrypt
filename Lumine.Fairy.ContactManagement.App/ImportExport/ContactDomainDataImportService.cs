using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Core.Models;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumine.Fairy.ContactManagement.App.ImportExport;

public sealed class ContactDomainDataImportService : IDataImportService
{
    private readonly IContactManagementStore _store;
    private readonly ContactManagementStorageOptions _options;
    private readonly ContactManagementValidationService _validationService;
    private readonly JsonSerializerOptions _jsonOptions;

    public ContactDomainDataImportService(
        IContactManagementStore store,
        ContactManagementStorageOptions options,
        ContactManagementValidationService? validationService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _validationService = validationService ?? new ContactManagementValidationService();
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task<ImportPreviewResult> PreviewAsync(ImportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceKind == ImportSourceKind.Excel)
        {
            return await PreviewExcelAsync(request, cancellationToken);
        }

        if (request.SourceKind != ImportSourceKind.Json)
        {
            return BuildInvalidResult("当前导入源不受支持。", "import.source.not_supported", "当前版本无法识别导入源类型。");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return BuildInvalidResult("导入文件不存在。", "import.file.not_found", "指定的 JSON 文件不存在。");
        }

        try
        {
            await using var stream = File.OpenRead(request.FilePath);
            var document = await JsonSerializer.DeserializeAsync<ContactDomainJsonExchangeDocument>(stream, _jsonOptions, cancellationToken);
            if (document is null)
            {
                return BuildInvalidResult("无法读取导入文件内容。", "import.file.empty", "JSON 内容为空或格式无效。");
            }

            List<ImportConflictItem> conflicts = [];
            if (document.SchemaVersion != _options.SchemaVersion)
            {
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.schema.mismatch",
                    Message = $"导入文件 schemaVersion={document.SchemaVersion}，当前系统 schemaVersion={_options.SchemaVersion}。",
                    Severity = ImportConflictSeverity.Error,
                    EntityType = "Import",
                    RecommendedAction = ImportConflictResolutionAction.Skip
                });
            }

            var previewSnapshot = document.ToSnapshot();
            var currentSnapshot = await _store.LoadAsync(cancellationToken);
            return BuildPreviewResult(currentSnapshot, previewSnapshot, conflicts);
        }
        catch (JsonException ex)
        {
            return BuildInvalidResult("JSON 解析失败。", "import.json.invalid", ex.Message);
        }
    }

    public async Task<ImportCommitResult> CommitAsync(ImportCommitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.PreviewResult.IsValid)
        {
            return new ImportCommitResult
            {
                Success = false,
                Summary = "导入预检未通过，未执行写入。"
            };
        }

        var currentSnapshot = await _store.LoadAsync(cancellationToken);
        var committedSnapshot = BuildCommittedSnapshot(
            currentSnapshot,
            request.PreviewResult.PreviewSnapshot,
            request.ConflictResolutions);

        await _store.SaveAsync(committedSnapshot, cancellationToken);
        var latestSnapshot = await _store.LoadAsync(cancellationToken);
        var skippedCount = request.ConflictResolutions.Count(static item => item.Action == ImportConflictResolutionAction.Skip);
        return new ImportCommitResult
        {
            Success = true,
            Summary = skippedCount > 0
                ? $"导入已完成，按确认结果跳过了 {skippedCount} 个冲突项。"
                : "导入已成功写入 SQLite。",
            Statistics = _validationService.BuildStatistics(ContactDomainSnapshotFilter.ToContactDomainSnapshot(latestSnapshot))
        };
    }

    private async Task<ImportPreviewResult> PreviewExcelAsync(ImportRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return BuildInvalidResult("导入文件不存在。", "import.file.not_found", "指定的 Excel 文件不存在。");
        }

        try
        {
            List<ImportConflictItem> conflicts = [];
            var previewSnapshot = await Task.Run(() => ContactDomainExcelWorkbookMapper.ReadSnapshot(request.FilePath, out conflicts), cancellationToken);
            var currentSnapshot = await _store.LoadAsync(cancellationToken);
            return BuildPreviewResult(currentSnapshot, previewSnapshot, conflicts);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return BuildInvalidResult("Excel 解析失败。", "import.excel.invalid", ex.Message);
        }
        catch (Exception ex)
        {
            return BuildInvalidResult("Excel 解析失败。", "import.excel.unhandled", ex.Message);
        }
    }

    private static ImportPreviewResult BuildInvalidResult(string summary, string code, string details)
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
                    Message = details,
                    Severity = ImportConflictSeverity.Error,
                    EntityType = "Import",
                    RecommendedAction = ImportConflictResolutionAction.Skip
                }
            ]
        };
    }

    private ImportPreviewResult BuildPreviewResult(
        ContactManagementSnapshot currentSnapshot,
        ContactManagementSnapshot previewSnapshot,
        IEnumerable<ImportConflictItem>? initialConflicts = null)
    {
        var conflicts = initialConflicts?.ToList() ?? [];

        try
        {
            _validationService.Validate(ContactManagementDataSet.FromSnapshot(previewSnapshot));
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
        var statistics = _validationService.BuildStatistics(ContactDomainSnapshotFilter.ToContactDomainSnapshot(previewSnapshot));
        var isValid = conflicts.All(static item => item.Severity != ImportConflictSeverity.Error);

        return new ImportPreviewResult
        {
            IsValid = isValid,
            Summary = isValid
                ? $"预检通过：联系人 {statistics.ContactCount}，公司 {statistics.CompanyCount}，项目 {statistics.ProjectCount}，关系 {statistics.RelationshipCount}。"
                : "预检未通过，请先处理错误项。",
            PreviewSnapshot = previewSnapshot,
            Statistics = statistics,
            Conflicts = conflicts
        };
    }

    private static IReadOnlyList<ImportConflictItem> AnalyzeDuplicates(ContactManagementSnapshot currentSnapshot, ContactManagementSnapshot previewSnapshot)
    {
        List<ImportConflictItem> conflicts = [];

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

        return conflicts;
    }

    private static ContactManagementSnapshot BuildCommittedSnapshot(
        ContactManagementSnapshot currentSnapshot,
        ContactManagementSnapshot importSnapshot,
        IReadOnlyList<ImportConflictResolutionDecision> resolutions)
    {
        var resolutionMap = resolutions.ToDictionary(CreateDecisionKey, static item => item.Action, StringComparer.OrdinalIgnoreCase);

        var contacts = currentSnapshot.Contacts.Select(Clone).ToList();
        foreach (var contact in importSnapshot.Contacts)
        {
            if (!ShouldSkipContact(contact, resolutionMap))
            {
                UpsertContact(contacts, contact);
            }
        }

        var companies = currentSnapshot.Companies.Select(Clone).ToList();
        foreach (var company in importSnapshot.Companies)
        {
            if (!ShouldSkipCompany(company, resolutionMap))
            {
                UpsertCompany(companies, company);
            }
        }

        var projects = currentSnapshot.Projects.Select(Clone).ToList();
        foreach (var project in importSnapshot.Projects)
        {
            if (!ShouldSkipProject(project, resolutionMap))
            {
                UpsertProject(projects, project);
            }
        }

        var relationships = currentSnapshot.RelationshipLinks.Select(Clone).ToList();
        foreach (var relationship in importSnapshot.RelationshipLinks)
        {
            if (!ShouldSkipRelationship(relationship, resolutionMap))
            {
                UpsertRelationship(relationships, relationship);
            }
        }

        return new ContactManagementSnapshot
        {
            Contacts = contacts,
            Companies = companies,
            Projects = projects,
            RelationshipLinks = relationships,
            TodoEntries = currentSnapshot.TodoEntries,
            ClientIssues = currentSnapshot.ClientIssues,
            DailyReports = currentSnapshot.DailyReports,
            ScratchNoteDocuments = currentSnapshot.ScratchNoteDocuments
        };
    }

    private static string CreateDecisionKey(ImportConflictResolutionDecision item)
        => $"{item.Code}|{item.EntityType}|{item.EntityIdentifier}";

    private static bool ShouldSkipContact(ContactProfile contact, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.contact.duplicate|{nameof(ContactProfile)}|{contact.FullName}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static bool ShouldSkipCompany(CompanyProfile company, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.company.duplicate|{nameof(CompanyProfile)}|{company.Name}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static bool ShouldSkipProject(ProjectProfile project, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.project.duplicate|{nameof(ProjectProfile)}|{project.Name}", out var action) && action == ImportConflictResolutionAction.Skip;

    private static bool ShouldSkipRelationship(RelationshipLink relationship, IReadOnlyDictionary<string, ImportConflictResolutionAction> resolutionMap)
        => resolutionMap.TryGetValue($"import.relationship.duplicate|{nameof(RelationshipLink)}|{relationship.Id}", out var action) && action == ImportConflictResolutionAction.Skip;

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
}
