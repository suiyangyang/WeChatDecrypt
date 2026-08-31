using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumine.Fairy.ContactManagement.App.ImportExport;
using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Lumine.Fairy.ContactManagement.App.ViewModels;

public sealed partial class ContactMainWindowViewModel : ObservableObject
{
    private const string AllFilterId = "all";
    private const string AssignedFilterId = "assigned";
    private const string UnassignedFilterId = "unassigned";
    private const string GroupChatFilterId = "group-chat";
    private const string ContactFilterId = "contact";

    private readonly ContactManagementWorkspaceService _workspaceService;
    private readonly IDataImportService _dataImportService;
    private readonly IDataExportService _dataExportService;
    private readonly int _schemaVersion;
    private readonly WeChatGroupChatDirectoryService? _groupChatDirectoryService;
    private RelationshipLinkItemViewModel? _trackedRelationship;

    public ContactMainWindowViewModel(
        string databaseFilePath,
        ContactManagementWorkspaceService workspaceService,
        IDataImportService dataImportService,
        IDataExportService dataExportService,
        int schemaVersion,
        ContactManagementSnapshot initialSnapshot,
        WeChatGroupChatDirectoryService? groupChatDirectoryService = null)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _dataImportService = dataImportService ?? throw new ArgumentNullException(nameof(dataImportService));
        _dataExportService = dataExportService ?? throw new ArgumentNullException(nameof(dataExportService));
        _schemaVersion = schemaVersion;
        _groupChatDirectoryService = groupChatDirectoryService;
        DataDirectoryText = $"数据库文件：{databaseFilePath}";

        var dataDirectory = Path.GetDirectoryName(databaseFilePath) ?? AppContext.BaseDirectory;
        ImportFilePath = Path.Combine(dataDirectory, "imports", "contact-management-import.json");
        ExportFilePath = Path.Combine(dataDirectory, "exports", $"contact-management-export-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        RelationshipTypeOptions = Enum.GetValues<ContactRelationshipType>();
        EntityKindOptions = Enum.GetValues<EntityKind>();
        RelationshipLinkTypeOptions = Enum.GetValues<RelationshipLinkType>();
        GroupChatAssignmentFilterOptions =
        [
            new LookupOptionViewModel { Id = AllFilterId, DisplayName = "全部" },
            new LookupOptionViewModel { Id = AssignedFilterId, DisplayName = "已分配" },
            new LookupOptionViewModel { Id = UnassignedFilterId, DisplayName = "未分配" }
        ];
        GroupChatTypeFilterOptions =
        [
            new LookupOptionViewModel { Id = AllFilterId, DisplayName = "全部类型" },
            new LookupOptionViewModel { Id = GroupChatFilterId, DisplayName = "群聊" },
            new LookupOptionViewModel { Id = ContactFilterId, DisplayName = "联系人" }
        ];
        SelectedGroupChatAssignmentFilter = GroupChatAssignmentFilterOptions[0];
        SelectedGroupChatTypeFilter = GroupChatTypeFilterOptions[0];

        ApplySnapshot(initialSnapshot);
        StatusText = "SQLite 数据已加载，可以开始维护联系人、公司、项目、微信联系人，并支持联系人域 JSON / Excel 导入导出。";
    }

    public string DataDirectoryText { get; }

    [ObservableProperty]
    private ContactManagementStatistics _statistics = new();

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _selectedMainTabIndex;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _contactFilterText = string.Empty;

    [ObservableProperty]
    private string _companyFilterText = string.Empty;

    [ObservableProperty]
    private string _projectFilterText = string.Empty;

    [ObservableProperty]
    private string _groupChatFilterText = string.Empty;

    [ObservableProperty]
    private LookupOptionViewModel? _selectedGroupChatAssignmentFilter;

    [ObservableProperty]
    private LookupOptionViewModel? _selectedGroupChatTypeFilter;

    [ObservableProperty]
    private string _importFilePath = string.Empty;

    [ObservableProperty]
    private string _exportFilePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportPreview))]
    [NotifyPropertyChangedFor(nameof(CanCommitImport))]
    [NotifyPropertyChangedFor(nameof(ImportPreviewSummary))]
    [NotifyPropertyChangedFor(nameof(PreviewStatistics))]
    [NotifyPropertyChangedFor(nameof(ImportErrorCount))]
    [NotifyPropertyChangedFor(nameof(ImportWarningCount))]
    [NotifyPropertyChangedFor(nameof(ImportInfoCount))]
    [NotifyPropertyChangedFor(nameof(HasImportErrors))]
    [NotifyPropertyChangedFor(nameof(HasImportWarnings))]
    [NotifyPropertyChangedFor(nameof(HasImportInfos))]
    [NotifyPropertyChangedFor(nameof(ImportDecisionHint))]
    private ImportPreviewResult? _importPreview;

    [ObservableProperty]
    private ContactItemViewModel? _selectedContact;

    [ObservableProperty]
    private CompanyItemViewModel? _selectedCompany;

    [ObservableProperty]
    private ProjectItemViewModel? _selectedProject;

    [ObservableProperty]
    private WeChatGroupChatItemViewModel? _selectedGroupChat;

    [ObservableProperty]
    private RelationshipLinkItemViewModel? _selectedRelationship;

    public ObservableCollection<ContactItemViewModel> Contacts { get; } = [];

    public ObservableCollection<ContactItemViewModel> FilteredContacts { get; } = [];

    public ObservableCollection<CompanyItemViewModel> Companies { get; } = [];

    public ObservableCollection<CompanyItemViewModel> FilteredCompanies { get; } = [];

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = [];

    public ObservableCollection<ProjectItemViewModel> FilteredProjects { get; } = [];

    public ObservableCollection<WeChatGroupChatItemViewModel> GroupChats { get; } = [];

    public ObservableCollection<WeChatGroupChatItemViewModel> FilteredGroupChats { get; } = [];

    public ObservableCollection<RelationshipLinkItemViewModel> RelationshipLinks { get; } = [];

    public ObservableCollection<LookupOptionViewModel> CompanyOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> ContactOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> ProjectOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> SourceEntityOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> TargetEntityOptions { get; } = [];

    public IReadOnlyList<ContactRelationshipType> RelationshipTypeOptions { get; }

    public IReadOnlyList<EntityKind> EntityKindOptions { get; }

    public IReadOnlyList<RelationshipLinkType> RelationshipLinkTypeOptions { get; }

    public IReadOnlyList<LookupOptionViewModel> GroupChatAssignmentFilterOptions { get; }

    public IReadOnlyList<LookupOptionViewModel> GroupChatTypeFilterOptions { get; }

    public ObservableCollection<ImportConflictItem> ImportConflicts { get; } = [];

    public ObservableCollection<ImportConflictItem> ImportErrors { get; } = [];

    public ObservableCollection<ImportConflictItem> ImportWarnings { get; } = [];

    public ObservableCollection<ImportConflictItem> ImportInfos { get; } = [];

    public ObservableCollection<ImportConflictDecisionItemViewModel> ImportWarningDecisions { get; } = [];

    public bool HasImportPreview => ImportPreview is not null;

    public bool CanCommitImport => ImportPreview?.IsValid == true;

    public ContactManagementStatistics PreviewStatistics => ImportPreview?.Statistics ?? new ContactManagementStatistics();

    public string ImportPreviewSummary => ImportPreview?.Summary ?? "尚未执行导入预检。";

    public int ImportErrorCount => ImportErrors.Count;

    public int ImportWarningCount => ImportWarnings.Count;

    public int ImportInfoCount => ImportInfos.Count;

    public bool HasImportErrors => ImportErrorCount > 0;

    public bool HasImportWarnings => ImportWarningCount > 0;

    public bool HasImportInfos => ImportInfoCount > 0;

    public bool IsHomePageSelected => SelectedMainTabIndex == 0;

    public bool IsContactsPageSelected => SelectedMainTabIndex == 1;

    public bool IsCompaniesPageSelected => SelectedMainTabIndex == 2;

    public bool IsProjectsPageSelected => SelectedMainTabIndex == 3;

    public bool IsGroupChatsPageSelected => SelectedMainTabIndex == 4;

    public bool IsRelationshipsPageSelected => false;

    public bool IsImportExportPageSelected => SelectedMainTabIndex == 5;

    public bool HasFilteredContacts => FilteredContacts.Count > 0;

    public bool HasFilteredCompanies => FilteredCompanies.Count > 0;

    public bool HasFilteredProjects => FilteredProjects.Count > 0;

    public bool HasFilteredGroupChats => FilteredGroupChats.Count > 0;

    public string ContactListSummaryText => BuildFilterSummaryText(FilteredContacts.Count, Contacts.Count, "条联系人", ContactFilterText);

    public string CompanyListSummaryText => BuildFilterSummaryText(FilteredCompanies.Count, Companies.Count, "家公司", CompanyFilterText);

    public string ProjectListSummaryText => BuildFilterSummaryText(FilteredProjects.Count, Projects.Count, "个项目", ProjectFilterText);

    public string GroupChatListSummaryText => BuildFilterSummaryText(
        FilteredGroupChats.Count,
        GroupChats.Count(item => item.IsActive),
        "个微信联系人",
        GroupChatFilterText,
        HasActiveGroupChatFilters);

    public string ContactEmptyStateText => BuildEmptyStateText("联系人", ContactFilterText);

    public string CompanyEmptyStateText => BuildEmptyStateText("公司", CompanyFilterText);

    public string ProjectEmptyStateText => BuildEmptyStateText("项目", ProjectFilterText);

    public string GroupChatEmptyStateText => GroupChats.All(item => !item.IsActive)
        ? "尚未同步微信联系人，请点击“刷新联系人”。"
        : HasActiveGroupChatFilters
            ? "当前筛选条件下没有匹配的微信联系人。"
            : BuildEmptyStateText("微信联系人", GroupChatFilterText);

    public string ImportDecisionHint => HasImportErrors
        ? "存在错误项，需先修正后才能提交导入。"
        : HasImportWarnings
            ? "当前仅有警告项，可选择保留导入或跳过这些项目，再提交。"
            : HasImportPreview
                ? "预检未发现异常，可直接提交导入。"
                : "尚未执行导入预检。";

    public void SetImportFilePath(string filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            ImportFilePath = filePath;
        }
    }

    public void SetExportFilePath(string filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            ExportFilePath = filePath;
        }
    }

    public void UpdateStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText = message;
        }
    }

    public Task SaveContactDraftAsync(ContactItemViewModel draft)
        => SaveDraftAsync(draft, draft.ToModel(), SelectionTarget.Contact, $"联系人“{draft.FullName}”已保存。", _workspaceService.UpsertContactAsync);

    public Task SaveCompanyDraftAsync(CompanyItemViewModel draft)
        => SaveDraftAsync(draft, draft.ToModel(), SelectionTarget.Company, $"公司“{draft.Name}”已保存。", _workspaceService.UpsertCompanyAsync);

    public Task SaveProjectDraftAsync(ProjectItemViewModel draft)
        => SaveDraftAsync(draft, draft.ToModel(), SelectionTarget.Project, $"项目“{draft.Name}”已保存。", _workspaceService.UpsertProjectAsync);

    public async Task SaveGroupChatDraftAsync(WeChatGroupChatItemViewModel draft)
    {
        if (_groupChatDirectoryService is null) throw new InvalidOperationException("微信联系人目录服务尚未初始化。");
        await SaveDraftAsync(draft, draft.ToModel(), SelectionTarget.GroupChat, $"微信联系人“{draft.DisplayName}”的公司归属已保存。", _groupChatDirectoryService.UpsertAsync);
    }

    public Task SaveRelationshipDraftAsync(RelationshipLinkItemViewModel draft)
        => SaveDraftAsync(draft, draft.ToModel(), SelectionTarget.Relationship, "关系记录已保存。", _workspaceService.UpsertRelationshipLinkAsync);

    [RelayCommand]
    private async Task RefreshGroupChatsAsync()
    {
        if (_groupChatDirectoryService is null)
        {
            StatusText = "微信联系人目录服务尚未初始化。";
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var snapshot = await _groupChatDirectoryService.RefreshAsync();
            ApplySnapshot(snapshot);
            StatusText = $"已同步 {GroupChats.Count(item => item.IsActive)} 个微信联系人和群聊。";
        });
    }

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        if (_groupChatDirectoryService is null)
        {
            StatusText = "微信联系人目录服务尚未初始化。";
            return;
        }

        var modifiedGroupChats = GroupChats
            .Where(static item => item.HasUnsavedChanges)
            .ToList();
        if (modifiedGroupChats.Count == 0)
        {
            StatusText = "当前没有待保存的修改。";
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            ContactManagementSnapshot? snapshot = null;
            foreach (var groupChat in modifiedGroupChats)
            {
                snapshot = await _groupChatDirectoryService.UpsertAsync(groupChat.ToModel());
            }

            if (snapshot is not null)
            {
                ApplySnapshot(snapshot);
            }

            StatusText = $"已保存 {modifiedGroupChats.Count} 个微信联系人修改。";
        });
    }

    [RelayCommand]
    private async Task AutoAssignGroupChatCompaniesAsync()
    {
        if (_groupChatDirectoryService is null)
        {
            StatusText = "微信联系人目录服务尚未初始化。";
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var activeGroupChats = GroupChats.Where(static item => item.IsActive).ToList();
            var unassignedGroupChats = activeGroupChats
                .Where(static item => string.IsNullOrWhiteSpace(item.CompanyId))
                .ToList();
            var companyOptions = Companies
                .Where(static company => company.IsActive && !string.IsNullOrWhiteSpace(company.Name))
                .Select(static company => new LookupOptionViewModel
                {
                    Id = company.Id,
                    DisplayName = company.DisplayName
                })
                .ToList();

            if (companyOptions.Count == 0)
            {
                StatusText = "当前没有可用于自动匹配的公司。";
                return;
            }

            var assignments = unassignedGroupChats
                .Select(item => (Item: item, Company: CompanyOptionSearch.FindBestMatch(item.DisplayName, companyOptions)))
                .Where(static item => item.Company is not null)
                .Select(static item => (item.Item, Company: item.Company!))
                .ToList();

            ContactManagementSnapshot? snapshot = null;
            foreach (var assignment in assignments)
            {
                var companyOption = assignment.Item.CompanyOptions.First(option =>
                    string.Equals(option.Id, assignment.Company.Id, StringComparison.OrdinalIgnoreCase));
                assignment.Item.SelectedCompanyOption = companyOption;
                snapshot = await _groupChatDirectoryService.UpsertAsync(assignment.Item.ToModel());
            }

            if (snapshot is not null)
            {
                ApplySnapshot(snapshot);
            }

            var alreadyAssignedCount = activeGroupChats.Count - unassignedGroupChats.Count;
            var unmatchedCount = unassignedGroupChats.Count - assignments.Count;
            StatusText = $"自动分配完成：已分配 {assignments.Count} 个，已有归属 {alreadyAssignedCount} 个，未匹配 {unmatchedCount} 个。";
        });
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            var snapshot = await _workspaceService.LoadAsync();
            ApplySnapshot(snapshot);
            StatusText = "已从 SQLite 重新加载联系人域数据。";
        });
    }

    [RelayCommand]
    private async Task PreviewImportAsync()
    {
        if (string.IsNullOrWhiteSpace(ImportFilePath))
        {
            StatusText = "请先填写导入文件路径。";
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var sourceKind = ResolveImportSourceKind(ImportFilePath);
            ImportPreview = await _dataImportService.PreviewAsync(new ImportRequest
            {
                SourceKind = sourceKind,
                FilePath = ImportFilePath
            });
            var sourceLabel = sourceKind == ImportSourceKind.Excel ? "Excel" : "JSON";
            StatusText = ImportPreview.IsValid ? $"{sourceLabel} 导入预检完成，可以选择提交。" : $"{sourceLabel} 导入预检未通过，请先处理错误项。";
        });
    }

    [RelayCommand]
    private void ClearImportPreview()
    {
        ImportPreview = null;
        StatusText = "已清空导入预检结果。";
    }

    [RelayCommand]
    private async Task CommitImportAsync()
    {
        if (ImportPreview is null)
        {
            StatusText = "请先执行导入预检。";
            return;
        }

        if (!ImportPreview.IsValid)
        {
            StatusText = "当前预检未通过，无法提交导入。";
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var result = await _dataImportService.CommitAsync(new ImportCommitRequest
            {
                PreviewResult = ImportPreview,
                ConflictResolutions = ImportWarningDecisions.Select(static item => item.ToDecision()).ToArray()
            });

            ApplySnapshot(await _workspaceService.LoadAsync());
            ImportPreview = null;
            StatusText = result.Summary;
        });
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportFilePath))
        {
            StatusText = "请先填写导出文件路径。";
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var targetKind = ResolveExportTargetKind(ExportFilePath);
            var request = new ExportRequest
            {
                TargetKind = targetKind,
                FilePath = ExportFilePath
            };

            if (targetKind == ExportTargetKind.Excel)
            {
                await _dataExportService.ExportExcelAsync(request);
                StatusText = $"已导出联系人域 Excel 文件：{ExportFilePath}";
                return;
            }

            await _dataExportService.ExportJsonAsync(request);
            StatusText = $"已导出联系人域 JSON 文件：{ExportFilePath}";
        });
    }

    [RelayCommand]
    private async Task GenerateImportTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportFilePath))
        {
            StatusText = "请先选择模板导出路径。";
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            await ContactDomainImportTemplateWriter.WriteExampleTemplateAsync(ExportFilePath, _schemaVersion);
            StatusText = $"已生成联系人域示例导入模板：{ExportFilePath}";
        });
    }

    [RelayCommand]
    private async Task SaveContactAsync()
    {
        if (SelectedContact is null)
        {
            StatusText = "请先选择联系人。";
            return;
        }

        await SaveContactDraftAsync(SelectedContact);
    }

    [RelayCommand]
    private async Task DeleteContactAsync()
    {
        if (SelectedContact is null)
        {
            StatusText = "请先选择联系人。";
            return;
        }

        var deletedName = SelectedContact.DisplayName;
        await ExecuteBusyAsync(async () =>
        {
            var snapshot = await _workspaceService.DeleteContactAsync(SelectedContact.Id);
            ApplySnapshot(snapshot);
            StatusText = $"联系人“{deletedName}”已删除，并同步清理关联引用。";
        });
    }

    [RelayCommand]
    private async Task SaveCompanyAsync()
    {
        if (SelectedCompany is null)
        {
            StatusText = "请先选择公司。";
            return;
        }

        await SaveCompanyDraftAsync(SelectedCompany);
    }

    [RelayCommand]
    private async Task DeleteCompanyAsync()
    {
        if (SelectedCompany is null)
        {
            StatusText = "请先选择公司。";
            return;
        }

        var deletedName = SelectedCompany.DisplayName;
        await ExecuteBusyAsync(async () =>
        {
            var snapshot = await _workspaceService.DeleteCompanyAsync(SelectedCompany.Id);
            ApplySnapshot(snapshot);
            StatusText = $"公司“{deletedName}”已删除，并同步清理项目和关系引用。";
        });
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        if (SelectedProject is null)
        {
            StatusText = "请先选择项目。";
            return;
        }

        await SaveProjectDraftAsync(SelectedProject);
    }

    [RelayCommand]
    private async Task DeleteProjectAsync()
    {
        if (SelectedProject is null)
        {
            StatusText = "请先选择项目。";
            return;
        }

        var deletedName = SelectedProject.DisplayName;
        await ExecuteBusyAsync(async () =>
        {
            var snapshot = await _workspaceService.DeleteProjectAsync(SelectedProject.Id);
            ApplySnapshot(snapshot);
            StatusText = $"项目“{deletedName}”已删除，并同步清理关系引用。";
        });
    }

    public async Task SaveGroupChatAsync(WeChatGroupChatItemViewModel? item)
    {
        if (item is null)
        {
            StatusText = "请先选择群聊。";
            return;
        }

        await SaveGroupChatDraftAsync(item);
    }

    [RelayCommand]
    private async Task SaveRelationshipAsync()
    {
        if (SelectedRelationship is null)
        {
            StatusText = "请先选择关系。";
            return;
        }

        await SaveRelationshipDraftAsync(SelectedRelationship);
    }

    [RelayCommand]
    private async Task DeleteRelationshipAsync()
    {
        if (SelectedRelationship is null)
        {
            StatusText = "请先选择关系。";
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            var snapshot = await _workspaceService.DeleteRelationshipLinkAsync(SelectedRelationship.Id);
            ApplySnapshot(snapshot);
            StatusText = "关系记录已删除。";
        });
    }

    partial void OnSelectedRelationshipChanged(RelationshipLinkItemViewModel? value)
    {
        if (_trackedRelationship is not null)
        {
            _trackedRelationship.PropertyChanged -= OnTrackedRelationshipPropertyChanged;
        }

        _trackedRelationship = value;

        if (_trackedRelationship is not null)
        {
            _trackedRelationship.PropertyChanged += OnTrackedRelationshipPropertyChanged;
        }

        RefreshRelationshipEntityOptions();
    }

    partial void OnContactFilterTextChanged(string value)
    {
        RefreshFilteredContacts();
        SelectedContact = ResolveSelection(FilteredContacts, item => item.Id, SelectedContact?.Id, null, false);
    }

    partial void OnCompanyFilterTextChanged(string value)
    {
        RefreshFilteredCompanies();
        SelectedCompany = ResolveSelection(FilteredCompanies, item => item.Id, SelectedCompany?.Id, null, false);
    }

    partial void OnProjectFilterTextChanged(string value)
    {
        RefreshFilteredProjects();
        SelectedProject = ResolveSelection(FilteredProjects, item => item.Id, SelectedProject?.Id, null, false);
    }

    partial void OnGroupChatFilterTextChanged(string value)
    {
        RefreshFilteredGroupChats();
        SelectedGroupChat = ResolveSelection(FilteredGroupChats, item => item.Id, SelectedGroupChat?.Id, null, false);
    }

    partial void OnSelectedGroupChatAssignmentFilterChanged(LookupOptionViewModel? value)
    {
        RefreshGroupChatFilterAndSelection();
    }

    partial void OnSelectedGroupChatTypeFilterChanged(LookupOptionViewModel? value)
    {
        RefreshGroupChatFilterAndSelection();
    }

    partial void OnSelectedMainTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsHomePageSelected));
        OnPropertyChanged(nameof(IsContactsPageSelected));
        OnPropertyChanged(nameof(IsCompaniesPageSelected));
        OnPropertyChanged(nameof(IsProjectsPageSelected));
        OnPropertyChanged(nameof(IsGroupChatsPageSelected));
        OnPropertyChanged(nameof(IsImportExportPageSelected));
    }

    partial void OnImportPreviewChanged(ImportPreviewResult? value)
    {
        var conflicts = value?.Conflicts ?? [];
        ReplaceCollection(ImportConflicts, conflicts);
        ReplaceCollection(ImportErrors, conflicts.Where(static item => item.Severity == ImportConflictSeverity.Error));
        ReplaceCollection(ImportWarnings, conflicts.Where(static item => item.Severity == ImportConflictSeverity.Warning));
        ReplaceCollection(ImportInfos, conflicts.Where(static item => item.Severity == ImportConflictSeverity.Info));
        ReplaceCollection(ImportWarningDecisions, conflicts
            .Where(static item => item.Severity == ImportConflictSeverity.Warning)
            .Select(static item => new ImportConflictDecisionItemViewModel(item)));
        OnPropertyChanged(nameof(ImportErrorCount));
        OnPropertyChanged(nameof(ImportWarningCount));
        OnPropertyChanged(nameof(ImportInfoCount));
        OnPropertyChanged(nameof(HasImportErrors));
        OnPropertyChanged(nameof(HasImportWarnings));
        OnPropertyChanged(nameof(HasImportInfos));
        OnPropertyChanged(nameof(ImportDecisionHint));
    }

    private void OnTrackedRelationshipPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RelationshipLinkItemViewModel.SourceEntityKind)
            or nameof(RelationshipLinkItemViewModel.TargetEntityKind))
        {
            RefreshRelationshipEntityOptions();
        }
    }

    private async Task ExecuteBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveDraftAsync<TViewModel, TModel>(
        TViewModel draft,
        TModel model,
        SelectionTarget selectionTarget,
        string successMessage,
        Func<TModel, CancellationToken, Task<ContactManagementSnapshot>> saveAction)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(saveAction);

        var preferredId = draft switch
        {
            ContactItemViewModel contact => contact.Id,
            CompanyItemViewModel company => company.Id,
            ProjectItemViewModel project => project.Id,
            WeChatGroupChatItemViewModel groupChat => groupChat.Id,
            RelationshipLinkItemViewModel relationship => relationship.Id,
            _ => string.Empty
        };

        await ExecuteBusyAsync(async () =>
        {
            var snapshot = await saveAction(model, CancellationToken.None);
            ApplySnapshot(snapshot, preferredId, selectionTarget);
            StatusText = successMessage;
        });
    }

    private void ApplySnapshot(ContactManagementSnapshot snapshot, string? preferredId = null, SelectionTarget selectionTarget = SelectionTarget.None)
    {
        Statistics = _workspaceService.BuildStatistics(ContactDomainSnapshotFilter.ToContactDomainSnapshot(snapshot));

        ReplaceCollection(Contacts, snapshot.Contacts.Select(ContactItemViewModel.FromModel));
        ReplaceCollection(Companies, snapshot.Companies.Select(CompanyItemViewModel.FromModel));
        ReplaceCollection(Projects, snapshot.Projects.Select(ProjectItemViewModel.FromModel));

        // Group chat rows resolve their selected company while being created, so
        // company lookup options must be ready before the rows are materialized.
        RefreshLookupOptions();

        ReplaceCollection(GroupChats, snapshot.WeChatGroupChats.Select(item => WeChatGroupChatItemViewModel.FromModel(item, CompanyOptions)));
        ReplaceCollection(RelationshipLinks, snapshot.RelationshipLinks.Select(RelationshipLinkItemViewModel.FromModel));

        RefreshFilteredContacts();
        RefreshFilteredCompanies();
        RefreshFilteredProjects();
        RefreshFilteredGroupChats();

        SelectedContact = ResolveSelection(FilteredContacts, item => item.Id, SelectedContact?.Id, preferredId, selectionTarget == SelectionTarget.Contact);
        SelectedCompany = ResolveSelection(FilteredCompanies, item => item.Id, SelectedCompany?.Id, preferredId, selectionTarget == SelectionTarget.Company);
        SelectedProject = ResolveSelection(FilteredProjects, item => item.Id, SelectedProject?.Id, preferredId, selectionTarget == SelectionTarget.Project);
        SelectedGroupChat = ResolveSelection(FilteredGroupChats, item => item.Id, SelectedGroupChat?.Id, preferredId, selectionTarget == SelectionTarget.GroupChat);
        SelectedRelationship = ResolveSelection(RelationshipLinks, item => item.Id, SelectedRelationship?.Id, preferredId, selectionTarget == SelectionTarget.Relationship);

        RefreshRelationshipEntityOptions();
    }

    private void RefreshFilteredContacts()
    {
        IEnumerable<ContactItemViewModel> results = Contacts;
        if (!string.IsNullOrWhiteSpace(ContactFilterText))
        {
            var keyword = ContactFilterText.Trim();
            results = results.Where(item =>
                item.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.FullName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.NicknamesText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.PhonesText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.EmailsText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.Notes.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.RelationshipTypeText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.ActiveStatusText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.CompanyIdsText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.ProjectIdsText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceCollection(FilteredContacts, results);
        RaiseFilteredContactsStateChanged();
    }

    private void RefreshFilteredCompanies()
    {
        IEnumerable<CompanyItemViewModel> results = Companies;
        if (!string.IsNullOrWhiteSpace(CompanyFilterText))
        {
            var keyword = CompanyFilterText.Trim();
            results = results.Where(item =>
                item.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.DepartmentsText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.AddressesText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.Notes.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.ActiveStatusText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceCollection(FilteredCompanies, results);
        RaiseFilteredCompaniesStateChanged();
    }

    private void RefreshFilteredProjects()
    {
        IEnumerable<ProjectItemViewModel> results = Projects;
        if (!string.IsNullOrWhiteSpace(ProjectFilterText))
        {
            var keyword = ProjectFilterText.Trim();
            results = results.Where(item =>
                item.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.TagsText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.GroupChatsText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.Notes.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.ActiveStatusText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.DeliveryDateText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || (item.CompanyId?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                || (item.OwnerContactId?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        ReplaceCollection(FilteredProjects, results);
        RaiseFilteredProjectsStateChanged();
    }

    private void RefreshFilteredGroupChats()
    {
        IEnumerable<WeChatGroupChatItemViewModel> results = GroupChats.Where(item => item.IsActive);

        if (string.Equals(SelectedGroupChatAssignmentFilter?.Id, AssignedFilterId, StringComparison.OrdinalIgnoreCase))
        {
            results = results.Where(static item => !string.IsNullOrWhiteSpace(item.CompanyId));
        }
        else if (string.Equals(SelectedGroupChatAssignmentFilter?.Id, UnassignedFilterId, StringComparison.OrdinalIgnoreCase))
        {
            results = results.Where(static item => string.IsNullOrWhiteSpace(item.CompanyId));
        }

        if (string.Equals(SelectedGroupChatTypeFilter?.Id, GroupChatFilterId, StringComparison.OrdinalIgnoreCase))
        {
            results = results.Where(static item => item.IsGroupChat);
        }
        else if (string.Equals(SelectedGroupChatTypeFilter?.Id, ContactFilterId, StringComparison.OrdinalIgnoreCase))
        {
            results = results.Where(static item => !item.IsGroupChat);
        }

        if (!string.IsNullOrWhiteSpace(GroupChatFilterText))
        {
            var keyword = GroupChatFilterText.Trim();
            results = results.Where(item => item.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.AccountDisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.TypeText.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.CompanyName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceCollection(FilteredGroupChats, results);
        RaiseFilteredGroupChatsStateChanged();
    }

    private void RaiseFilteredContactsStateChanged()
    {
        OnPropertyChanged(nameof(HasFilteredContacts));
        OnPropertyChanged(nameof(ContactListSummaryText));
        OnPropertyChanged(nameof(ContactEmptyStateText));
    }

    private void RaiseFilteredCompaniesStateChanged()
    {
        OnPropertyChanged(nameof(HasFilteredCompanies));
        OnPropertyChanged(nameof(CompanyListSummaryText));
        OnPropertyChanged(nameof(CompanyEmptyStateText));
    }

    private void RaiseFilteredProjectsStateChanged()
    {
        OnPropertyChanged(nameof(HasFilteredProjects));
        OnPropertyChanged(nameof(ProjectListSummaryText));
        OnPropertyChanged(nameof(ProjectEmptyStateText));
    }

    private void RaiseFilteredGroupChatsStateChanged()
    {
        OnPropertyChanged(nameof(HasFilteredGroupChats));
        OnPropertyChanged(nameof(GroupChatListSummaryText));
        OnPropertyChanged(nameof(GroupChatEmptyStateText));
    }

    private bool HasActiveGroupChatFilters => !string.IsNullOrWhiteSpace(GroupChatFilterText)
        || !string.Equals(SelectedGroupChatAssignmentFilter?.Id, AllFilterId, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(SelectedGroupChatTypeFilter?.Id, AllFilterId, StringComparison.OrdinalIgnoreCase);

    private void RefreshGroupChatFilterAndSelection()
    {
        RefreshFilteredGroupChats();
        SelectedGroupChat = ResolveSelection(FilteredGroupChats, item => item.Id, SelectedGroupChat?.Id, null, false);
    }

    private static string BuildFilterSummaryText(int filteredCount, int totalCount, string unitText, string filterText, bool hasAdvancedFilter = false)
    {
        return !hasAdvancedFilter && string.IsNullOrWhiteSpace(filterText)
            ? $"共 {totalCount} {unitText}"
            : $"筛选结果 {filteredCount} / {totalCount} {unitText}";
    }

    private static string BuildEmptyStateText(string entityText, string filterText)
    {
        return string.IsNullOrWhiteSpace(filterText)
            ? $"当前还没有{entityText}数据。"
            : $"没有匹配“{filterText.Trim()}”的{entityText}。";
    }

    private void RefreshLookupOptions()
    {
        ReplaceCollection(CompanyOptions, Companies.Select(static company => new LookupOptionViewModel
        {
            Id = company.Id,
            DisplayName = company.DisplayName
        }));

        foreach (var groupChat in GroupChats)
        {
            groupChat.SetCompanyOptions(CompanyOptions);
        }

        ReplaceCollection(ContactOptions, Contacts.Select(static contact => new LookupOptionViewModel
        {
            Id = contact.Id,
            DisplayName = contact.DisplayName
        }));

        ReplaceCollection(ProjectOptions, Projects.Select(static project => new LookupOptionViewModel
        {
            Id = project.Id,
            DisplayName = project.DisplayName
        }));
    }

    private void RefreshRelationshipEntityOptions()
    {
        var sourceKind = SelectedRelationship?.SourceEntityKind ?? EntityKind.Contact;
        var targetKind = SelectedRelationship?.TargetEntityKind ?? EntityKind.Company;

        ReplaceCollection(SourceEntityOptions, BuildEntityOptions(sourceKind));
        ReplaceCollection(TargetEntityOptions, BuildEntityOptions(targetKind));

        if (SelectedRelationship is null)
        {
            return;
        }

        if (!SourceEntityOptions.Any(x => string.Equals(x.Id, SelectedRelationship.SourceEntityId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedRelationship.SourceEntityId = SourceEntityOptions.FirstOrDefault()?.Id ?? string.Empty;
        }

        if (!TargetEntityOptions.Any(x => string.Equals(x.Id, SelectedRelationship.TargetEntityId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedRelationship.TargetEntityId = TargetEntityOptions.FirstOrDefault()?.Id ?? string.Empty;
        }
    }

    private IEnumerable<LookupOptionViewModel> BuildEntityOptions(EntityKind entityKind)
    {
        return entityKind switch
        {
            EntityKind.Contact => Contacts.Select(static contact => new LookupOptionViewModel { Id = contact.Id, DisplayName = contact.DisplayName }),
            EntityKind.Company => Companies.Select(static company => new LookupOptionViewModel { Id = company.Id, DisplayName = company.DisplayName }),
            EntityKind.Project => Projects.Select(static project => new LookupOptionViewModel { Id = project.Id, DisplayName = project.DisplayName }),
            _ => []
        };
    }

    private static TItem? ResolveSelection<TItem>(IEnumerable<TItem> items, Func<TItem, string> getId, string? currentId, string? preferredId, bool usePreferred)
        where TItem : class
    {
        var targetId = usePreferred ? preferredId : currentId;
        return items.FirstOrDefault(item => string.Equals(getId(item), targetId, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault();
    }

    private static ImportSourceKind ResolveImportSourceKind(string filePath)
        => string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase)
            ? ImportSourceKind.Excel
            : ImportSourceKind.Json;

    private static ExportTargetKind ResolveExportTargetKind(string filePath)
        => string.Equals(Path.GetExtension(filePath), ".xlsx", StringComparison.OrdinalIgnoreCase)
            ? ExportTargetKind.Excel
            : ExportTargetKind.Json;

    private static void ReplaceCollection<TItem>(ObservableCollection<TItem> target, IEnumerable<TItem> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private enum SelectionTarget
    {
        None = 0,
        Contact = 1,
        Company = 2,
        Project = 3,
        Relationship = 4,
        GroupChat = 5
    }
}
