using Lumine.Fairy.ContactManagement.App.ViewModels;
using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Models;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class ContactMainWindowViewModelFilterTests
{
    [Fact]
    public void RelationshipTypeOptions_IncludeSelf()
    {
        var viewModel = CreateViewModel(new ContactManagementSnapshot());

        Assert.Contains(ContactRelationshipType.Self, viewModel.RelationshipTypeOptions);

        var contact = ContactItemViewModel.FromModel(new ContactProfile
        {
            Id = "contact-self",
            FullName = "本人",
            RelationshipType = ContactRelationshipType.Self
        });
        Assert.Equal("自己", contact.RelationshipTypeText);
    }

    [Fact]
    public void FilterTexts_RestrictContactsCompaniesAndProjects()
    {
        var viewModel = CreateViewModel(new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile
                {
                    Id = "contact-zhangsan",
                    FullName = "张三",
                    Nicknames = ["老张"],
                    Phones = ["13800000001"],
                    Emails = ["zhangsan@example.com"],
                    Notes = "前端负责人"
                },
                new ContactProfile
                {
                    Id = "contact-lisi",
                    FullName = "李四",
                    Nicknames = ["小李"],
                    Phones = ["13800000002"],
                    Emails = ["lisi@example.com"],
                    Notes = "后端支持"
                }
            ],
            Companies =
            [
                new CompanyProfile
                {
                    Id = "company-star",
                    Name = "星云科技",
                    Departments = ["研发中心"],
                    Addresses = ["杭州"],
                    Notes = "重点客户"
                },
                new CompanyProfile
                {
                    Id = "company-river",
                    Name = "江河实业",
                    Departments = ["销售部"],
                    Addresses = ["苏州"],
                    Notes = "制造行业"
                }
            ],
            Projects =
            [
                new ProjectProfile
                {
                    Id = "project-fairy",
                    Name = "Fairy 桌面助理",
                    Tags = ["AI", "桌面端"],
                    GroupChats = ["Fairy 项目群"],
                    Notes = "桌面端升级"
                },
                new ProjectProfile
                {
                    Id = "project-erp",
                    Name = "ERP 改造",
                    Tags = ["企业"],
                    GroupChats = ["ERP 交付群"],
                    Notes = "供应链流程"
                }
            ]
        });

        viewModel.ContactFilterText = "老张";
        var contact = Assert.Single(viewModel.FilteredContacts);
        Assert.Equal("张三", contact.FullName);

        viewModel.CompanyFilterText = "研发中心";
        var company = Assert.Single(viewModel.FilteredCompanies);
        Assert.Equal("星云科技", company.Name);

        viewModel.ProjectFilterText = "桌面端";
        var project = Assert.Single(viewModel.FilteredProjects);
        Assert.Equal("Fairy 桌面助理", project.Name);
    }

    [Fact]
    public void FilterTexts_Cleared_RestoreFullLists()
    {
        var viewModel = CreateViewModel(new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile { Id = "contact-1", FullName = "张三" },
                new ContactProfile { Id = "contact-2", FullName = "李四" }
            ],
            Companies =
            [
                new CompanyProfile { Id = "company-1", Name = "公司A" },
                new CompanyProfile { Id = "company-2", Name = "公司B" }
            ],
            Projects =
            [
                new ProjectProfile { Id = "project-1", Name = "项目A" },
                new ProjectProfile { Id = "project-2", Name = "项目B" }
            ]
        });

        viewModel.ContactFilterText = "张";
        viewModel.CompanyFilterText = "A";
        viewModel.ProjectFilterText = "A";

        viewModel.ContactFilterText = string.Empty;
        viewModel.CompanyFilterText = string.Empty;
        viewModel.ProjectFilterText = string.Empty;

        Assert.Equal(2, viewModel.FilteredContacts.Count);
        Assert.Equal(2, viewModel.FilteredCompanies.Count);
        Assert.Equal(2, viewModel.FilteredProjects.Count);
    }

    private static ContactMainWindowViewModel CreateViewModel(ContactManagementSnapshot snapshot)
    {
        var workspaceService = new ContactManagementWorkspaceService(new InMemoryContactManagementStore(snapshot), new ContactManagementValidationService());
        return new ContactMainWindowViewModel(
            databaseFilePath: Path.Combine(Path.GetTempPath(), "fairy-contact-tests", "contact.db"),
            workspaceService: workspaceService,
            dataImportService: new StubDataImportService(),
            dataExportService: new StubDataExportService(),
            schemaVersion: 1,
            initialSnapshot: snapshot);
    }

    private sealed class InMemoryContactManagementStore(ContactManagementSnapshot snapshot) : IContactManagementStore
    {
        private ContactManagementSnapshot _snapshot = snapshot;

        public Task<ContactManagementSnapshot> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshot);

        public Task SaveAsync(ContactManagementSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshot = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class StubDataImportService : IDataImportService
    {
        public Task<ImportPreviewResult> PreviewAsync(ImportRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportPreviewResult());

        public Task<ImportCommitResult> CommitAsync(ImportCommitRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ImportCommitResult());
    }

    private sealed class StubDataExportService : IDataExportService
    {
        public Task ExportJsonAsync(ExportRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ExportExcelAsync(ExportRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
