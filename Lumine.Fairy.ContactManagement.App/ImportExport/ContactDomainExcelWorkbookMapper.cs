using ClosedXML.Excel;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.ImportExport;

internal static class ContactDomainExcelWorkbookMapper
{
    private const string ContactsSheetName = "Contacts";
    private const string CompaniesSheetName = "Companies";
    private const string ProjectsSheetName = "Projects";
    private const string WeChatGroupChatsSheetName = "WeChatGroupChats";
    private const string RelationshipLinksSheetName = "RelationshipLinks";

    private static readonly string[] UnsupportedSheetNames = ["TodoEntries", "ClientIssues", "DailyReports", "ScratchNoteDocuments"];

    public static ContactManagementSnapshot ReadSnapshot(string filePath, out List<ImportConflictItem> conflicts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        conflicts = [];

        using var workbook = new XLWorkbook(filePath);
        ValidateUnsupportedSheets(workbook, conflicts);

        var contactsSheet = GetRequiredSheet(workbook, ContactsSheetName, conflicts);
        var companiesSheet = GetRequiredSheet(workbook, CompaniesSheetName, conflicts);
        var projectsSheet = GetRequiredSheet(workbook, ProjectsSheetName, conflicts);
        var groupChatsSheet = GetRequiredSheet(workbook, WeChatGroupChatsSheetName, conflicts);
        var relationshipsSheet = GetRequiredSheet(workbook, RelationshipLinksSheetName, conflicts);

        if (contactsSheet is null || companiesSheet is null || projectsSheet is null || groupChatsSheet is null || relationshipsSheet is null)
        {
            return new ContactManagementSnapshot();
        }

        return new ContactManagementSnapshot
        {
            Contacts = ReadContacts(contactsSheet, conflicts),
            Companies = ReadCompanies(companiesSheet, conflicts),
            Projects = ReadProjects(projectsSheet, conflicts),
            WeChatGroupChats = ReadWeChatGroupChats(groupChatsSheet, conflicts),
            RelationshipLinks = ReadRelationshipLinks(relationshipsSheet, conflicts)
        };
    }

    public static void WriteSnapshot(string filePath, ContactManagementSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(snapshot);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var workbook = new XLWorkbook();
        WriteContacts(workbook, snapshot.Contacts);
        WriteCompanies(workbook, snapshot.Companies);
        WriteProjects(workbook, snapshot.Projects);
        WriteWeChatGroupChats(workbook, snapshot.WeChatGroupChats);
        WriteRelationshipLinks(workbook, snapshot.RelationshipLinks);
        workbook.SaveAs(filePath);
    }

    private static void ValidateUnsupportedSheets(XLWorkbook workbook, List<ImportConflictItem> conflicts)
    {
        foreach (var sheetName in UnsupportedSheetNames)
        {
            if (workbook.Worksheets.TryGetWorksheet(sheetName, out _))
            {
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.excel.sheet.unsupported",
                    Message = $"联系人应用不再支持工作表“{sheetName}”，请改用联系人域专用模板。",
                    Severity = ImportConflictSeverity.Error,
                    EntityType = "Import",
                    EntityIdentifier = sheetName,
                    RecommendedAction = ImportConflictResolutionAction.Skip
                });
            }
        }
    }

    private static IXLWorksheet? GetRequiredSheet(XLWorkbook workbook, string sheetName, List<ImportConflictItem> conflicts)
    {
        if (workbook.Worksheets.TryGetWorksheet(sheetName, out var worksheet))
        {
            return worksheet;
        }

        conflicts.Add(new ImportConflictItem
        {
            Code = "import.excel.sheet.missing",
            Message = $"缺少工作表“{sheetName}”。",
            Severity = ImportConflictSeverity.Error,
            EntityType = "Import",
            EntityIdentifier = sheetName,
            RecommendedAction = ImportConflictResolutionAction.Skip
        });
        return null;
    }

    private static List<ContactProfile> ReadContacts(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["FullName"], ContactsSheetName, conflicts);
        List<ContactProfile> items = [];
        foreach (var row in ReadDataRows(worksheet))
        {
            var relationshipTypeText = GetString(row, headers, "RelationshipType");
            if (!TryParseEnum(relationshipTypeText, ContactRelationshipType.Stranger, out ContactRelationshipType relationshipType))
            {
                conflicts.Add(CreateRowConflict(ContactsSheetName, row.RowNumber(), "联系人关系类型无效。", "import.excel.contact.relationship.invalid"));
                continue;
            }

            items.Add(new ContactProfile
            {
                Id = GetString(row, headers, "Id", fallback: Guid.NewGuid().ToString("N")),
                FullName = GetString(row, headers, "FullName"),
                Nicknames = SplitDelimited(GetString(row, headers, "Nicknames")),
                WeChatIds = SplitDelimited(GetString(row, headers, "WeChatIds")),
                Phones = SplitDelimited(GetString(row, headers, "Phones")),
                Emails = SplitDelimited(GetString(row, headers, "Emails")),
                CompanyIds = SplitDelimited(GetString(row, headers, "CompanyIds")),
                ProjectIds = SplitDelimited(GetString(row, headers, "ProjectIds")),
                RelationshipType = relationshipType,
                Notes = GetString(row, headers, "Notes"),
                IsActive = GetBoolean(row, headers, "IsActive", true)
            });
        }

        return items;
    }

    private static List<CompanyProfile> ReadCompanies(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["Name"], CompaniesSheetName, conflicts);
        List<CompanyProfile> items = [];
        foreach (var row in ReadDataRows(worksheet))
        {
            items.Add(new CompanyProfile
            {
                Id = GetString(row, headers, "Id", fallback: Guid.NewGuid().ToString("N")),
                Name = GetString(row, headers, "Name"),
                Addresses = SplitDelimited(GetString(row, headers, "Addresses")),
                Departments = SplitDelimited(GetString(row, headers, "Departments")),
                Notes = GetString(row, headers, "Notes"),
                IsActive = GetBoolean(row, headers, "IsActive", true)
            });
        }

        return items;
    }

    private static List<ProjectProfile> ReadProjects(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["Name"], ProjectsSheetName, conflicts);
        List<ProjectProfile> items = [];
        foreach (var row in ReadDataRows(worksheet))
        {
            items.Add(new ProjectProfile
            {
                Id = GetString(row, headers, "Id", fallback: Guid.NewGuid().ToString("N")),
                Name = GetString(row, headers, "Name"),
                CompanyId = GetNullableString(row, headers, "CompanyId"),
                OwnerContactId = GetNullableString(row, headers, "OwnerContactId"),
                DeliveryDate = DateOnly.TryParse(GetString(row, headers, "DeliveryDate"), out var deliveryDate) ? deliveryDate : null,
                Tags = SplitDelimited(GetString(row, headers, "Tags")),
                GroupChats = SplitDelimited(GetString(row, headers, "GroupChats")),
                Notes = GetString(row, headers, "Notes"),
                IsActive = GetBoolean(row, headers, "IsActive", true)
            });
        }

        return items;
    }

    private static List<RelationshipLink> ReadRelationshipLinks(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["SourceKind", "SourceId", "TargetKind", "TargetId", "LinkType"], RelationshipLinksSheetName, conflicts);
        List<RelationshipLink> items = [];
        foreach (var row in ReadDataRows(worksheet))
        {
            if (!TryParseEnum(GetString(row, headers, "SourceKind"), EntityKind.Contact, out EntityKind sourceKind))
            {
                conflicts.Add(CreateRowConflict(RelationshipLinksSheetName, row.RowNumber(), "关系来源实体类型无效。", "import.excel.relationship.sourcekind.invalid"));
                continue;
            }

            if (!TryParseEnum(GetString(row, headers, "TargetKind"), EntityKind.Company, out EntityKind targetKind))
            {
                conflicts.Add(CreateRowConflict(RelationshipLinksSheetName, row.RowNumber(), "关系目标实体类型无效。", "import.excel.relationship.targetkind.invalid"));
                continue;
            }

            if (!TryParseEnum(GetString(row, headers, "LinkType"), RelationshipLinkType.WorksAt, out RelationshipLinkType linkType))
            {
                conflicts.Add(CreateRowConflict(RelationshipLinksSheetName, row.RowNumber(), "关系类型无效。", "import.excel.relationship.linktype.invalid"));
                continue;
            }

            items.Add(new RelationshipLink
            {
                Id = GetString(row, headers, "Id", fallback: Guid.NewGuid().ToString("N")),
                SourceEntityKind = sourceKind,
                SourceEntityId = GetString(row, headers, "SourceId"),
                TargetEntityKind = targetKind,
                TargetEntityId = GetString(row, headers, "TargetId"),
                LinkType = linkType,
                DepartmentName = GetString(row, headers, "DepartmentName"),
                Title = GetString(row, headers, "Title"),
                IsPrimary = GetBoolean(row, headers, "IsPrimary", false),
                Notes = GetString(row, headers, "Notes")
            });
        }

        return items;
    }

    private static List<WeChatGroupChatProfile> ReadWeChatGroupChats(IXLWorksheet worksheet, List<ImportConflictItem> conflicts)
    {
        var headers = ReadHeaders(worksheet, ["WeChatUsername", "DisplayName"], WeChatGroupChatsSheetName, conflicts);
        List<WeChatGroupChatProfile> items = [];
        foreach (var row in ReadDataRows(worksheet))
        {
            var username = GetString(row, headers, "WeChatUsername");
            if (string.IsNullOrWhiteSpace(username)) continue;
            items.Add(new WeChatGroupChatProfile
            {
                Id = GetString(row, headers, "Id", fallback: Guid.NewGuid().ToString("N")),
                AccountId = GetString(row, headers, "AccountId"),
                AccountDisplayName = GetString(row, headers, "AccountDisplayName"),
                WeChatUsername = username,
                IsGroupChat = GetBoolean(row, headers, "IsGroupChat", true),
                DisplayName = GetString(row, headers, "DisplayName", fallback: username),
                LastMessageAt = DateTimeOffset.TryParse(GetString(row, headers, "LastMessageAt"), out var lastMessageAt) ? lastMessageAt : null,
                CompanyId = GetNullableString(row, headers, "CompanyId"),
                ProjectId = GetNullableString(row, headers, "ProjectId"),
                IsActive = GetBoolean(row, headers, "IsActive", true),
                LastSyncedAt = DateTimeOffset.TryParse(GetString(row, headers, "LastSyncedAt"), out var syncedAt) ? syncedAt : DateTimeOffset.UtcNow
            });
        }
        return items;
    }

    private static Dictionary<string, int> ReadHeaders(IXLWorksheet worksheet, IReadOnlyList<string> requiredHeaders, string sheetName, List<ImportConflictItem> conflicts)
    {
        Dictionary<string, int> headers = new(StringComparer.OrdinalIgnoreCase);
        var headerRow = worksheet.Row(1);
        foreach (var cell in headerRow.CellsUsed())
        {
            var name = cell.GetString().Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                headers[name] = cell.Address.ColumnNumber;
            }
        }

        foreach (var header in requiredHeaders)
        {
            if (!headers.ContainsKey(header))
            {
                conflicts.Add(new ImportConflictItem
                {
                    Code = "import.excel.header.missing",
                    Message = $"工作表“{sheetName}”缺少列“{header}”。",
                    Severity = ImportConflictSeverity.Error,
                    EntityType = sheetName,
                    EntityIdentifier = header,
                    RecommendedAction = ImportConflictResolutionAction.Skip
                });
            }
        }

        return headers;
    }

    private static IEnumerable<IXLRow> ReadDataRows(IXLWorksheet worksheet)
        => worksheet.RowsUsed().Skip(1).Where(static row => !row.IsEmpty());

    private static string GetString(IXLRow row, IReadOnlyDictionary<string, int> headers, string name, string fallback = "")
    {
        if (!headers.TryGetValue(name, out var column))
        {
            return fallback;
        }

        var value = row.Cell(column).GetString().Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string? GetNullableString(IXLRow row, IReadOnlyDictionary<string, int> headers, string name)
    {
        var value = GetString(row, headers, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool GetBoolean(IXLRow row, IReadOnlyDictionary<string, int> headers, string name, bool fallback)
    {
        var value = GetString(row, headers, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return bool.TryParse(value, out var parsed)
            ? parsed
            : value is "1" or "是" or "Y" or "y";
    }

    private static bool TryParseEnum<TEnum>(string raw, TEnum fallback, out TEnum value)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = fallback;
            return true;
        }

        return Enum.TryParse(raw, true, out value);
    }

    private static ImportConflictItem CreateRowConflict(string sheetName, int rowNumber, string message, string code)
    {
        return new ImportConflictItem
        {
            Code = code,
            Message = message,
            Severity = ImportConflictSeverity.Error,
            EntityType = sheetName,
            EntityIdentifier = $"Row {rowNumber}",
            RecommendedAction = ImportConflictResolutionAction.Skip
        };
    }

    private static List<string> SplitDelimited(string raw)
        => raw.Split([';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string JoinDelimited(IEnumerable<string> values)
        => string.Join("; ", values.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()));

    private static void WriteContacts(XLWorkbook workbook, IReadOnlyList<ContactProfile> items)
    {
        var worksheet = workbook.Worksheets.Add(ContactsSheetName);
        var headers = new[] { "Id", "FullName", "Nicknames", "WeChatIds", "Phones", "Emails", "CompanyIds", "ProjectIds", "RelationshipType", "Notes", "IsActive" };
        WriteHeaders(worksheet, headers);
        var row = 2;
        foreach (var item in items)
        {
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.FullName;
            worksheet.Cell(row, 3).Value = JoinDelimited(item.Nicknames);
            worksheet.Cell(row, 4).Value = JoinDelimited(item.WeChatIds);
            worksheet.Cell(row, 5).Value = JoinDelimited(item.Phones);
            worksheet.Cell(row, 6).Value = JoinDelimited(item.Emails);
            worksheet.Cell(row, 7).Value = JoinDelimited(item.CompanyIds);
            worksheet.Cell(row, 8).Value = JoinDelimited(item.ProjectIds);
            worksheet.Cell(row, 9).Value = item.RelationshipType.ToString();
            worksheet.Cell(row, 10).Value = item.Notes;
            worksheet.Cell(row, 11).Value = item.IsActive;
            row++;
        }

        worksheet.Columns().AdjustToContents();
    }

    private static void WriteCompanies(XLWorkbook workbook, IReadOnlyList<CompanyProfile> items)
    {
        var worksheet = workbook.Worksheets.Add(CompaniesSheetName);
        var headers = new[] { "Id", "Name", "Addresses", "Departments", "Notes", "IsActive" };
        WriteHeaders(worksheet, headers);
        var row = 2;
        foreach (var item in items)
        {
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.Name;
            worksheet.Cell(row, 3).Value = JoinDelimited(item.Addresses);
            worksheet.Cell(row, 4).Value = JoinDelimited(item.Departments);
            worksheet.Cell(row, 5).Value = item.Notes;
            worksheet.Cell(row, 6).Value = item.IsActive;
            row++;
        }

        worksheet.Columns().AdjustToContents();
    }

    private static void WriteProjects(XLWorkbook workbook, IReadOnlyList<ProjectProfile> items)
    {
        var worksheet = workbook.Worksheets.Add(ProjectsSheetName);
        var headers = new[] { "Id", "Name", "CompanyId", "CompanyName", "OwnerContactId", "OwnerFullName", "DeliveryDate", "Tags", "GroupChats", "Notes", "IsActive" };
        WriteHeaders(worksheet, headers);
        var row = 2;
        foreach (var item in items)
        {
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.Name;
            worksheet.Cell(row, 3).Value = item.CompanyId ?? string.Empty;
            worksheet.Cell(row, 4).Value = string.Empty;
            worksheet.Cell(row, 5).Value = item.OwnerContactId ?? string.Empty;
            worksheet.Cell(row, 6).Value = string.Empty;
            worksheet.Cell(row, 7).Value = item.DeliveryDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            worksheet.Cell(row, 8).Value = JoinDelimited(item.Tags);
            worksheet.Cell(row, 9).Value = JoinDelimited(item.GroupChats);
            worksheet.Cell(row, 10).Value = item.Notes;
            worksheet.Cell(row, 11).Value = item.IsActive;
            row++;
        }

        worksheet.Columns().AdjustToContents();
    }

    private static void WriteWeChatGroupChats(XLWorkbook workbook, IReadOnlyList<WeChatGroupChatProfile> items)
    {
        var worksheet = workbook.Worksheets.Add(WeChatGroupChatsSheetName);
        var headers = new[] { "Id", "AccountId", "AccountDisplayName", "WeChatUsername", "IsGroupChat", "DisplayName", "LastMessageAt", "CompanyId", "ProjectId", "IsActive", "LastSyncedAt" };
        WriteHeaders(worksheet, headers);
        var row = 2;
        foreach (var item in items)
        {
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.AccountId;
            worksheet.Cell(row, 3).Value = item.AccountDisplayName;
            worksheet.Cell(row, 4).Value = item.WeChatUsername;
            worksheet.Cell(row, 5).Value = item.IsGroupChat;
            worksheet.Cell(row, 6).Value = item.DisplayName;
            worksheet.Cell(row, 7).Value = item.LastMessageAt?.ToString("O") ?? string.Empty;
            worksheet.Cell(row, 8).Value = item.CompanyId ?? string.Empty;
            worksheet.Cell(row, 9).Value = item.ProjectId ?? string.Empty;
            worksheet.Cell(row, 10).Value = item.IsActive;
            worksheet.Cell(row, 11).Value = item.LastSyncedAt.ToString("O");
            row++;
        }
        worksheet.Columns().AdjustToContents();
    }

    private static void WriteRelationshipLinks(XLWorkbook workbook, IReadOnlyList<RelationshipLink> items)
    {
        var worksheet = workbook.Worksheets.Add(RelationshipLinksSheetName);
        var headers = new[] { "Id", "SourceKind", "SourceId", "TargetKind", "TargetId", "SourceName", "TargetName", "LinkType", "DepartmentName", "Title", "IsPrimary", "Notes" };
        WriteHeaders(worksheet, headers);
        var row = 2;
        foreach (var item in items)
        {
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.SourceEntityKind.ToString();
            worksheet.Cell(row, 3).Value = item.SourceEntityId;
            worksheet.Cell(row, 4).Value = item.TargetEntityKind.ToString();
            worksheet.Cell(row, 5).Value = item.TargetEntityId;
            worksheet.Cell(row, 6).Value = string.Empty;
            worksheet.Cell(row, 7).Value = string.Empty;
            worksheet.Cell(row, 8).Value = item.LinkType.ToString();
            worksheet.Cell(row, 9).Value = item.DepartmentName;
            worksheet.Cell(row, 10).Value = item.Title;
            worksheet.Cell(row, 11).Value = item.IsPrimary;
            worksheet.Cell(row, 12).Value = item.Notes;
            row++;
        }

        worksheet.Columns().AdjustToContents();
    }

    private static void WriteHeaders(IXLWorksheet worksheet, IReadOnlyList<string> headers)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            worksheet.Cell(1, index + 1).Value = headers[index];
        }

        worksheet.Row(1).Style.Font.Bold = true;
    }
}
