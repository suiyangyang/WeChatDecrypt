using ClosedXML.Excel;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;
using Lumine.Fairy.ContactManagement.Models;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class ExcelDataExchangeServiceTests
{
    [Fact]
    public async Task ExportExcelAsync_ThenPreviewAsync_RoundTripsCurrentSnapshot()
    {
        var tempDirectory = CreateTempDirectory();
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = tempDirectory
        };

        var validationService = new ContactManagementValidationService();
        var store = new ContactManagementSqliteStore(options, validationService);
        await store.SaveAsync(new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile
                {
                    Id = "contact-1",
                    FullName = "赵六",
                    Phones = ["13600000000"],
                    Emails = ["zhaoliu@example.com"],
                    CompanyIds = ["company-1"],
                    ProjectIds = ["project-1"]
                }
            ],
            Companies =
            [
                new CompanyProfile { Id = "company-1", Name = "远山咨询" }
            ],
            Projects =
            [
                new ProjectProfile { Id = "project-1", Name = "投标整理", CompanyId = "company-1", OwnerContactId = "contact-1", GroupChats = ["投标微信群"] }
            ],
            RelationshipLinks =
            [
                new RelationshipLink
                {
                    Id = "link-1",
                    SourceEntityId = "contact-1",
                    SourceEntityKind = EntityKind.Contact,
                    TargetEntityId = "company-1",
                    TargetEntityKind = EntityKind.Company,
                    LinkType = RelationshipLinkType.WorksAt,
                    DepartmentName = "咨询部"
                }
            ],
            TodoEntries =
            [
                new TodoEntry
                {
                    Id = "todo-1",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "投标整理",
                    Content = "整理 Excel 导出",
                    WorkHours = 2m,
                    IsDaily = true,
                    SortOrder = 1
                }
            ],
            ClientIssues =
            [
                new ClientIssueEntry
                {
                    Id = "issue-1",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "投标整理",
                    ClientName = "远山客户",
                    IssueLines = ["字段顺序需确认"],
                    SolutionLines = ["按模板固定输出"]
                }
            ],
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 21),
                    TotalHours = 2m,
                    FormattedReportText = "1、(投标整理)整理 Excel 导出,2;"
                }
            ],
            ScratchNoteDocuments =
            [
                new ScratchNoteDocument
                {
                    Id = "scratch-note-20260321",
                    WorkDate = new DateOnly(2026, 3, 21),
                    Title = "2026-03-21 随手记",
                    RawText = "3/21\n1.投标整理: 整理 Excel 导出 2"
                }
            ]
        });

        var exportService = new JsonDataExportService(store, options);
        var importService = new JsonDataImportService(store, options, validationService);
        var excelPath = Path.Combine(tempDirectory, "export", "contact-data.xlsx");

        await exportService.ExportExcelAsync(new ExportRequest
        {
            TargetKind = ExportTargetKind.Excel,
            FilePath = excelPath
        });

        var preview = await importService.PreviewAsync(new ImportRequest
        {
            SourceKind = ImportSourceKind.Excel,
            FilePath = excelPath
        });

        Assert.True(File.Exists(excelPath));
        Assert.True(preview.IsValid);
        Assert.Single(preview.PreviewSnapshot.Contacts);
        Assert.Single(preview.PreviewSnapshot.Companies);
        Assert.Single(preview.PreviewSnapshot.Projects);
        Assert.Single(preview.PreviewSnapshot.RelationshipLinks);
        Assert.Equal(["company-1"], preview.PreviewSnapshot.Contacts[0].CompanyIds);
        Assert.Equal(["投标微信群"], preview.PreviewSnapshot.Projects[0].GroupChats);
        Assert.Equal(1, preview.Statistics.TodoCount);
        Assert.Equal(1, preview.Statistics.ClientIssueCount);
        Assert.Equal(1, preview.Statistics.DailyReportCount);
        Assert.Equal(1, preview.Statistics.ScratchNoteCount);
        Assert.Single(preview.PreviewSnapshot.TodoEntries);
        Assert.Single(preview.PreviewSnapshot.ClientIssues);
        Assert.Single(preview.PreviewSnapshot.DailyReports);
        Assert.Single(preview.PreviewSnapshot.ScratchNoteDocuments);
        Assert.Contains(preview.Conflicts, item => item.Code == "import.contact.duplicate");
        Assert.Contains(preview.Conflicts, item => item.Code == "import.todo.duplicate");
        Assert.Contains(preview.Conflicts, item => item.Code == "import.clientissue.duplicate");
        Assert.Contains(preview.Conflicts, item => item.Code == "import.dailyreport.duplicate");
        Assert.Contains(preview.Conflicts, item => item.Code == "import.scratchnote.duplicate");
    }

    [Fact]
    public async Task PreviewAsync_WhenWorkbookMissingSheet_ReturnsError()
    {
        var tempDirectory = CreateTempDirectory();
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = tempDirectory
        };

        var workbookPath = Path.Combine(tempDirectory, "broken.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var contacts = workbook.Worksheets.Add("Contacts");
            contacts.Cell(1, 1).Value = "FullName";
            contacts.Cell(2, 1).Value = "仅有联系人";
            workbook.SaveAs(workbookPath);
        }

        var store = new ContactManagementSqliteStore(options, new ContactManagementValidationService());
        var importService = new JsonDataImportService(store, options, new ContactManagementValidationService());

        var preview = await importService.PreviewAsync(new ImportRequest
        {
            SourceKind = ImportSourceKind.Excel,
            FilePath = workbookPath
        });

        Assert.False(preview.IsValid);
        Assert.Contains(preview.Conflicts, item => item.Code == "import.excel.sheet.missing");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
