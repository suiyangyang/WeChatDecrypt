using ClosedXML.Excel;
using Lumine.Fairy.ContactManagement.Models;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

internal static class ExcelContactManagementWorkbookMapper
{
    private const string ContactsSheetName = "Contacts";
    private const string CompaniesSheetName = "Companies";
    private const string ProjectsSheetName = "Projects";
    private const string RelationshipLinksSheetName = "RelationshipLinks";
    private const string TodoEntriesSheetName = "TodoEntries";
    private const string ClientIssuesSheetName = "ClientIssues";
    private const string DailyReportsSheetName = "DailyReports";
    private const string ScratchNoteDocumentsSheetName = "ScratchNoteDocuments";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static ContactManagementSnapshot ReadSnapshot(string filePath, out List<ImportConflictItem> conflicts)
    {
        conflicts = [];

        using var workbook = new XLWorkbook(filePath);
        var contactsSheet = GetRequiredSheet(workbook, ContactsSheetName, conflicts);
        var companiesSheet = GetRequiredSheet(workbook, CompaniesSheetName, conflicts);
        var projectsSheet = GetRequiredSheet(workbook, ProjectsSheetName, conflicts);
        var relationshipSheet = GetRequiredSheet(workbook, RelationshipLinksSheetName, conflicts);
        var todoEntriesSheet = GetRequiredSheet(workbook, TodoEntriesSheetName, conflicts);
        var clientIssuesSheet = GetRequiredSheet(workbook, ClientIssuesSheetName, conflicts);
        var dailyReportsSheet = GetRequiredSheet(workbook, DailyReportsSheetName, conflicts);
        var scratchNoteDocumentsSheet = GetRequiredSheet(workbook, ScratchNoteDocumentsSheetName, conflicts);

        if (conflicts.Any(static item => item.Severity == ImportConflictSeverity.Error))
        {
            return new ContactManagementSnapshot();
        }

        var contacts = ReadContacts(contactsSheet!, conflicts);
        var companies = ReadCompanies(companiesSheet!, conflicts);
        var contactById = contacts.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var companyById = companies.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var contactByName = contacts.ToDictionary(item => item.FullName, StringComparer.OrdinalIgnoreCase);
        var companyByName = companies.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        var projects = ReadProjects(projectsSheet!, contactById, contactByName, companyById, companyByName, conflicts);
        var projectById = projects.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var projectByName = projects.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

        var relationships = ReadRelationships(
            relationshipSheet!,
            contactById,
            contactByName,
            companyById,
            companyByName,
            projectById,
            projectByName,
            conflicts);

        var todoEntries = ReadTodoEntries(todoEntriesSheet!, conflicts);
        var clientIssues = ReadClientIssues(clientIssuesSheet!, conflicts);
        var dailyReports = ReadDailyReports(dailyReportsSheet!, conflicts);
        var scratchNoteDocuments = ReadScratchNoteDocuments(scratchNoteDocumentsSheet!, conflicts);

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

    public static void WriteSnapshot(string filePath, ContactManagementSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var workbook = new XLWorkbook();
        WriteContacts(workbook, snapshot.Contacts);
        WriteCompanies(workbook, snapshot.Companies);
        WriteProjects(workbook, snapshot.Projects, snapshot.Companies, snapshot.Contacts);
        WriteRelationships(workbook, snapshot.RelationshipLinks, snapshot.Contacts, snapshot.Companies, snapshot.Projects);
        WriteTodoEntries(workbook, snapshot.TodoEntries);
        WriteClientIssues(workbook, snapshot.ClientIssues);
        WriteDailyReports(workbook, snapshot.DailyReports);
        WriteScratchNoteDocuments(workbook, snapshot.ScratchNoteDocuments);
        workbook.SaveAs(filePath);
    }

    private static IXLWorksheet? GetRequiredSheet(XLWorkbook workbook, string sheetName, List<ImportConflictItem> conflicts)
    {
        if (workbook.TryGetWorksheet(sheetName, out var worksheet))
        {
            return worksheet;
        }

        conflicts.Add(new ImportConflictItem
        {
            Code = "import.excel.sheet.missing",
            Message = $"缺少工作表：{sheetName}。",
            Severity = ImportConflictSeverity.Error,
            EntityType = "Excel",
            EntityIdentifier = sheetName,
            RecommendedAction = ImportConflictResolutionAction.Skip
        });

        return null;
    }

    private static List<ContactProfile> ReadContacts(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["FullName"], ContactsSheetName, conflicts);
        if (headers.Count == 0)
        {
            return [];
        }

        var contacts = new List<ContactProfile>();
        foreach (var row in EnumerateDataRows(worksheet))
        {
            var fullName = GetString(row, headers, "FullName");
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            if (!TryParseEnum(GetString(row, headers, "RelationshipType"), ContactRelationshipType.Stranger, out ContactRelationshipType relationshipType))
            {
                conflicts.Add(CreateRowConflict(ContactsSheetName, row.RowNumber(), "联系人关系类型无效。", "import.excel.contact.relationship_type.invalid"));
                continue;
            }

            contacts.Add(new ContactProfile
            {
                Id = GetString(row, headers, "Id").NullIfWhiteSpace() ?? Guid.NewGuid().ToString("N"),
                FullName = fullName,
                Nicknames = SplitValues(GetString(row, headers, "Nicknames")),
                WeChatIds = SplitValues(GetString(row, headers, "WeChatIds")),
                Phones = SplitValues(GetString(row, headers, "Phones")),
                Emails = SplitValues(GetString(row, headers, "Emails")),
                CompanyIds = SplitValues(GetString(row, headers, "CompanyIds")),
                ProjectIds = SplitValues(GetString(row, headers, "ProjectIds")),
                RelationshipType = relationshipType,
                Notes = GetString(row, headers, "Notes"),
                IsActive = ParseBool(GetString(row, headers, "IsActive"), defaultValue: true)
            });
        }

        return contacts;
    }

    private static List<CompanyProfile> ReadCompanies(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["Name"], CompaniesSheetName, conflicts);
        if (headers.Count == 0)
        {
            return [];
        }

        var companies = new List<CompanyProfile>();
        foreach (var row in EnumerateDataRows(worksheet))
        {
            var name = GetString(row, headers, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            companies.Add(new CompanyProfile
            {
                Id = GetString(row, headers, "Id").NullIfWhiteSpace() ?? Guid.NewGuid().ToString("N"),
                Name = name,
                Addresses = SplitValues(GetString(row, headers, "Addresses")),
                Departments = SplitValues(GetString(row, headers, "Departments")),
                Notes = GetString(row, headers, "Notes"),
                IsActive = ParseBool(GetString(row, headers, "IsActive"), defaultValue: true)
            });
        }

        return companies;
    }

    private static List<ProjectProfile> ReadProjects(
        IXLWorksheet worksheet,
        IReadOnlyDictionary<string, ContactProfile> contactById,
        IReadOnlyDictionary<string, ContactProfile> contactByName,
        IReadOnlyDictionary<string, CompanyProfile> companyById,
        IReadOnlyDictionary<string, CompanyProfile> companyByName,
        List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["Name"], ProjectsSheetName, conflicts);
        if (headers.Count == 0)
        {
            return [];
        }

        var projects = new List<ProjectProfile>();
        foreach (var row in EnumerateDataRows(worksheet))
        {
            var name = GetString(row, headers, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var companyId = ResolveEntityId(
                GetString(row, headers, "CompanyId"),
                GetString(row, headers, "CompanyName"),
                companyById,
                companyByName,
                ProjectsSheetName,
                row.RowNumber(),
                "公司",
                conflicts);

            var ownerContactId = ResolveEntityId(
                GetString(row, headers, "OwnerContactId"),
                GetString(row, headers, "OwnerFullName"),
                contactById,
                contactByName,
                ProjectsSheetName,
                row.RowNumber(),
                "联系人",
                conflicts);

            projects.Add(new ProjectProfile
            {
                Id = GetString(row, headers, "Id").NullIfWhiteSpace() ?? Guid.NewGuid().ToString("N"),
                Name = name,
                CompanyId = companyId,
                OwnerContactId = ownerContactId,
                DeliveryDate = TryParseNullableDateOnly(GetString(row, headers, "DeliveryDate"), out var deliveryDate) ? deliveryDate : null,
                Tags = SplitValues(GetString(row, headers, "Tags")),
                GroupChats = SplitValues(GetString(row, headers, "GroupChats")),
                Notes = GetString(row, headers, "Notes"),
                IsActive = ParseBool(GetString(row, headers, "IsActive"), defaultValue: true)
            });
        }

        return projects;
    }

    private static List<RelationshipLink> ReadRelationships(
        IXLWorksheet worksheet,
        IReadOnlyDictionary<string, ContactProfile> contactById,
        IReadOnlyDictionary<string, ContactProfile> contactByName,
        IReadOnlyDictionary<string, CompanyProfile> companyById,
        IReadOnlyDictionary<string, CompanyProfile> companyByName,
        IReadOnlyDictionary<string, ProjectProfile> projectById,
        IReadOnlyDictionary<string, ProjectProfile> projectByName,
        List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["SourceKind", "SourceName", "TargetKind", "TargetName", "LinkType"], RelationshipLinksSheetName, conflicts);
        if (headers.Count == 0)
        {
            return [];
        }

        var relationships = new List<RelationshipLink>();
        foreach (var row in EnumerateDataRows(worksheet))
        {
            var sourceName = GetString(row, headers, "SourceName");
            var targetName = GetString(row, headers, "TargetName");
            if (string.IsNullOrWhiteSpace(sourceName) && string.IsNullOrWhiteSpace(targetName))
            {
                continue;
            }

            if (!TryParseEnum(GetString(row, headers, "SourceKind"), EntityKind.Contact, out EntityKind sourceKind)
                || !TryParseEnum(GetString(row, headers, "TargetKind"), EntityKind.Contact, out EntityKind targetKind)
                || !TryParseEnum(GetString(row, headers, "LinkType"), RelationshipLinkType.Knows, out RelationshipLinkType linkType))
            {
                conflicts.Add(CreateRowConflict(RelationshipLinksSheetName, row.RowNumber(), "关系行包含无效枚举值。", "import.excel.relationship.enum.invalid"));
                continue;
            }

            var sourceId = ResolveEntityIdByKind(GetString(row, headers, "SourceId"), sourceName, sourceKind, contactById, contactByName, companyById, companyByName, projectById, projectByName, row.RowNumber(), true, conflicts);
            var targetId = ResolveEntityIdByKind(GetString(row, headers, "TargetId"), targetName, targetKind, contactById, contactByName, companyById, companyByName, projectById, projectByName, row.RowNumber(), false, conflicts);

            relationships.Add(new RelationshipLink
            {
                Id = GetString(row, headers, "Id").NullIfWhiteSpace() ?? Guid.NewGuid().ToString("N"),
                SourceEntityId = sourceId,
                SourceEntityKind = sourceKind,
                TargetEntityId = targetId,
                TargetEntityKind = targetKind,
                LinkType = linkType,
                DepartmentName = GetString(row, headers, "DepartmentName"),
                Title = GetString(row, headers, "Title"),
                IsPrimary = ParseBool(GetString(row, headers, "IsPrimary"), defaultValue: false),
                Notes = GetString(row, headers, "Notes")
            });
        }

        return relationships;
    }

    private static List<TodoEntry> ReadTodoEntries(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["ProjectName"], TodoEntriesSheetName, conflicts);
        if (headers.Count == 0)
        {
            return [];
        }

        var items = new List<TodoEntry>();
        foreach (var row in EnumerateDataRows(worksheet))
        {
            var projectName = GetString(row, headers, "ProjectName");
            if (string.IsNullOrWhiteSpace(projectName))
            {
                continue;
            }

            if (!TryParseEnum(GetString(row, headers, "Status"), TodoStatus.NotStarted, out TodoStatus status))
            {
                conflicts.Add(CreateRowConflict(TodoEntriesSheetName, row.RowNumber(), "Todo 状态无效。", "import.excel.todo.status.invalid"));
                continue;
            }

            if (!TryParseNullableDecimal(GetString(row, headers, "WorkHours"), out var workHours))
            {
                conflicts.Add(CreateRowConflict(TodoEntriesSheetName, row.RowNumber(), "Todo 工时无效。", "import.excel.todo.workhours.invalid"));
                continue;
            }

            if (!TryParseNullableDateOnly(GetString(row, headers, "WorkDate"), out var workDate))
            {
                conflicts.Add(CreateRowConflict(TodoEntriesSheetName, row.RowNumber(), "Todo 工作日期无效。", "import.excel.todo.workdate.invalid"));
                continue;
            }

            items.Add(new TodoEntry
            {
                Id = GetString(row, headers, "Id").NullIfWhiteSpace() ?? Guid.NewGuid().ToString("N"),
                WorkDate = workDate,
                ProjectName = projectName,
                Title = GetString(row, headers, "Title"),
                Content = GetString(row, headers, "Content"),
                Status = status,
                WorkHours = workHours,
                IsDaily = ParseBool(GetString(row, headers, "IsDaily"), defaultValue: false),
                IsFromScratchNote = ParseBool(GetString(row, headers, "IsFromScratchNote"), defaultValue: false),
                SourceText = GetString(row, headers, "SourceText"),
                SortOrder = ParseInt(GetString(row, headers, "SortOrder"), 0),
                Notes = GetString(row, headers, "Notes")
            });
        }

        return items;
    }

    private static List<ClientIssueEntry> ReadClientIssues(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["ProjectName", "ClientName"], ClientIssuesSheetName, conflicts);
        if (headers.Count == 0)
        {
            return [];
        }

        var items = new List<ClientIssueEntry>();
        foreach (var row in EnumerateDataRows(worksheet))
        {
            var projectName = GetString(row, headers, "ProjectName");
            var clientName = GetString(row, headers, "ClientName");
            if (string.IsNullOrWhiteSpace(projectName) && string.IsNullOrWhiteSpace(clientName))
            {
                continue;
            }

            if (!TryParseNullableDateOnly(GetString(row, headers, "WorkDate"), out var workDate))
            {
                conflicts.Add(CreateRowConflict(ClientIssuesSheetName, row.RowNumber(), "客户问题工作日期无效。", "import.excel.clientissue.workdate.invalid"));
                continue;
            }

            items.Add(new ClientIssueEntry
            {
                Id = GetString(row, headers, "Id").NullIfWhiteSpace() ?? Guid.NewGuid().ToString("N"),
                WorkDate = workDate,
                ProjectName = projectName,
                ClientName = clientName,
                IssueLines = SplitValues(GetString(row, headers, "IssueLines")),
                SolutionLines = SplitValues(GetString(row, headers, "SolutionLines")),
                Notes = GetString(row, headers, "Notes")
            });
        }

        return items;
    }

    private static List<DailyReport> ReadDailyReports(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["Date"], DailyReportsSheetName, conflicts);
        if (headers.Count == 0)
        {
            return [];
        }

        var items = new List<DailyReport>();
        foreach (var row in EnumerateDataRows(worksheet))
        {
            var rawDate = GetString(row, headers, "Date");
            if (string.IsNullOrWhiteSpace(rawDate))
            {
                continue;
            }

            if (!TryParseDateOnly(rawDate, out var date))
            {
                conflicts.Add(CreateRowConflict(DailyReportsSheetName, row.RowNumber(), "日报日期无效。", "import.excel.dailyreport.date.invalid"));
                continue;
            }

            if (!TryParseNullableDecimal(GetString(row, headers, "TotalHours"), out var totalHours))
            {
                conflicts.Add(CreateRowConflict(DailyReportsSheetName, row.RowNumber(), "日报总工时无效。", "import.excel.dailyreport.totalhours.invalid"));
                continue;
            }

            if (!TryDeserializeJsonList(GetString(row, headers, "ItemsJson"), out List<TodoEntry> reportItems)
                || !TryDeserializeJsonList(GetString(row, headers, "ClientIssuesJson"), out List<ClientIssueEntry> reportIssues))
            {
                conflicts.Add(CreateRowConflict(DailyReportsSheetName, row.RowNumber(), "日报明细 JSON 无效。", "import.excel.dailyreport.json.invalid"));
                continue;
            }

            items.Add(new DailyReport
            {
                Date = date,
                Items = reportItems,
                ClientIssues = reportIssues,
                TotalHours = totalHours ?? 0m,
                RawScratchText = GetString(row, headers, "RawScratchText"),
                FormattedReportText = GetString(row, headers, "FormattedReportText")
            });
        }

        return items;
    }

    private static List<ScratchNoteDocument> ReadScratchNoteDocuments(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["Id"], ScratchNoteDocumentsSheetName, conflicts);
        if (headers.Count == 0)
        {
            return [];
        }

        var items = new List<ScratchNoteDocument>();
        foreach (var row in EnumerateDataRows(worksheet))
        {
            var id = GetString(row, headers, "Id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!TryParseNullableDateOnly(GetString(row, headers, "WorkDate"), out var workDate)
                || !TryParseDateTimeOffset(GetString(row, headers, "CreatedAtUtc"), out var createdAtUtc)
                || !TryParseDateTimeOffset(GetString(row, headers, "UpdatedAtUtc"), out var updatedAtUtc)
                || !TryParseNullableDateTimeOffset(GetString(row, headers, "LastArrangedAtUtc"), out var lastArrangedAtUtc))
            {
                conflicts.Add(CreateRowConflict(ScratchNoteDocumentsSheetName, row.RowNumber(), "随手记日期字段无效。", "import.excel.scratchnote.datetime.invalid"));
                continue;
            }

            items.Add(new ScratchNoteDocument
            {
                Id = id,
                WorkDate = workDate,
                Title = GetString(row, headers, "Title"),
                RawText = GetString(row, headers, "RawText"),
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = updatedAtUtc,
                LastArrangedAtUtc = lastArrangedAtUtc
            });
        }

        return items;
    }

    private static void WriteContacts(XLWorkbook workbook, IReadOnlyList<ContactProfile> contacts)
    {
        var worksheet = workbook.Worksheets.Add(ContactsSheetName);
        var headers = new[] { "Id", "FullName", "Nicknames", "WeChatIds", "Phones", "Emails", "CompanyIds", "ProjectIds", "RelationshipType", "Notes", "IsActive" };
        WriteHeaders(worksheet, headers);
        for (var i = 0; i < contacts.Count; i++)
        {
            var row = i + 2;
            var item = contacts[i];
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.FullName;
            worksheet.Cell(row, 3).Value = JoinValues(item.Nicknames);
            worksheet.Cell(row, 4).Value = JoinValues(item.WeChatIds);
            worksheet.Cell(row, 5).Value = JoinValues(item.Phones);
            worksheet.Cell(row, 6).Value = JoinValues(item.Emails);
            worksheet.Cell(row, 7).Value = JoinValues(item.CompanyIds);
            worksheet.Cell(row, 8).Value = JoinValues(item.ProjectIds);
            worksheet.Cell(row, 9).Value = item.RelationshipType.ToString();
            worksheet.Cell(row, 10).Value = item.Notes;
            worksheet.Cell(row, 11).Value = item.IsActive;
        }
        worksheet.Columns().AdjustToContents();
    }

    private static void WriteCompanies(XLWorkbook workbook, IReadOnlyList<CompanyProfile> companies)
    {
        var worksheet = workbook.Worksheets.Add(CompaniesSheetName);
        var headers = new[] { "Id", "Name", "Addresses", "Departments", "Notes", "IsActive" };
        WriteHeaders(worksheet, headers);
        for (var i = 0; i < companies.Count; i++)
        {
            var row = i + 2;
            var item = companies[i];
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.Name;
            worksheet.Cell(row, 3).Value = JoinValues(item.Addresses);
            worksheet.Cell(row, 4).Value = JoinValues(item.Departments);
            worksheet.Cell(row, 5).Value = item.Notes;
            worksheet.Cell(row, 6).Value = item.IsActive;
        }
        worksheet.Columns().AdjustToContents();
    }

    private static void WriteProjects(XLWorkbook workbook, IReadOnlyList<ProjectProfile> projects, IReadOnlyList<CompanyProfile> companies, IReadOnlyList<ContactProfile> contacts)
    {
        var companyNames = companies.ToDictionary(item => item.Id, item => item.Name, StringComparer.OrdinalIgnoreCase);
        var contactNames = contacts.ToDictionary(item => item.Id, item => item.FullName, StringComparer.OrdinalIgnoreCase);
        var worksheet = workbook.Worksheets.Add(ProjectsSheetName);
        var headers = new[] { "Id", "Name", "CompanyId", "CompanyName", "OwnerContactId", "OwnerFullName", "DeliveryDate", "Tags", "GroupChats", "Notes", "IsActive" };
        WriteHeaders(worksheet, headers);
        for (var i = 0; i < projects.Count; i++)
        {
            var row = i + 2;
            var item = projects[i];
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.Name;
            worksheet.Cell(row, 3).Value = item.CompanyId ?? string.Empty;
            worksheet.Cell(row, 4).Value = item.CompanyId is not null && companyNames.TryGetValue(item.CompanyId, out var companyName) ? companyName : string.Empty;
            worksheet.Cell(row, 5).Value = item.OwnerContactId ?? string.Empty;
            worksheet.Cell(row, 6).Value = item.OwnerContactId is not null && contactNames.TryGetValue(item.OwnerContactId, out var ownerName) ? ownerName : string.Empty;
            worksheet.Cell(row, 7).Value = item.DeliveryDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            worksheet.Cell(row, 8).Value = JoinValues(item.Tags);
            worksheet.Cell(row, 9).Value = JoinValues(item.GroupChats);
            worksheet.Cell(row, 10).Value = item.Notes;
            worksheet.Cell(row, 11).Value = item.IsActive;
        }
        worksheet.Columns().AdjustToContents();
    }

    private static void WriteRelationships(XLWorkbook workbook, IReadOnlyList<RelationshipLink> relationships, IReadOnlyList<ContactProfile> contacts, IReadOnlyList<CompanyProfile> companies, IReadOnlyList<ProjectProfile> projects)
    {
        var contactNames = contacts.ToDictionary(item => item.Id, item => item.FullName, StringComparer.OrdinalIgnoreCase);
        var companyNames = companies.ToDictionary(item => item.Id, item => item.Name, StringComparer.OrdinalIgnoreCase);
        var projectNames = projects.ToDictionary(item => item.Id, item => item.Name, StringComparer.OrdinalIgnoreCase);
        var worksheet = workbook.Worksheets.Add(RelationshipLinksSheetName);
        var headers = new[] { "Id", "SourceKind", "SourceId", "SourceName", "TargetKind", "TargetId", "TargetName", "LinkType", "DepartmentName", "Title", "IsPrimary", "Notes" };
        WriteHeaders(worksheet, headers);
        for (var i = 0; i < relationships.Count; i++)
        {
            var row = i + 2;
            var item = relationships[i];
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.SourceEntityKind.ToString();
            worksheet.Cell(row, 3).Value = item.SourceEntityId;
            worksheet.Cell(row, 4).Value = ResolveName(item.SourceEntityKind, item.SourceEntityId, contactNames, companyNames, projectNames);
            worksheet.Cell(row, 5).Value = item.TargetEntityKind.ToString();
            worksheet.Cell(row, 6).Value = item.TargetEntityId;
            worksheet.Cell(row, 7).Value = ResolveName(item.TargetEntityKind, item.TargetEntityId, contactNames, companyNames, projectNames);
            worksheet.Cell(row, 8).Value = item.LinkType.ToString();
            worksheet.Cell(row, 9).Value = item.DepartmentName;
            worksheet.Cell(row, 10).Value = item.Title;
            worksheet.Cell(row, 11).Value = item.IsPrimary;
            worksheet.Cell(row, 12).Value = item.Notes;
        }
        worksheet.Columns().AdjustToContents();
    }

    private static void WriteTodoEntries(XLWorkbook workbook, IReadOnlyList<TodoEntry> items)
    {
        var worksheet = workbook.Worksheets.Add(TodoEntriesSheetName);
        var headers = new[] { "Id", "WorkDate", "ProjectName", "Title", "Content", "Status", "WorkHours", "IsDaily", "IsFromScratchNote", "SourceText", "SortOrder", "Notes" };
        WriteHeaders(worksheet, headers);
        for (var i = 0; i < items.Count; i++)
        {
            var row = i + 2;
            var item = items[i];
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.WorkDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            worksheet.Cell(row, 3).Value = item.ProjectName;
            worksheet.Cell(row, 4).Value = item.Title;
            worksheet.Cell(row, 5).Value = item.Content;
            worksheet.Cell(row, 6).Value = item.Status.ToString();
            worksheet.Cell(row, 7).Value = item.WorkHours?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            worksheet.Cell(row, 8).Value = item.IsDaily;
            worksheet.Cell(row, 9).Value = item.IsFromScratchNote;
            worksheet.Cell(row, 10).Value = item.SourceText;
            worksheet.Cell(row, 11).Value = item.SortOrder;
            worksheet.Cell(row, 12).Value = item.Notes;
        }
        worksheet.Columns().AdjustToContents();
    }

    private static void WriteClientIssues(XLWorkbook workbook, IReadOnlyList<ClientIssueEntry> items)
    {
        var worksheet = workbook.Worksheets.Add(ClientIssuesSheetName);
        var headers = new[] { "Id", "WorkDate", "ProjectName", "ClientName", "IssueLines", "SolutionLines", "Notes" };
        WriteHeaders(worksheet, headers);
        for (var i = 0; i < items.Count; i++)
        {
            var row = i + 2;
            var item = items[i];
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.WorkDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            worksheet.Cell(row, 3).Value = item.ProjectName;
            worksheet.Cell(row, 4).Value = item.ClientName;
            worksheet.Cell(row, 5).Value = JoinValues(item.IssueLines);
            worksheet.Cell(row, 6).Value = JoinValues(item.SolutionLines);
            worksheet.Cell(row, 7).Value = item.Notes;
        }
        worksheet.Columns().AdjustToContents();
    }

    private static void WriteDailyReports(XLWorkbook workbook, IReadOnlyList<DailyReport> items)
    {
        var worksheet = workbook.Worksheets.Add(DailyReportsSheetName);
        var headers = new[] { "Date", "TotalHours", "RawScratchText", "FormattedReportText", "ItemsJson", "ClientIssuesJson" };
        WriteHeaders(worksheet, headers);
        for (var i = 0; i < items.Count; i++)
        {
            var row = i + 2;
            var item = items[i];
            worksheet.Cell(row, 1).Value = item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            worksheet.Cell(row, 2).Value = item.TotalHours.ToString(CultureInfo.InvariantCulture);
            worksheet.Cell(row, 3).Value = item.RawScratchText;
            worksheet.Cell(row, 4).Value = item.FormattedReportText;
            worksheet.Cell(row, 5).Value = JsonSerializer.Serialize(item.Items, JsonOptions);
            worksheet.Cell(row, 6).Value = JsonSerializer.Serialize(item.ClientIssues, JsonOptions);
        }
        worksheet.Columns().AdjustToContents();
    }

    private static void WriteScratchNoteDocuments(XLWorkbook workbook, IReadOnlyList<ScratchNoteDocument> items)
    {
        var worksheet = workbook.Worksheets.Add(ScratchNoteDocumentsSheetName);
        var headers = new[] { "Id", "WorkDate", "Title", "RawText", "CreatedAtUtc", "UpdatedAtUtc", "LastArrangedAtUtc" };
        WriteHeaders(worksheet, headers);
        for (var i = 0; i < items.Count; i++)
        {
            var row = i + 2;
            var item = items[i];
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.WorkDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            worksheet.Cell(row, 3).Value = item.Title;
            worksheet.Cell(row, 4).Value = item.RawText;
            worksheet.Cell(row, 5).Value = item.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            worksheet.Cell(row, 6).Value = item.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            worksheet.Cell(row, 7).Value = item.LastArrangedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
        }
        worksheet.Columns().AdjustToContents();
    }

    private static Dictionary<string, int> ReadHeaders(IXLWorksheet worksheet, IEnumerable<string> requiredHeaders, string sheetName, List<ImportConflictItem> conflicts)
    {
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = worksheet.Row(1);
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim();
            if (!string.IsNullOrWhiteSpace(header))
            {
                headers[header] = cell.Address.ColumnNumber;
            }
        }

        foreach (var requiredHeader in requiredHeaders)
        {
            if (!headers.ContainsKey(requiredHeader))
            {
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.excel.header.missing",
                    Message = $"工作表 {sheetName} 缺少列：{requiredHeader}。",
                    Severity = ImportConflictSeverity.Error,
                    EntityType = sheetName,
                    EntityIdentifier = requiredHeader,
                    RecommendedAction = ImportConflictResolutionAction.Skip
                });
            }
        }

        return headers;
    }

    private static IEnumerable<IXLRow> EnumerateDataRows(IXLWorksheet worksheet)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            yield return worksheet.Row(rowNumber);
        }
    }

    private static string GetString(IXLRow row, IReadOnlyDictionary<string, int> headers, string columnName)
    {
        return headers.TryGetValue(columnName, out var columnNumber)
            ? row.Cell(columnNumber).GetString().Trim()
            : string.Empty;
    }

    private static List<string> SplitValues(string value)
    {
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string JoinValues(IEnumerable<string> values)
    {
        return string.Join(";", values);
    }

    private static bool ParseBool(string value, bool defaultValue)
    {
        return bool.TryParse(value, out var result) ? result : defaultValue;
    }

    private static int ParseInt(string value, int defaultValue)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;

    private static bool TryParseNullableDecimal(string value, out decimal? result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            return true;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant))
        {
            result = invariant;
            return true;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var current))
        {
            result = current;
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryParseDateOnly(string value, out DateOnly result)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            return true;
        }

        return DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out result);
    }

    private static bool TryParseNullableDateOnly(string value, out DateOnly? result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            return true;
        }

        if (TryParseDateOnly(value, out var parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryParseDateTimeOffset(string value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result)
            || DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.RoundtripKind, out result);
    }

    private static bool TryParseNullableDateTimeOffset(string value, out DateTimeOffset? result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            return true;
        }

        if (TryParseDateTimeOffset(value, out var parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryDeserializeJsonList<TItem>(string value, out List<TItem> items)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            items = [];
            return true;
        }

        try
        {
            items = JsonSerializer.Deserialize<List<TItem>>(value, JsonOptions) ?? [];
            return true;
        }
        catch (JsonException)
        {
            items = [];
            return false;
        }
    }

    private static bool TryParseEnum<TEnum>(string value, TEnum defaultValue, out TEnum result)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = defaultValue;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out result);
    }

    private static string? ResolveEntityId<TModel>(
        string rawId,
        string rawName,
        IReadOnlyDictionary<string, TModel> itemsById,
        IReadOnlyDictionary<string, TModel> itemsByName,
        string sheetName,
        int rowNumber,
        string entityLabel,
        List<ImportConflictItem> conflicts)
    {
        if (!string.IsNullOrWhiteSpace(rawId) && itemsById.ContainsKey(rawId))
        {
            return rawId;
        }

        if (!string.IsNullOrWhiteSpace(rawName))
        {
            if (itemsByName.TryGetValue(rawName, out var item))
            {
                return item switch
                {
                    ContactProfile contact => contact.Id,
                    CompanyProfile company => company.Id,
                    ProjectProfile project => project.Id,
                    _ => null
                };
            }

            conflicts.Add(CreateRowConflict(sheetName, rowNumber, $"无法解析关联{entityLabel}：{rawName}。", "import.excel.reference.not_found"));
        }

        return null;
    }

    private static string ResolveEntityIdByKind(
        string rawId,
        string rawName,
        EntityKind entityKind,
        IReadOnlyDictionary<string, ContactProfile> contactById,
        IReadOnlyDictionary<string, ContactProfile> contactByName,
        IReadOnlyDictionary<string, CompanyProfile> companyById,
        IReadOnlyDictionary<string, CompanyProfile> companyByName,
        IReadOnlyDictionary<string, ProjectProfile> projectById,
        IReadOnlyDictionary<string, ProjectProfile> projectByName,
        int rowNumber,
        bool isSource,
        List<ImportConflictItem> conflicts)
    {
        var label = isSource ? "源实体" : "目标实体";
        return entityKind switch
        {
            EntityKind.Contact => ResolveEntityId(rawId, rawName, contactById, contactByName, RelationshipLinksSheetName, rowNumber, label, conflicts) ?? string.Empty,
            EntityKind.Company => ResolveEntityId(rawId, rawName, companyById, companyByName, RelationshipLinksSheetName, rowNumber, label, conflicts) ?? string.Empty,
            EntityKind.Project => ResolveEntityId(rawId, rawName, projectById, projectByName, RelationshipLinksSheetName, rowNumber, label, conflicts) ?? string.Empty,
            _ => string.Empty
        };
    }

    private static ImportConflictItem CreateRowConflict(string sheetName, int rowNumber, string message, string code)
    {
        return new ImportConflictItem
        {
            Code = code,
            Message = $"[{sheetName} 第 {rowNumber} 行] {message}",
            Severity = ImportConflictSeverity.Error,
            EntityType = sheetName,
            EntityIdentifier = rowNumber.ToString(),
            RecommendedAction = ImportConflictResolutionAction.Skip
        };
    }

    private static void WriteHeaders(IXLWorksheet worksheet, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
        }

        var range = worksheet.Range(1, 1, 1, headers.Count);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
    }

    private static string ResolveName(
        EntityKind entityKind,
        string entityId,
        IReadOnlyDictionary<string, string> contactNames,
        IReadOnlyDictionary<string, string> companyNames,
        IReadOnlyDictionary<string, string> projectNames)
    {
        return entityKind switch
        {
            EntityKind.Contact => contactNames.TryGetValue(entityId, out var contactName) ? contactName : string.Empty,
            EntityKind.Company => companyNames.TryGetValue(entityId, out var companyName) ? companyName : string.Empty,
            EntityKind.Project => projectNames.TryGetValue(entityId, out var projectName) ? projectName : string.Empty,
            _ => string.Empty
        };
    }

    private static string? NullIfWhiteSpace(this string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
