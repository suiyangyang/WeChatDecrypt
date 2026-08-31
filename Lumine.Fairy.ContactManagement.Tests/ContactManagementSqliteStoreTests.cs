using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;
using Lumine.Fairy.ContactManagement.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class ContactManagementSqliteStoreTests
{
    [Fact]
    public void ContactManagementStorageOptions_DefaultBaseDirectoryPath_UsesStableAbsolutePath()
    {
        var options = new ContactManagementStorageOptions();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expectedBaseDirectory = !string.IsNullOrWhiteSpace(localAppData)
            ? Path.Combine(localAppData, "Lumine.Fairy", "AppData", "contact-management")
            : !string.IsNullOrWhiteSpace(userProfile)
                ? Path.Combine(userProfile, ".lumine-fairy", "AppData", "contact-management")
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Lumine.Fairy", "AppData", "contact-management"));

        Assert.True(Path.IsPathRooted(options.BaseDirectoryPath));
        Assert.Equal(expectedBaseDirectory, options.BaseDirectoryPath);
        Assert.Equal(Path.Combine(expectedBaseDirectory, "contact-management.db"), options.DatabaseFilePath);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsSnapshot()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = tempDirectory
        };

        var store = new ContactManagementSqliteStore(options, new ContactManagementValidationService());
        var contact = new ContactProfile
        {
            Id = "contact-1",
            FullName = "张三",
            Nicknames = ["阿三", "zhangsan@wechat"],
            Phones = ["+86 13800000000"],
            Emails = ["zhangsan@example.com"],
            CompanyIds = ["company-1"],
            ProjectIds = ["project-1"]
        };
        var company = new CompanyProfile
        {
            Id = "company-1",
            Name = "示例科技"
        };
        var project = new ProjectProfile
        {
            Id = "project-1",
            Name = "Fairy OS 联系人系统",
            CompanyId = company.Id,
            OwnerContactId = contact.Id,
            DeliveryDate = new DateOnly(2026, 4, 15),
            Tags = ["crm", "mvp"],
            GroupChats = ["微信A群", "QQ支持群"]
        };
        var relationship = new RelationshipLink
        {
            Id = "link-1",
            SourceEntityId = contact.Id,
            SourceEntityKind = EntityKind.Contact,
            TargetEntityId = company.Id,
            TargetEntityKind = EntityKind.Company,
            LinkType = RelationshipLinkType.WorksAt,
            DepartmentName = "研发",
            IsPrimary = true
        };
        var todoEntry = new TodoEntry
        {
            Id = "todo-1",
            WorkDate = new DateOnly(2026, 3, 21),
            ProjectName = "Lumine.Fairy",
            Title = "TodoList 管理方案设计",
            Content = "TodoList 管理方案设计",
            Status = TodoStatus.InProgress,
            WorkHours = 2.5m,
            IsDaily = true,
            IsFromScratchNote = true,
            SourceText = "1.Lumine.Fairy: TodoList 管理方案设计 2.5",
            SortOrder = 1
        };
        var clientIssue = new ClientIssueEntry
        {
            Id = "issue-1",
            WorkDate = new DateOnly(2026, 3, 21),
            ProjectName = "福立",
            ClientName = "福立",
            IssueLines = ["检测器联机失败", "设备重连后状态异常"],
            SolutionLines = ["重启服务并复核串口参数"]
        };
        var dailyReport = new DailyReport
        {
            Date = new DateOnly(2026, 3, 21),
            Items = [todoEntry],
            ClientIssues = [clientIssue],
            TotalHours = 2.5m,
            RawScratchText = "2026/3/21\n1.Lumine.Fairy: TodoList 管理方案设计 2.5",
            FormattedReportText = "1、(Lumine.Fairy)TodoList 管理方案设计,2.5;"
        };
        var scratchNote = new ScratchNoteDocument
        {
            Id = "note-1",
            WorkDate = new DateOnly(2026, 3, 21),
            Title = "2026-03-21 随手记",
            RawText = dailyReport.RawScratchText,
            CreatedAtUtc = new DateTimeOffset(2026, 3, 21, 1, 2, 3, TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(2026, 3, 21, 2, 3, 4, TimeSpan.Zero),
            LastArrangedAtUtc = new DateTimeOffset(2026, 3, 21, 3, 4, 5, TimeSpan.Zero)
        };

        await store.SaveAsync(new ContactManagementSnapshot
        {
            Contacts = [contact],
            Companies = [company],
            Projects = [project],
            RelationshipLinks = [relationship],
            TodoEntries = [todoEntry],
            ClientIssues = [clientIssue],
            DailyReports = [dailyReport],
            ScratchNoteDocuments = [scratchNote]
        });

        var snapshot = await store.LoadAsync();

        Assert.True(File.Exists(options.DatabaseFilePath));
        Assert.Single(snapshot.Contacts);
        Assert.Single(snapshot.Companies);
        Assert.Single(snapshot.Projects);
        Assert.Single(snapshot.RelationshipLinks);
        Assert.Single(snapshot.TodoEntries);
        Assert.Single(snapshot.ClientIssues);
        Assert.Single(snapshot.DailyReports);
        Assert.Single(snapshot.ScratchNoteDocuments);
        Assert.Equal("张三", snapshot.Contacts[0].FullName);
        Assert.Equal(["company-1"], snapshot.Contacts[0].CompanyIds);
        Assert.Equal(["project-1"], snapshot.Contacts[0].ProjectIds);
        Assert.Equal("示例科技", snapshot.Companies[0].Name);
        Assert.Equal("Fairy OS 联系人系统", snapshot.Projects[0].Name);
        Assert.Equal(new DateOnly(2026, 4, 15), snapshot.Projects[0].DeliveryDate);
        Assert.Equal(["微信A群", "QQ支持群"], snapshot.Projects[0].GroupChats);
        Assert.Equal("研发", snapshot.RelationshipLinks[0].DepartmentName);
        Assert.Equal(2.5m, snapshot.TodoEntries[0].WorkHours);
        Assert.Equal("检测器联机失败", snapshot.ClientIssues[0].IssueLines[0]);
        Assert.Equal("1、(Lumine.Fairy)TodoList 管理方案设计,2.5;", snapshot.DailyReports[0].FormattedReportText);
        Assert.Equal("2026-03-21 随手记", snapshot.ScratchNoteDocuments[0].Title);
    }

    [Fact]
    public async Task SaveAsync_WhenCalledTwice_ReplacesPreviousSnapshot()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = tempDirectory
        };

        var store = new ContactManagementSqliteStore(options, new ContactManagementValidationService());

        await store.SaveAsync(new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile { Id = "contact-1", FullName = "第一版联系人" }
            ],
            Companies =
            [
                new CompanyProfile { Id = "company-1", Name = "第一版公司" }
            ]
        });

        await store.SaveAsync(new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile { Id = "contact-2", FullName = "第二版联系人" }
            ],
            Companies =
            [
                new CompanyProfile { Id = "company-2", Name = "第二版公司" }
            ]
        });

        var snapshot = await store.LoadAsync();

        var contact = Assert.Single(snapshot.Contacts);
        var company = Assert.Single(snapshot.Companies);
        Assert.Equal("contact-2", contact.Id);
        Assert.Equal("第二版联系人", contact.FullName);
        Assert.Equal("company-2", company.Id);
        Assert.Equal("第二版公司", company.Name);
    }

    [Fact]
    public async Task Constructor_WhenSchemaSixAlreadyContainsAccountDisplayName_CompletesUpgrade()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = tempDirectory
        };

        Directory.CreateDirectory(tempDirectory);
        await using (var connection = new SqliteConnection($"Data Source={options.DatabaseFilePath};"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE app_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO app_meta(key, value) VALUES ('schema_version', '6');
                CREATE TABLE wechat_group_chats (
                    id TEXT PRIMARY KEY,
                    account_id TEXT NOT NULL,
                    account_display_name TEXT NOT NULL DEFAULT '',
                    wechat_username TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    last_message_at TEXT NULL,
                    company_id TEXT NULL,
                    project_id TEXT NULL,
                    is_active INTEGER NOT NULL DEFAULT 1,
                    last_synced_at TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new ContactManagementSqliteStore(options, new ContactManagementValidationService());
        var snapshot = await store.LoadAsync();

        Assert.Empty(snapshot.WeChatGroupChats);
        await using var verifyConnection = new SqliteConnection($"Data Source={options.DatabaseFilePath};");
        await verifyConnection.OpenAsync();
        await using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "SELECT value FROM app_meta WHERE key='schema_version';";
        Assert.Equal("10", (string?)await verifyCommand.ExecuteScalarAsync());
    }
}
