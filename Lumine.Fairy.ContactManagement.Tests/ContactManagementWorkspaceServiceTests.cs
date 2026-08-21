using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;
using Lumine.Fairy.ContactManagement.Models;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class ContactManagementWorkspaceServiceTests
{
    [Fact]
    public async Task DeleteContactAsync_RemovesRelationshipsAndClearsProjectOwner()
    {
        var service = CreateService(out _);
        await service.SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile { Id = "contact-1", FullName = "张三" },
                new ContactProfile { Id = "contact-2", FullName = "李四" }
            ],
            Projects =
            [
                new ProjectProfile { Id = "project-1", Name = "项目A", OwnerContactId = "contact-1", DeliveryDate = new DateOnly(2026, 4, 30) }
            ],
            RelationshipLinks =
            [
                new RelationshipLink
                {
                    Id = "link-1",
                    SourceEntityId = "contact-1",
                    SourceEntityKind = EntityKind.Contact,
                    TargetEntityId = "contact-2",
                    TargetEntityKind = EntityKind.Contact,
                    LinkType = RelationshipLinkType.Knows
                }
            ]
        });

        var snapshot = await service.DeleteContactAsync("contact-1");

        Assert.Single(snapshot.Contacts);
        Assert.Equal("contact-2", snapshot.Contacts[0].Id);
        var project = Assert.Single(snapshot.Projects);
        Assert.Null(project.OwnerContactId);
        Assert.Equal(new DateOnly(2026, 4, 30), project.DeliveryDate);
        Assert.Empty(snapshot.RelationshipLinks);
    }

    [Fact]
    public async Task DeleteCompanyAsync_RemovesRelationshipsAndClearsProjectCompany()
    {
        var service = CreateService(out _);
        await service.SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile { Id = "contact-1", FullName = "张三", CompanyIds = ["company-1"] }
            ],
            Companies =
            [
                new CompanyProfile { Id = "company-1", Name = "公司A" },
                new CompanyProfile { Id = "company-2", Name = "公司B" }
            ],
            Projects =
            [
                new ProjectProfile { Id = "project-1", Name = "项目A", CompanyId = "company-1", DeliveryDate = new DateOnly(2026, 5, 20) }
            ],
            RelationshipLinks =
            [
                new RelationshipLink
                {
                    Id = "link-1",
                    SourceEntityId = "company-1",
                    SourceEntityKind = EntityKind.Company,
                    TargetEntityId = "project-1",
                    TargetEntityKind = EntityKind.Project,
                    LinkType = RelationshipLinkType.ProjectBelongsToCompany
                }
            ]
        });

        var snapshot = await service.DeleteCompanyAsync("company-1");

        Assert.Single(snapshot.Companies);
        Assert.Equal("company-2", snapshot.Companies[0].Id);
        Assert.Empty(snapshot.Contacts[0].CompanyIds);
        var project = Assert.Single(snapshot.Projects);
        Assert.Null(project.CompanyId);
        Assert.Equal(new DateOnly(2026, 5, 20), project.DeliveryDate);
        Assert.Empty(snapshot.RelationshipLinks);
    }

    [Fact]
    public async Task UpsertContactAsync_SynchronizesAffiliationRelationships()
    {
        var service = CreateService(out _);
        await service.SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Companies =
            [
                new CompanyProfile { Id = "company-1", Name = "公司A" }
            ],
            Projects =
            [
                new ProjectProfile { Id = "project-1", Name = "项目A" }
            ]
        });

        var snapshot = await service.UpsertContactAsync(new ContactProfile
        {
            Id = "contact-1",
            FullName = "张三",
            CompanyIds = ["company-1"],
            ProjectIds = ["project-1"]
        });

        Assert.Equal(2, snapshot.RelationshipLinks.Count);
        Assert.Contains(snapshot.RelationshipLinks, item => item.LinkType == RelationshipLinkType.WorksAt && item.TargetEntityId == "company-1");
        Assert.Contains(snapshot.RelationshipLinks, item => item.LinkType == RelationshipLinkType.ParticipatesInProject && item.TargetEntityId == "project-1");
    }

    [Fact]
    public async Task DeleteProjectAsync_RemovesProjectFromContactSelections()
    {
        var service = CreateService(out _);
        await service.SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile { Id = "contact-1", FullName = "张三", ProjectIds = ["project-1"] }
            ],
            Projects =
            [
                new ProjectProfile { Id = "project-1", Name = "项目A" }
            ]
        });

        var snapshot = await service.DeleteProjectAsync("project-1");

        Assert.Empty(snapshot.Projects);
        Assert.Empty(snapshot.Contacts[0].ProjectIds);
    }

    [Fact]
    public async Task UpsertContactAsync_PreservesTodoSubsystemData()
    {
        var service = CreateService(out _);
        await service.SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile { Id = "contact-1", FullName = "张三" }
            ],
            TodoEntries =
            [
                new TodoEntry
                {
                    Id = "todo-1",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "Lumine.Fairy",
                    Title = "方案整理",
                    Content = "方案整理",
                    WorkHours = 2m,
                    IsDaily = true
                }
            ],
            ClientIssues =
            [
                new ClientIssueEntry
                {
                    Id = "issue-1",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "福立",
                    ClientName = "福立",
                    IssueLines = ["联机失败"]
                }
            ],
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 21),
                    TotalHours = 2m,
                    FormattedReportText = "1、(Lumine.Fairy)方案整理,2;"
                }
            ],
            ScratchNoteDocuments =
            [
                new ScratchNoteDocument
                {
                    Id = "note-1",
                    Title = "今日随手记",
                    RawText = "2026/3/21\n1.Lumine.Fairy: 方案整理 2"
                }
            ]
        });

        var snapshot = await service.UpsertContactAsync(new ContactProfile
        {
            Id = "contact-1",
            FullName = "张三（更新）"
        });

        Assert.Single(snapshot.TodoEntries);
        Assert.Single(snapshot.ClientIssues);
        Assert.Single(snapshot.DailyReports);
        Assert.Single(snapshot.ScratchNoteDocuments);
        Assert.Equal("张三（更新）", snapshot.Contacts[0].FullName);
    }

    private static ContactManagementWorkspaceService CreateService(out ContactManagementStorageOptions options)
    {
        options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"))
        };

        var store = new ContactManagementSqliteStore(options, new ContactManagementValidationService());
        return new ContactManagementWorkspaceService(store, new ContactManagementValidationService());
    }
}
