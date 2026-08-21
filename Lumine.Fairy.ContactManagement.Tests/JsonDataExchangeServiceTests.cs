using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;
using Lumine.Fairy.ContactManagement.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class JsonDataExchangeServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task PreviewAsync_WithValidJson_ReturnsSnapshotAndWarnings()
    {
        var tempDirectory = CreateTempDirectory();
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = tempDirectory
        };

        var store = new ContactManagementSqliteStore(options, new ContactManagementValidationService());
        await store.SaveAsync(new ContactManagementSnapshot
        {
            Companies =
            [
                new CompanyProfile { Id = "company-existing", Name = "星云科技" }
            ],
            Contacts =
            [
                new ContactProfile
                {
                    Id = "contact-existing",
                    FullName = "张三",
                    Phones = ["13800000000"],
                    Emails = ["zhangsan@existing.com"]
                }
            ],
            TodoEntries =
            [
                new TodoEntry
                {
                    Id = "todo-existing",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "客户档案整理",
                    Content = "整理导入说明",
                    WorkHours = 0.5m,
                    IsDaily = true,
                    SortOrder = 1
                }
            ],
            ClientIssues =
            [
                new ClientIssueEntry
                {
                    Id = "issue-existing",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "客户档案整理",
                    ClientName = "导入客户",
                    IssueLines = ["已有问题"],
                    SolutionLines = ["已有方案"]
                }
            ],
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 21),
                    TotalHours = 0.5m,
                    FormattedReportText = "existing report"
                }
            ],
            ScratchNoteDocuments =
            [
                new ScratchNoteDocument
                {
                    Id = "scratch-note-existing",
                    WorkDate = new DateOnly(2026, 3, 21),
                    Title = "existing scratch",
                    RawText = "existing raw"
                }
            ]
        });

        var importFilePath = Path.Combine(tempDirectory, "import.json");
        await WriteExchangeFileAsync(importFilePath, options.SchemaVersion, new ContactManagementSnapshot
        {
            Companies =
            [
                new CompanyProfile { Id = "company-new", Name = "星云科技" }
            ],
            Contacts =
            [
                new ContactProfile
                {
                    Id = "contact-new",
                    FullName = "张三",
                    Phones = ["13800000000"],
                    Emails = ["zhangsan@import.com"]
                }
            ],
            Projects =
            [
                new ProjectProfile { Id = "project-1", Name = "客户档案整理" }
            ],
            TodoEntries =
            [
                new TodoEntry
                {
                    Id = "todo-1",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "客户档案整理",
                    Content = "整理导入说明",
                    WorkHours = 1m,
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
                    ProjectName = "客户档案整理",
                    ClientName = "导入客户",
                    IssueLines = ["导入文件缺少列"],
                    SolutionLines = ["补齐模板字段"]
                }
            ],
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 21),
                    TotalHours = 1m,
                    FormattedReportText = "1、(客户档案整理)整理导入说明,1;"
                }
            ],
            ScratchNoteDocuments =
            [
                new ScratchNoteDocument
                {
                    Id = "scratch-note-20260321",
                    WorkDate = new DateOnly(2026, 3, 21),
                    Title = "2026-03-21 随手记",
                    RawText = "3/21\n1.客户档案整理: 整理导入说明 1"
                }
            ]
        });

        var service = new JsonDataImportService(store, options, new ContactManagementValidationService());
        var result = await service.PreviewAsync(new ImportRequest { FilePath = importFilePath });

        Assert.True(result.IsValid);
        Assert.Equal(1, result.Statistics.ContactCount);
        Assert.Equal(1, result.Statistics.CompanyCount);
        Assert.Equal(1, result.Statistics.ProjectCount);
        Assert.Equal(1, result.Statistics.TodoCount);
        Assert.Equal(1, result.Statistics.ClientIssueCount);
        Assert.Equal(1, result.Statistics.DailyReportCount);
        Assert.Equal(1, result.Statistics.ScratchNoteCount);
        Assert.Single(result.PreviewSnapshot.TodoEntries);
        Assert.Single(result.PreviewSnapshot.ClientIssues);
        Assert.Single(result.PreviewSnapshot.DailyReports);
        Assert.Single(result.PreviewSnapshot.ScratchNoteDocuments);
        Assert.Contains(result.Conflicts, item => item.Code == "import.contact.duplicate");
        Assert.Contains(result.Conflicts, item => item.Code == "import.company.duplicate");

        var todoConflict = Assert.Single(result.Conflicts, item => item.Code == "import.todo.duplicate");
        Assert.Equal(nameof(TodoEntry), todoConflict.EntityType);
        var clientIssueConflict = Assert.Single(result.Conflicts, item => item.Code == "import.clientissue.duplicate");
        Assert.Equal(nameof(ClientIssueEntry), clientIssueConflict.EntityType);
        var dailyReportConflict = Assert.Single(result.Conflicts, item => item.Code == "import.dailyreport.duplicate");
        Assert.Equal("2026-03-21", dailyReportConflict.EntityIdentifier);
        var scratchNoteConflict = Assert.Single(result.Conflicts, item => item.Code == "import.scratchnote.duplicate");
        Assert.Equal(nameof(ScratchNoteDocument), scratchNoteConflict.EntityType);
    }

    [Fact]
    public async Task CommitAsync_WithValidPreview_PersistsSnapshotToSqlite()
    {
        var tempDirectory = CreateTempDirectory();
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = tempDirectory
        };

        var validationService = new ContactManagementValidationService();
        var store = new ContactManagementSqliteStore(options, validationService);
        var importFilePath = Path.Combine(tempDirectory, "commit-import.json");
        await WriteExchangeFileAsync(importFilePath, options.SchemaVersion, new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile
                {
                    Id = "contact-1",
                    FullName = "李四",
                    Phones = ["13900000000"],
                    Emails = ["lisi@example.com"]
                }
            ],
            Companies =
            [
                new CompanyProfile { Id = "company-1", Name = "晨光信息" }
            ],
            TodoEntries =
            [
                new TodoEntry
                {
                    Id = "todo-1",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "晨光信息项目",
                    Content = "整理日报",
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
                    ProjectName = "晨光信息项目",
                    ClientName = "晨光客户",
                    IssueLines = ["接口异常"],
                    SolutionLines = ["补日志"]
                }
            ],
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 21),
                    TotalHours = 2m,
                    FormattedReportText = "1、(晨光信息项目)整理日报,2;"
                }
            ],
            ScratchNoteDocuments =
            [
                new ScratchNoteDocument
                {
                    Id = "scratch-note-20260321",
                    WorkDate = new DateOnly(2026, 3, 21),
                    Title = "2026-03-21 随手记",
                    RawText = "3/21\n1.晨光信息项目: 整理日报 2"
                }
            ]
        });

        var importService = new JsonDataImportService(store, options, validationService);
        var preview = await importService.PreviewAsync(new ImportRequest { FilePath = importFilePath });
        var commit = await importService.CommitAsync(new ImportCommitRequest { PreviewResult = preview });
        var savedSnapshot = await store.LoadAsync();

        Assert.True(commit.Success);
        Assert.Single(savedSnapshot.Contacts);
        Assert.Single(savedSnapshot.Companies);
        Assert.Single(savedSnapshot.TodoEntries);
        Assert.Single(savedSnapshot.ClientIssues);
        Assert.Single(savedSnapshot.DailyReports);
        Assert.Single(savedSnapshot.ScratchNoteDocuments);
        Assert.Equal("李四", savedSnapshot.Contacts[0].FullName);
        Assert.Equal("晨光信息", savedSnapshot.Companies[0].Name);
    }

    [Fact]
    public async Task CommitAsync_WhenDuplicateWarningsAreSkipped_KeepsExistingData()
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
                    Id = "contact-existing",
                    FullName = "张三",
                    Phones = ["13800000000"],
                    Emails = ["zhangsan@existing.com"],
                    Notes = "existing"
                }
            ],
            Companies =
            [
                new CompanyProfile { Id = "company-existing", Name = "星云科技", Notes = "existing-company" }
            ],
            TodoEntries =
            [
                new TodoEntry
                {
                    Id = "todo-existing",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "星云科技项目",
                    Title = "整理模板",
                    Content = "整理模板",
                    WorkHours = 1m,
                    IsDaily = true,
                    SortOrder = 1,
                    Notes = "existing-todo"
                }
            ],
            ClientIssues =
            [
                new ClientIssueEntry
                {
                    Id = "issue-existing",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "星云科技项目",
                    ClientName = "星云客户",
                    IssueLines = ["旧问题"],
                    SolutionLines = ["旧方案"],
                    Notes = "existing-issue"
                }
            ],
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 21),
                    TotalHours = 1m,
                    FormattedReportText = "旧日报",
                    RawScratchText = "old-raw"
                }
            ],
            ScratchNoteDocuments =
            [
                new ScratchNoteDocument
                {
                    Id = "scratch-note-20260321",
                    WorkDate = new DateOnly(2026, 3, 21),
                    Title = "旧随手记",
                    RawText = "old scratch"
                }
            ]
        });

        var importFilePath = Path.Combine(tempDirectory, "skip-duplicate.json");
        await WriteExchangeFileAsync(importFilePath, options.SchemaVersion, new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile
                {
                    Id = "contact-new",
                    FullName = "张三",
                    Phones = ["13800000000"],
                    Emails = ["zhangsan@import.com"],
                    Notes = "imported"
                }
            ],
            Companies =
            [
                new CompanyProfile { Id = "company-new", Name = "星云科技", Notes = "imported-company" }
            ],
            TodoEntries =
            [
                new TodoEntry
                {
                    Id = "todo-new",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "星云科技项目",
                    Title = "整理模板",
                    Content = "整理模板",
                    WorkHours = 2m,
                    IsDaily = true,
                    SortOrder = 1,
                    Notes = "imported-todo"
                }
            ],
            ClientIssues =
            [
                new ClientIssueEntry
                {
                    Id = "issue-new",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "星云科技项目",
                    ClientName = "星云客户",
                    IssueLines = ["新问题"],
                    SolutionLines = ["新方案"],
                    Notes = "imported-issue"
                }
            ],
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 21),
                    TotalHours = 2m,
                    FormattedReportText = "新日报",
                    RawScratchText = "new-raw"
                }
            ],
            ScratchNoteDocuments =
            [
                new ScratchNoteDocument
                {
                    Id = "scratch-note-20260321-new",
                    WorkDate = new DateOnly(2026, 3, 21),
                    Title = "新随手记",
                    RawText = "new scratch"
                }
            ]
        });

        var importService = new JsonDataImportService(store, options, validationService);
        var preview = await importService.PreviewAsync(new ImportRequest { FilePath = importFilePath });

        var commit = await importService.CommitAsync(new ImportCommitRequest
        {
            PreviewResult = preview,
            ConflictResolutions =
            [
                new ImportConflictResolutionDecision
                {
                    Code = "import.contact.duplicate",
                    EntityType = nameof(ContactProfile),
                    EntityIdentifier = "张三",
                    Action = ImportConflictResolutionAction.Skip
                },
                new ImportConflictResolutionDecision
                {
                    Code = "import.company.duplicate",
                    EntityType = nameof(CompanyProfile),
                    EntityIdentifier = "星云科技",
                    Action = ImportConflictResolutionAction.Skip
                },
                new ImportConflictResolutionDecision
                {
                    Code = "import.todo.duplicate",
                    EntityType = nameof(TodoEntry),
                    EntityIdentifier = "2026-03-21|星云科技项目|整理模板|#1",
                    Action = ImportConflictResolutionAction.Skip
                },
                new ImportConflictResolutionDecision
                {
                    Code = "import.clientissue.duplicate",
                    EntityType = nameof(ClientIssueEntry),
                    EntityIdentifier = "2026-03-21|星云科技项目|星云客户",
                    Action = ImportConflictResolutionAction.Skip
                },
                new ImportConflictResolutionDecision
                {
                    Code = "import.dailyreport.duplicate",
                    EntityType = nameof(DailyReport),
                    EntityIdentifier = "2026-03-21",
                    Action = ImportConflictResolutionAction.Skip
                },
                new ImportConflictResolutionDecision
                {
                    Code = "import.scratchnote.duplicate",
                    EntityType = nameof(ScratchNoteDocument),
                    EntityIdentifier = "scratch-note-20260321-new",
                    Action = ImportConflictResolutionAction.Skip
                }
            ]
        });

        var savedSnapshot = await store.LoadAsync();
        var savedContact = Assert.Single(savedSnapshot.Contacts);
        var savedCompany = Assert.Single(savedSnapshot.Companies);
        var savedTodo = Assert.Single(savedSnapshot.TodoEntries);
        var savedClientIssue = Assert.Single(savedSnapshot.ClientIssues);
        var savedDailyReport = Assert.Single(savedSnapshot.DailyReports);
        var savedScratchNote = Assert.Single(savedSnapshot.ScratchNoteDocuments);

        Assert.True(commit.Success);
        Assert.Contains("跳过了 6 个冲突项", commit.Summary);
        Assert.Equal("contact-existing", savedContact.Id);
        Assert.Equal("existing", savedContact.Notes);
        Assert.Equal("company-existing", savedCompany.Id);
        Assert.Equal("existing-company", savedCompany.Notes);
        Assert.Equal("todo-existing", savedTodo.Id);
        Assert.Equal("existing-todo", savedTodo.Notes);
        Assert.Equal("issue-existing", savedClientIssue.Id);
        Assert.Equal("existing-issue", savedClientIssue.Notes);
        Assert.Equal(1m, savedDailyReport.TotalHours);
        Assert.Equal("old-raw", savedDailyReport.RawScratchText);
        Assert.Equal("scratch-note-20260321", savedScratchNote.Id);
        Assert.Equal("old scratch", savedScratchNote.RawText);
    }

    [Fact]
    public async Task ExportJsonAsync_ThenPreviewAsync_RoundTripsCurrentSnapshot()
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
                    FullName = "王五",
                    Phones = ["13700000000"]
                }
            ],
            Companies =
            [
                new CompanyProfile { Id = "company-1", Name = "北辰设计" }
            ],
            Projects =
            [
                new ProjectProfile { Id = "project-1", Name = "官网重构", CompanyId = "company-1", OwnerContactId = "contact-1" }
            ],
            TodoEntries =
            [
                new TodoEntry
                {
                    Id = "todo-1",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "官网重构",
                    Content = "联调导出",
                    WorkHours = 1.5m,
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
                    ProjectName = "官网重构",
                    ClientName = "北辰客户",
                    IssueLines = ["导出字段核对"],
                    SolutionLines = ["增加回归测试"]
                }
            ],
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 21),
                    TotalHours = 1.5m,
                    FormattedReportText = "1、(官网重构)联调导出,1.5;"
                }
            ],
            ScratchNoteDocuments =
            [
                new ScratchNoteDocument
                {
                    Id = "scratch-note-20260321",
                    WorkDate = new DateOnly(2026, 3, 21),
                    Title = "2026-03-21 随手记",
                    RawText = "3/21\n1.官网重构: 联调导出 1.5"
                }
            ]
        });

        var exportFilePath = Path.Combine(tempDirectory, "export", "contact-data.json");
        var exportService = new JsonDataExportService(store, options);
        await exportService.ExportJsonAsync(new ExportRequest { FilePath = exportFilePath });

        var importService = new JsonDataImportService(store, options, validationService);
        var preview = await importService.PreviewAsync(new ImportRequest { FilePath = exportFilePath });

        Assert.True(File.Exists(exportFilePath));
        Assert.True(preview.IsValid);
        Assert.Equal(1, preview.Statistics.TodoCount);
        Assert.Equal(1, preview.Statistics.ClientIssueCount);
        Assert.Equal(1, preview.Statistics.DailyReportCount);
        Assert.Equal(1, preview.Statistics.ScratchNoteCount);
        var contact = Assert.Single(preview.PreviewSnapshot.Contacts);
        Assert.Equal("王五", contact.FullName);
        Assert.Single(preview.PreviewSnapshot.Projects);
        Assert.Single(preview.PreviewSnapshot.TodoEntries);
        Assert.Single(preview.PreviewSnapshot.ClientIssues);
        Assert.Single(preview.PreviewSnapshot.DailyReports);
        Assert.Single(preview.PreviewSnapshot.ScratchNoteDocuments);
        Assert.Contains(preview.Conflicts, item => item.Code == "import.contact.duplicate");
    }

    [Fact]
    public async Task PreviewAsync_WithMissingFile_ReturnsError()
    {
        var tempDirectory = CreateTempDirectory();
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = tempDirectory
        };

        var store = new ContactManagementSqliteStore(options, new ContactManagementValidationService());
        var service = new JsonDataImportService(store, options, new ContactManagementValidationService());
        var result = await service.PreviewAsync(new ImportRequest { FilePath = Path.Combine(tempDirectory, "missing.json") });

        Assert.False(result.IsValid);
        Assert.Contains(result.Conflicts, item => item.Code == "import.file.not_found");
    }

    private static async Task WriteExchangeFileAsync(string filePath, int schemaVersion, ContactManagementSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new
        {
            SchemaVersion = schemaVersion,
            Contacts = snapshot.Contacts,
            Companies = snapshot.Companies,
            Projects = snapshot.Projects,
            RelationshipLinks = snapshot.RelationshipLinks,
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        };

        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(document, JsonOptions));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
