using Lumine.Fairy.ContactManagement.App.ViewModels;
using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.Domain;
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

    [Fact]
    public void InitialSnapshot_PreservesGroupChatCompanyAssignment()
    {
        var viewModel = CreateViewModel(new ContactManagementSnapshot
        {
            Companies = [new CompanyProfile { Id = "company-1", Name = "示例公司" }],
            WeChatGroupChats = [new WeChatGroupChatProfile
            {
                Id = "group-1",
                AccountId = "account-1",
                WeChatUsername = "12345@chatroom",
                DisplayName = "示例群聊",
                CompanyId = "company-1"
            }]
        });

        var groupChat = Assert.Single(viewModel.GroupChats);
        Assert.Equal("company-1", groupChat.CompanyId);
        Assert.Equal("示例公司", groupChat.CompanyName);
        Assert.Equal(2, groupChat.FilteredCompanyOptions.Count);
        Assert.Equal("company-1", groupChat.SelectedCompanyOption?.Id);
    }

    [Fact]
    public void InactiveWechatEntries_AreNotShownInContactList()
    {
        var viewModel = CreateViewModel(new ContactManagementSnapshot
        {
            WeChatGroupChats =
            [
                new WeChatGroupChatProfile { Id = "active", DisplayName = "当前联系人", IsActive = true },
                new WeChatGroupChatProfile { Id = "inactive", DisplayName = "已移除联系人", IsActive = false }
            ]
        });

        Assert.Single(viewModel.FilteredGroupChats);
        Assert.Equal("当前联系人", viewModel.FilteredGroupChats[0].DisplayName);
        Assert.DoesNotContain(viewModel.FilteredGroupChats, item => item.DisplayName == "已移除联系人");
        Assert.DoesNotContain("已移除联系人", viewModel.GroupChatListSummaryText);
    }

    [Fact]
    public void GroupChatAdvancedFilters_FilterByAssignmentAndContactType()
    {
        var viewModel = CreateViewModel(new ContactManagementSnapshot
        {
            Companies = [new CompanyProfile { Id = "company-1", Name = "示例公司" }],
            WeChatGroupChats =
            [
                new WeChatGroupChatProfile { Id = "assigned-group", DisplayName = "已分配群聊", IsGroupChat = true, CompanyId = "company-1" },
                new WeChatGroupChatProfile { Id = "unassigned-group", DisplayName = "未分配群聊", IsGroupChat = true },
                new WeChatGroupChatProfile { Id = "assigned-contact", DisplayName = "已分配联系人", IsGroupChat = false, CompanyId = "company-1" },
                new WeChatGroupChatProfile { Id = "unassigned-contact", DisplayName = "未分配联系人", IsGroupChat = false }
            ]
        });

        Assert.Equal(4, viewModel.FilteredGroupChats.Count);

        viewModel.SelectedGroupChatAssignmentFilter = viewModel.GroupChatAssignmentFilterOptions.Single(item => item.DisplayName == "已分配");
        Assert.Equal(["已分配群聊", "已分配联系人"], viewModel.FilteredGroupChats.Select(item => item.DisplayName));

        viewModel.SelectedGroupChatTypeFilter = viewModel.GroupChatTypeFilterOptions.Single(item => item.DisplayName == "联系人");
        Assert.Equal(["已分配联系人"], viewModel.FilteredGroupChats.Select(item => item.DisplayName));

        viewModel.SelectedGroupChatAssignmentFilter = viewModel.GroupChatAssignmentFilterOptions.Single(item => item.DisplayName == "未分配");
        Assert.Equal([], viewModel.FilteredGroupChats.Where(item => item.IsGroupChat).Select(item => item.DisplayName));
        Assert.Equal(["未分配联系人"], viewModel.FilteredGroupChats.Select(item => item.DisplayName));

        viewModel.SelectedGroupChatTypeFilter = viewModel.GroupChatTypeFilterOptions.Single(item => item.DisplayName == "全部类型");
        Assert.Equal(["未分配群聊", "未分配联系人"], viewModel.FilteredGroupChats.Select(item => item.DisplayName));
    }

    [Fact]
    public async Task SaveAll_PersistsOnlyModifiedWechatEntries()
    {
        var initialSnapshot = new ContactManagementSnapshot
        {
            Companies =
            [
                new CompanyProfile { Id = "company-1", Name = "示例公司" },
                new CompanyProfile { Id = "company-2", Name = "另一家公司" }
            ],
            WeChatGroupChats =
            [
                new WeChatGroupChatProfile { Id = "modified", DisplayName = "待保存联系人" },
                new WeChatGroupChatProfile { Id = "unchanged", DisplayName = "未修改联系人", CompanyId = "company-1" }
            ]
        };
        var store = new InMemoryContactManagementStore(initialSnapshot);
        var workspaceService = new ContactManagementWorkspaceService(store, new ContactManagementValidationService());
        var viewModel = new ContactMainWindowViewModel(
            databaseFilePath: Path.Combine(Path.GetTempPath(), "fairy-contact-tests", "contact.db"),
            workspaceService: workspaceService,
            dataImportService: new StubDataImportService(),
            dataExportService: new StubDataExportService(),
            schemaVersion: 1,
            initialSnapshot: initialSnapshot,
            groupChatDirectoryService: new WeChatGroupChatDirectoryService(store, new EmptyGroupChatProvider()));

        var modified = viewModel.GroupChats.Single(item => item.Id == "modified");
        modified.SelectedCompanyOption = modified.CompanyOptions.Single(item => item.Id == "company-2");

        await viewModel.SaveAllCommand.ExecuteAsync(null);

        var saved = await store.LoadAsync();
        Assert.Equal("company-2", saved.WeChatGroupChats.Single(item => item.Id == "modified").CompanyId);
        Assert.Equal("company-1", saved.WeChatGroupChats.Single(item => item.Id == "unchanged").CompanyId);
        Assert.False(viewModel.GroupChats.Single(item => item.Id == "modified").HasUnsavedChanges);
        Assert.Contains("已保存 1 个", viewModel.StatusText);
    }

    [Fact]
    public async Task SavingNewCompany_RefreshesCompanyOptions()
    {
        var viewModel = CreateViewModel(new ContactManagementSnapshot());
        var company = CompanyItemViewModel.CreateNew();
        company.Id = "company-new";
        company.Name = "北辰设计";

        await viewModel.SaveCompanyDraftAsync(company);

        var option = Assert.Single(viewModel.CompanyOptions);
        Assert.Equal("company-new", option.Id);
        Assert.Equal("北辰设计", option.DisplayName);
    }

    [Theory]
    [InlineData("北辰设计", "beichen")]
    [InlineData("北辰设计", "beichensheji")]
    [InlineData("北辰设计", "辰设")]
    public void CompanyOptionSearch_MatchesChineseAndPinyin(string companyName, string keyword)
    {
        Assert.True(CompanyOptionSearch.Matches(companyName, keyword));
    }

    [Theory]
    [InlineData("北辰设计-张三", "company-north")]
    [InlineData("beichensheji-LiSi", "company-north")]
    [InlineData("🌟beichensheji-张三", "company-north")]
    [InlineData("赛那尔", "company-sainar")]
    [InlineData("峰鑫泽", "company-shouda-fengxinze")]
    public void CompanyOptionSearch_FindsCompanyInsideWechatName(string weChatName, string expectedCompanyId)
    {
        var options = new[]
        {
            new LookupOptionViewModel { Id = "company-north", DisplayName = "北辰设计" },
            new LookupOptionViewModel { Id = "company-north-long", DisplayName = "北辰设计上海分公司" },
            new LookupOptionViewModel { Id = "company-sainar", DisplayName = "赛纳尔" },
            new LookupOptionViewModel { Id = "company-shouda-fengxinze", DisplayName = "首达/峰鑫泽" }
        };

        var result = CompanyOptionSearch.FindBestMatch(weChatName, options);

        Assert.Equal(expectedCompanyId, result?.Id);
    }

    [Fact]
    public void CompanyOptionSearch_RejectsIncompletePinyinSequenceAndInitials()
    {
        var options = new[]
        {
            new LookupOptionViewModel { Id = "company-sainar", DisplayName = "赛纳尔" }
        };

        Assert.Null(CompanyOptionSearch.FindBestMatch("塞尔", options));
        Assert.Null(CompanyOptionSearch.FindBestMatch("赛", new[]
        {
            new LookupOptionViewModel { Id = "company-ai", DisplayName = "爱" }
        }));
        Assert.False(CompanyOptionSearch.Matches("北辰设计", "bcsj"));
    }

    [Fact]
    public async Task AutoAssignGroupChatCompanies_MatchesAndPersistsOnlyUnassignedActiveEntries()
    {
        var initialSnapshot = new ContactManagementSnapshot
        {
            Companies =
            [
                new CompanyProfile { Id = "company-north", Name = "北辰设计" },
                new CompanyProfile { Id = "company-star", Name = "星云科技" },
                new CompanyProfile { Id = "company-sainar", Name = "赛纳尔" },
                new CompanyProfile { Id = "company-shouda-fengxinze", Name = "首达/峰鑫泽" }
            ],
            WeChatGroupChats =
            [
                new WeChatGroupChatProfile { Id = "group-chinese", DisplayName = "北辰设计-张三", IsActive = true },
                new WeChatGroupChatProfile { Id = "group-pinyin", DisplayName = "xingyunkeji-李四", IsActive = true },
                new WeChatGroupChatProfile { Id = "group-sainar", DisplayName = "赛那尔", IsActive = true },
                new WeChatGroupChatProfile { Id = "group-se-er", DisplayName = "塞尔", IsActive = true },
                new WeChatGroupChatProfile { Id = "group-alias", DisplayName = "峰鑫泽", IsActive = true },
                new WeChatGroupChatProfile { Id = "group-existing", DisplayName = "星云科技-王五", CompanyId = "company-north", IsActive = true },
                new WeChatGroupChatProfile { Id = "group-inactive", DisplayName = "北辰设计-已移除", IsActive = false }
            ]
        };
        var store = new InMemoryContactManagementStore(initialSnapshot);
        var workspaceService = new ContactManagementWorkspaceService(store, new ContactManagementValidationService());
        var directoryService = new WeChatGroupChatDirectoryService(store, new EmptyGroupChatProvider());
        var viewModel = new ContactMainWindowViewModel(
            databaseFilePath: Path.Combine(Path.GetTempPath(), "fairy-contact-tests", "contact.db"),
            workspaceService: workspaceService,
            dataImportService: new StubDataImportService(),
            dataExportService: new StubDataExportService(),
            schemaVersion: 1,
            initialSnapshot: initialSnapshot,
            groupChatDirectoryService: directoryService);

        await viewModel.AutoAssignGroupChatCompaniesCommand.ExecuteAsync(null);

        var saved = await store.LoadAsync();
        Assert.Equal("company-north", saved.WeChatGroupChats.Single(item => item.Id == "group-chinese").CompanyId);
        Assert.Equal("company-star", saved.WeChatGroupChats.Single(item => item.Id == "group-pinyin").CompanyId);
        Assert.Equal("company-sainar", saved.WeChatGroupChats.Single(item => item.Id == "group-sainar").CompanyId);
        Assert.Null(saved.WeChatGroupChats.Single(item => item.Id == "group-se-er").CompanyId);
        Assert.Equal("company-shouda-fengxinze", saved.WeChatGroupChats.Single(item => item.Id == "group-alias").CompanyId);
        Assert.Equal("company-north", saved.WeChatGroupChats.Single(item => item.Id == "group-existing").CompanyId);
        Assert.Null(saved.WeChatGroupChats.Single(item => item.Id == "group-inactive").CompanyId);
        Assert.Contains("已分配 4 个", viewModel.StatusText);
    }

    [Fact]
    public void LookupOption_DisplaysCompanyNameWhenUsedAsComboBoxText()
    {
        var option = new LookupOptionViewModel { Id = "company-1", DisplayName = "示例公司" };

        Assert.Equal("示例公司", option.ToString());
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

    private sealed class EmptyGroupChatProvider : IWeChatGroupChatProvider
    {
        public Task<IReadOnlyList<WeChatGroupChatCandidate>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<WeChatGroupChatCandidate>>([]);
    }
}
