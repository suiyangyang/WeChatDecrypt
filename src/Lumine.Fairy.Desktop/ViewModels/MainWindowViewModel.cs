using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumine.Fairy.Application;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;
using Lumine.Fairy.Desktop.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.ContactManagement.App.ImportExport;
using Lumine.Fairy.ContactManagement.App.ViewModels;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Abstractions.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.WeChat;

namespace Lumine.Fairy.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    public event EventHandler? WorkPageRefreshing;
    public event EventHandler? WorkPageRefreshed;

    private readonly SqliteNotificationStore _store;
    private readonly SqliteTodoStore _todoStore;
    private readonly NotificationService _notificationService;
    private readonly TodoService _todoService;
    private readonly QwenPawClient _qwenPaw;
    private readonly WeChatDataAnalysisClient _wechat;
    private readonly WeChatAnalysisCoordinator _analysis;
    private readonly IncrementalAnalysisScheduler _scheduler;
    private readonly NotificationHttpServer _notificationApi;
    private readonly WeChatContextHttpServer? _wechatContextApi;
    private readonly AppSettingsStore _settingsStore;
    private readonly WindowsStartupService _startupService;
    private readonly ContactManagementSqliteStore _contactManagementStore;
    private readonly WeChatAnalysisContextResolver _wechatAnalysisContextResolver = new();
    private readonly TimeZoneContext _timeZoneContext;
    private readonly DispatcherTimer _clockTimer;
    private AppSettings _appSettings;
    private WeChatAnalysisSettings _wechatAnalysisSettings;
    private IReadOnlyList<WeChatWhitelistEntry> _wechatWhitelistCatalog = [];

    public ObservableCollection<NotificationItemViewModel> Notifications { get; } = [];
    public ObservableCollection<QwenPawAgent> Agents { get; } = [];
    public ObservableCollection<WechatWhitelistOptionViewModel> WechatWhitelistContacts { get; } = [];
    public ObservableCollection<WechatWhitelistOptionViewModel> WechatWhitelistCompanies { get; } = [];
    public ObservableCollection<AnalysisStageItemViewModel> AnalysisStages { get; } = [];
    public IReadOnlyList<TimeZoneOption> TimeZones { get; }
    public ObservableCollection<WorkItemViewModel> WorkItems { get; } = [];
    public ObservableCollection<TodoListViewModel> TodoLists { get; } = [];
    public ObservableCollection<TodoCompanyFilterOption> TodoCompanyFilters { get; } = [];
    public ObservableCollection<TodoCompanyFilterOption> FilteredTodoCompanyFilters { get; } = [];
    public ObservableCollection<NotificationItemViewModel> WorkNotifications { get; } = [];
    public IReadOnlyList<WorkTodoFilterOption> WorkTodoFilters { get; } =
    [
        new(WorkTodoFilter.All, "全部卡片"),
        new(WorkTodoFilter.Active, "待办"),
        new(WorkTodoFilter.InProgress, "进行中"),
        new(WorkTodoFilter.Completed, "已完成"),
        new(WorkTodoFilter.Overdue, "逾期"),
        new(WorkTodoFilter.Archived, "已归档"),
    ];
    public IReadOnlyList<WorkTodoSortOption> WorkTodoSorts { get; } =
    [
        new(WorkTodoSort.Position, "手动顺序"),
        new(WorkTodoSort.Priority, "按优先级"),
        new(WorkTodoSort.DueAt, "按截止时间"),
        new(WorkTodoSort.CreatedAt, "按创建时间"),
    ];
    public ContactMainWindowViewModel ContactManagementViewModel { get; }
    public IReadOnlyList<WechatAnalysisContentOptionViewModel> WechatAnalysisContentOptions { get; } =
    [
        new(WeChatAnalysisContentType.Todo, "ToDo"),
        new(WeChatAnalysisContentType.CustomerQuestion, "客户提问"),
    ];
    public IReadOnlyList<WechatAutomaticAnalysisModeOption> WechatAutomaticAnalysisModes { get; } =
    [
        new(WeChatAutomaticAnalysisMode.OneMonth, "1个月内的消息"),
        new(WeChatAutomaticAnalysisMode.CurrentWeek, "本周的消息"),
        new(WeChatAutomaticAnalysisMode.Recent50, "最近50条"),
        new(WeChatAutomaticAnalysisMode.Unanalyzed, "未分析过的消息"),
    ];

    [ObservableProperty] private string _currentPage = "通知中心";
    [ObservableProperty] private int _selectedPageIndex;
    [ObservableProperty] private string _serverUrl = "http://192.168.1.100:18803";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _statusMessage = "正在初始化本地 Agent…";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isQwenPawConnected;
    [ObservableProperty] private bool _isNotificationApiRunning;
    [ObservableProperty] private QwenPawAgent? _selectedAgent;
    [ObservableProperty] private bool _isWechatWhitelistEnabled;
    [ObservableProperty] private decimal? _wechatAnalysisIntervalMinutes;
    [ObservableProperty] private WechatAutomaticAnalysisModeOption? _selectedWechatAutomaticAnalysisMode;
    [ObservableProperty] private string _notificationApiEndpoint = "http://<Fairy-IP>:18765/api/v1/notifications";
    [ObservableProperty] private string _notificationApiToken = "local-dev-notification-token";
    [ObservableProperty] private bool _isNotificationApiTokenVisible;
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private int _selectedNotificationTabIndex;
    [ObservableProperty] private bool _hasUnreadNotifications;
    [ObservableProperty] private bool _hasMoreNotifications;
    [ObservableProperty] private bool _isNotificationSearchOpen;
    [ObservableProperty] private string _notificationSearchKeyword = string.Empty;
    [ObservableProperty] private bool _isNotificationMenuOpen;
    [ObservableProperty] private int _workUnreadCount;
    [ObservableProperty] private int _wechatUnreadCount;
    [ObservableProperty] private int _remoteUnreadCount;
    [ObservableProperty] private bool _hasWorkNotifications;
    [ObservableProperty] private bool _hasWorkItems;
    [ObservableProperty] private bool _isWorkPageEmpty = true;
    [ObservableProperty] private int _selectedWorkTabIndex;
    [ObservableProperty] private WorkTodoFilterOption? _selectedWorkTodoFilter;
    [ObservableProperty] private WorkTodoSortOption? _selectedWorkTodoSort;
    [ObservableProperty] private TodoCompanyFilterOption? _selectedTodoCompanyFilter;
    [ObservableProperty] private string _todoCompanyFilterText = string.Empty;
    [ObservableProperty] private string _todoSearchKeyword = string.Empty;
    [ObservableProperty] private string _newTodoListName = string.Empty;
    [ObservableProperty] private int _workPendingCount;
    [ObservableProperty] private int _workInProgressCount;
    [ObservableProperty] private int _workCompletedCount;
    [ObservableProperty] private int _workOverdueCount;
    [ObservableProperty] private int _workPendingConfirmationCount;
    [ObservableProperty] private TimeZoneOption? _selectedTimeZone;
    [ObservableProperty] private bool _isStartupEnabled;
    [ObservableProperty] private string _startupStatusText = string.Empty;
    [ObservableProperty] private string _analysisContextText = "尚未运行微信分析";
    [ObservableProperty] private string _analysisRoundSummaryText = "本轮尚未建立";
    [ObservableProperty] private string _analysisCurrentStageText = "等待开始";
    [ObservableProperty] private string _analysisOverallStatusText = "未开始";
    [ObservableProperty] private string _analysisLogText = string.Empty;
    [ObservableProperty] private bool _isAnalysisLogExpanded;
    private const int NotificationPageSize = 100;
    private int _notificationOffset;
    private bool _isLoadingNotifications;
    private bool _suppressStartupSettingChanged;
    private DateTimeOffset? _analysisStartedAt;
    private DateTimeOffset? _analysisEndedAt;
    private Action<NotificationRecord>? _desktopNotificationPresenter;
    private IReadOnlyList<TodoRecord> _allWorkItems = [];
    private IReadOnlyList<TodoListRecord> _allTodoLists = [];
    private TodoBoardRecord _todoBoard = new("default", "工作看板", "#1769D1");

    public MainWindowViewModel()
    {
        _settingsStore = new AppSettingsStore();
        _startupService = new WindowsStartupService();
        _appSettings = _settingsStore.Load();
        _wechatAnalysisSettings = _appSettings.WeChatAnalysis ?? new WeChatAnalysisSettings();
        IsWechatWhitelistEnabled = _wechatAnalysisSettings.EnableWhitelist;
        WechatAnalysisIntervalMinutes = WeChatAnalysisSettings.NormalizeAnalysisIntervalMinutes(_wechatAnalysisSettings.AnalysisIntervalMinutes);
        SelectedWechatAutomaticAnalysisMode = WechatAutomaticAnalysisModes.FirstOrDefault(x => x.Mode == _wechatAnalysisSettings.AutomaticMode)
            ?? WechatAutomaticAnalysisModes[0];
        foreach (var option in WechatAnalysisContentOptions)
            option.IsSelected = _wechatAnalysisSettings.ContentTypes.Contains(option.ContentType);
        _timeZoneContext = new TimeZoneContext(_appSettings.TimeZoneId);
        TimeZones = TimeZoneInfo.GetSystemTimeZones()
            .Select(zone => new TimeZoneOption(zone.Id, TimeZoneDisplayHelpers.GetDisplayName(zone), TimeZoneDisplayHelpers.GetOffsetText(zone)))
            .OrderByDescending(x => x.Id == _timeZoneContext.Id)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        SelectedTimeZone = TimeZones.FirstOrDefault(x => x.Id == _timeZoneContext.Id)
                           ?? TimeZones.FirstOrDefault(x => x.Id == TimeZoneContext.DefaultTimeZoneId)
                           ?? TimeZones.FirstOrDefault();
        _suppressStartupSettingChanged = true;
        IsStartupEnabled = _startupService.IsEnabled();
        _suppressStartupSettingChanged = false;
        StartupStatusText = IsStartupEnabled
            ? "开机启动已开启，Fairy 将在下次登录 Windows 时自动运行。"
            : "开机启动未开启。";
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(CurrentTimeText));
            OnPropertyChanged(nameof(AnalysisElapsedText));
            foreach (var stage in AnalysisStages) stage.RefreshDuration(DateTimeOffset.UtcNow);
        };
        _clockTimer.Start();
        SelectedWorkTodoFilter = WorkTodoFilters[0];
        SelectedWorkTodoSort = WorkTodoSorts[0];

        if (int.TryParse(ReadEnvironment("QwenPawInitialPage"), out var initialPage) && initialPage is >= 0 and <= 6)
            SelectedPageIndex = initialPage;
        if (int.TryParse(ReadEnvironment("QwenPawInitialNotificationTab"), out var initialNotificationTab) && initialNotificationTab is >= 0 and <= 2)
            SelectedNotificationTabIndex = initialNotificationTab;
        IsNotificationSearchOpen = ReadEnvironment("QwenPawInitialSearch") == "1";
        IsNotificationMenuOpen = ReadEnvironment("QwenPawInitialNotificationMenu") == "1";
        _store = new SqliteNotificationStore();
        _todoStore = new SqliteTodoStore();
        var contactStorageOptions = new ContactManagementStorageOptions();
        var contactValidationService = new ContactManagementValidationService();
        _contactManagementStore = new ContactManagementSqliteStore(contactStorageOptions, contactValidationService);
        _notificationService = new NotificationService(_store);
        _todoService = new TodoService(_todoStore);
        _qwenPaw = new QwenPawClient(new ProtectedTokenStore());
        _wechat = new WeChatDataAnalysisClient();
        var contactWorkspaceService = new ContactManagementWorkspaceService(_contactManagementStore, contactValidationService);
        IDataImportService contactImportService = new ContactDomainDataImportService(_contactManagementStore, contactStorageOptions, contactValidationService);
        IDataExportService contactExportService = new ContactDomainDataExportService(_contactManagementStore, contactStorageOptions);
        var contactGroupChatDirectoryService = new WeChatGroupChatDirectoryService(
            _contactManagementStore,
            new WeChatGroupChatProvider(_wechat));
        var contactSnapshot = contactWorkspaceService.LoadAsync().GetAwaiter().GetResult();
        ContactManagementViewModel = new ContactMainWindowViewModel(
            contactStorageOptions.DatabaseFilePath,
            contactWorkspaceService,
            contactImportService,
            contactExportService,
            contactStorageOptions.SchemaVersion,
            contactSnapshot,
            contactGroupChatDirectoryService);
        RefreshTodoCompanyFilters();
        _analysis = new WeChatAnalysisCoordinator(_wechat, _qwenPaw, _notificationService, _todoService, ResolveConversationCompanyAsync);
        _scheduler = new IncrementalAnalysisScheduler(_wechat, _qwenPaw, _analysis, _notificationService);
        _scheduler.WhitelistLoader = RefreshWechatWhitelistCatalogAsync;
        _wechatContextApi = CreateWechatContextApi();
        ApplyWechatAnalysisConfiguration(false);
        _notificationApi = new NotificationHttpServer(
            _notificationService,
            token: NotificationApiToken,
            analysis: _analysis,
            scheduler: _scheduler,
            bindAddress: ReadEnvironment("LumineFairyNotificationBindAddress"));
        NotificationApiEndpoint = _notificationApi.Endpoint;
        _notificationService.NotificationAdded += OnNotificationAdded;
        _todoService.TodoChanged += OnTodoChanged;
        _analysis.ProgressChanged += OnAnalysisProgressChanged;
        _ = InitializeAsync();
    }

    public void SetDesktopNotificationPresenter(Action<NotificationRecord> presenter)
        => _desktopNotificationPresenter = presenter;

    public string TimeZoneSummary => $"当前时区：{SelectedTimeZone?.DisplayName ?? _timeZoneContext.Current.DisplayName}（{TimeZoneDisplayHelpers.GetOffsetText(_timeZoneContext.Current)}）";

    public string CurrentTimeText => $"当前时间：{_timeZoneContext.FormatCurrent()}";

    public string AnalysisElapsedText => _analysisStartedAt is null
        ? "总耗时 00:00"
        : $"总耗时 {FormatDuration((_analysisEndedAt ?? DateTimeOffset.UtcNow) - _analysisStartedAt.Value)}";

    partial void OnSelectedTimeZoneChanged(TimeZoneOption? value)
    {
        if (value is null || value.Id == _timeZoneContext.Id) return;

        _timeZoneContext.SetTimeZone(value.Id);
        _appSettings.TimeZoneId = _timeZoneContext.Id;
        _settingsStore.Save(_appSettings);
        OnPropertyChanged(nameof(TimeZoneSummary));
        OnPropertyChanged(nameof(CurrentTimeText));
        foreach (var item in Notifications) item.RefreshTime();
        StatusMessage = $"已切换时区：{value.DisplayName}。";
    }

    partial void OnIsStartupEnabledChanged(bool value)
    {
        if (_suppressStartupSettingChanged)
            return;

        try
        {
            _startupService.SetEnabled(value);
            StartupStatusText = value
                ? "开机启动已开启，Fairy 将在下次登录 Windows 时自动运行。"
                : "开机启动已关闭。";
            StatusMessage = StartupStatusText;
        }
        catch (Exception ex)
        {
            _suppressStartupSettingChanged = true;
            IsStartupEnabled = !value;
            _suppressStartupSettingChanged = false;
            StartupStatusText = $"开机启动设置失败：{ex.Message}";
            StatusMessage = StartupStatusText;
        }
    }

    partial void OnSelectedAgentChanged(QwenPawAgent? value)
    {
        if (_scheduler is not null)
            _scheduler.AgentId = value?.Id ?? "default";
        if (_settingsStore is not null)
        {
            _appSettings.WeChatAnalysisAgentId = value?.Id;
            _settingsStore.Save(_appSettings);
        }
    }

    partial void OnSelectedNotificationTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAllNotificationTabSelected));
        OnPropertyChanged(nameof(IsUnreadNotificationTabSelected));
        OnPropertyChanged(nameof(IsReadNotificationTabSelected));
        _ = ReloadNotificationsAsync();
    }

    partial void OnSelectedWorkTodoFilterChanged(WorkTodoFilterOption? value)
    {
        RefreshWorkItems();
        OnPropertyChanged(nameof(WorkSummaryText));
        OnPropertyChanged(nameof(CanReorderTodos));
    }

    partial void OnSelectedWorkTodoSortChanged(WorkTodoSortOption? value)
    {
        RefreshWorkItems();
        OnPropertyChanged(nameof(CanReorderTodos));
    }

    partial void OnSelectedTodoCompanyFilterChanged(TodoCompanyFilterOption? value)
    {
        if (value is not null)
            TodoCompanyFilterText = value.Name;

        RefreshFilteredTodoCompanyFilters();
        RefreshWorkItems();
    }

    partial void OnTodoCompanyFilterTextChanged(string value)
    {
        RefreshFilteredTodoCompanyFilters();
        if (!string.Equals(value.Trim(), SelectedTodoCompanyFilter?.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            RefreshWorkItems();
    }

    partial void OnTodoSearchKeywordChanged(string value)
    {
        RefreshWorkItems();
        OnPropertyChanged(nameof(CanReorderTodos));
    }

    public bool IsAllNotificationTabSelected => SelectedNotificationTabIndex == 0;
    public bool IsUnreadNotificationTabSelected => SelectedNotificationTabIndex == 1;
    public bool IsReadNotificationTabSelected => SelectedNotificationTabIndex == 2;
    public bool IsNotificationCenterNavSelected => SelectedPageIndex == 0;

    public string PageTitle => SelectedPageIndex switch
    {
        0 => "通知中心",
        1 => "工作",
        2 => "微信分析",
        3 => "联系人管理",
        4 => "App",
        5 => "远程设备",
        6 => "设置",
        _ => "通知中心",
    };

    public string PageSubtitle => SelectedPageIndex switch
    {
        0 => "统一查看本地应用、微信分析和 QwenPaw 任务通知",
        1 => "聚合 TODO、待跟进事项和工作类通知",
        2 => "选择 QwenPaw Agent，分析最近微信会话并生成工作通知",
        3 => "维护联系人、公司、项目和微信联系人归属",
        4 => "打开 Todo / 日报等独立应用",
        5 => "设备连接、远程控制和设备管理",
        6 => "管理 QwenPaw 连接、本地通知接口和微信分析设置",
        _ => string.Empty,
    };

    partial void OnSelectedPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsNotificationPageSelected));
        OnPropertyChanged(nameof(IsWorkPageSelected));
        OnPropertyChanged(nameof(IsWechatPageSelected));
        OnPropertyChanged(nameof(IsContactManagementPageSelected));
        OnPropertyChanged(nameof(IsAppPageSelected));
        OnPropertyChanged(nameof(IsRemotePageSelected));
        OnPropertyChanged(nameof(IsSettingsPageSelected));
        OnPropertyChanged(nameof(IsNotificationCenterNavSelected));
        OnPropertyChanged(nameof(IsPageHeaderVisible));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        if (value == 1)
            _ = ReloadWorkPageAsync();
    }

    public bool IsNotificationPageSelected => SelectedPageIndex == 0;
    public bool IsWorkPageSelected => SelectedPageIndex == 1;
    public bool IsWechatPageSelected => SelectedPageIndex == 2;
    public bool IsContactManagementPageSelected => SelectedPageIndex == 3;
    public bool IsAppPageSelected => SelectedPageIndex == 4;
    public bool IsRemotePageSelected => SelectedPageIndex == 5;
    public bool IsSettingsPageSelected => SelectedPageIndex == 6;
    public bool IsPageHeaderVisible => !IsContactManagementPageSelected && !IsWorkPageSelected;

    public string WorkSummaryText
        => $"待办 {WorkPendingCount}  ·  进行中 {WorkInProgressCount}  ·  已完成 {WorkCompletedCount}  ·  逾期 {WorkOverdueCount}  ·  待确认 {WorkPendingConfirmationCount}";

    public bool CanReorderTodos
        => SelectedWorkTodoSort?.Sort == WorkTodoSort.Position
           && SelectedWorkTodoFilter?.Filter == WorkTodoFilter.All
           && string.IsNullOrWhiteSpace(TodoSearchKeyword);

    private async Task InitializeAsync()
    {
        try
        {
            await _notificationApi.StartAsync();
            IsNotificationApiRunning = true;
            if (_wechatContextApi is not null)
            {
                try
                {
                    await _wechatContextApi.StartAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = $"微信上下文 MCP 未启动：{ex.Message}";
                }
            }
            await ReloadNotificationsAsync();
            await ReloadWorkPageAsync();
            await LoadWechatWhitelistAsync();

            Username = ReadEnvironment("QwenPawUser");
            Password = ReadEnvironment("QwenPawPwd");
            if (!string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password))
                await LoginAsync();
            else
                StatusMessage = "本地 Agent 已运行，请登录 QwenPaw。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"初始化失败：{ex.Message}";
        }
    }

    private static string ReadEnvironment(string name)
        => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
           ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
           ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
           ?? string.Empty;

    private WeChatContextHttpServer? CreateWechatContextApi()
    {
        var settings = WeChatContextSettingsLoader.Load();
        if (string.IsNullOrWhiteSpace(settings.Token)) return null;

        return new WeChatContextHttpServer(settings.Host, settings.Port, settings.Token, async (request, cancellationToken) =>
        {
            var snapshot = await _contactManagementStore.LoadAsync(cancellationToken);
            var account = new ChatAccount(request.AccountId, string.Empty, 1);
            var session = new ChatSession(
                request.WeChatUsername,
                request.DisplayName,
                string.Empty,
                0,
                request.IsGroupChat ? ChatConversationKind.Group : ChatConversationKind.Unknown);
            return _wechatAnalysisContextResolver.Resolve(snapshot, account, session);
        });
    }

    [RelayCommand]
    private async Task RefreshWechatWhitelistAsync()
    {
        await LoadWechatWhitelistAsync();
        StatusMessage = $"已刷新联系人和公司：{WechatWhitelistContacts.Count} 位联系人、{WechatWhitelistCompanies.Count} 家公司。";
    }

    private async Task LoadWechatWhitelistAsync()
    {
        try
        {
            var snapshot = await _contactManagementStore.LoadAsync();
            var selectedContactIds = _wechatAnalysisSettings.WhitelistContactIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedCompanyIds = _wechatAnalysisSettings.WhitelistCompanyIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var contacts = snapshot.Contacts
                .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.FullName))
                .Select(x => new WechatWhitelistOptionViewModel("contact", x.Id, x.FullName, selectedContactIds.Contains(x.Id), x.Nicknames))
                .ToList();
            var companies = snapshot.Companies
                .Where(x => x.IsActive && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => new WechatWhitelistOptionViewModel("company", x.Id, x.Name, selectedCompanyIds.Contains(x.Id)))
                .ToList();
            var catalog = BuildWechatWhitelistCatalog(snapshot);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WechatWhitelistContacts.Clear();
                foreach (var contact in contacts) WechatWhitelistContacts.Add(contact);
                WechatWhitelistCompanies.Clear();
                foreach (var company in companies) WechatWhitelistCompanies.Add(company);
                _wechatWhitelistCatalog = catalog;
                _analysis.Whitelist = catalog;
                _scheduler.Whitelist = catalog;
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取联系人和公司失败：{ex.Message}";
        }
    }

    private async Task<IReadOnlyList<WeChatWhitelistEntry>> RefreshWechatWhitelistCatalogAsync(CancellationToken cancellationToken)
    {
        var catalog = BuildWechatWhitelistCatalog(await _contactManagementStore.LoadAsync(cancellationToken));
        _wechatWhitelistCatalog = catalog;
        _analysis.Whitelist = catalog;
        return catalog;
    }

    private async Task<ConversationCompanyContext?> ResolveConversationCompanyAsync(
        ChatAccount account,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        var snapshot = await _contactManagementStore.LoadAsync(cancellationToken);
        return _wechatAnalysisContextResolver.Resolve(snapshot, account, session);
    }

    private static IReadOnlyList<WeChatWhitelistEntry> BuildWechatWhitelistCatalog(ContactManagementSnapshot snapshot)
    {
        var activeContacts = snapshot.Contacts
            .Where(contact => contact.IsActive && !string.IsNullOrWhiteSpace(contact.FullName))
            .ToList();
        var activeCompanies = snapshot.Companies
            .Where(company => company.IsActive && !string.IsNullOrWhiteSpace(company.Name))
            .ToList();

        var projectsByCompany = snapshot.Projects
            .Where(project => project.IsActive)
            .Where(project => !string.IsNullOrWhiteSpace(project.CompanyId))
            .GroupBy(project => project.CompanyId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var projectsByContact = snapshot.Projects
            .Where(project => project.IsActive)
            .Where(project => !string.IsNullOrWhiteSpace(project.OwnerContactId))
            .GroupBy(project => project.OwnerContactId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var entries = activeContacts
            .Select(contact => new WeChatWhitelistEntry(
                contact.Id,
                "contact",
                contact.FullName,
                [contact.FullName, .. contact.Nicknames, .. GetProjectAliases(projectsByContact, contact.Id)],
                contact.WeChatIds))
            .ToList();

        entries.AddRange(activeCompanies.Select(company =>
        {
            var linkedContacts = activeContacts
                .Where(contact => contact.CompanyIds.Contains(company.Id, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var linkedProjects = projectsByCompany.TryGetValue(company.Id, out var companyProjects)
                ? companyProjects
                : [];
            var aliases = new[] { company.Name }
                .Concat(linkedContacts.SelectMany(contact => new[] { contact.FullName }.Concat(contact.Nicknames)))
                .Concat(linkedProjects.SelectMany(project => new[] { project.Name }.Concat(project.GroupChats)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var exactIds = linkedContacts.SelectMany(contact => contact.WeChatIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new WeChatWhitelistEntry(company.Id, "company", company.Name, aliases, exactIds);
        }));

        return entries;
    }

    private static IReadOnlyList<string> GetProjectAliases(
        IReadOnlyDictionary<string, List<ProjectProfile>> projectsByContact,
        string contactId)
        => projectsByContact.TryGetValue(contactId, out var projects)
            ? projects.SelectMany(project => new[] { project.Name }.Concat(project.GroupChats)).ToArray()
            : [];

    [RelayCommand]
    private void SaveWechatAnalysisSettings()
    {
        var contentTypes = WechatAnalysisContentOptions
            .Where(x => x.IsSelected)
            .Select(x => x.ContentType)
            .ToList();
        if (contentTypes.Count == 0)
        {
            StatusMessage = "请至少选择一种分析内容。";
            return;
        }

        var intervalMinutes = WechatAnalysisIntervalMinutes ?? WeChatAnalysisSettings.DefaultAnalysisIntervalMinutes;
        if (intervalMinutes != decimal.Truncate(intervalMinutes))
        {
            StatusMessage = "解析间隔必须是整数分钟。";
            return;
        }

        _wechatAnalysisSettings = new WeChatAnalysisSettings
        {
            EnableWhitelist = IsWechatWhitelistEnabled,
            WhitelistContactIds = WechatWhitelistContacts.Where(x => x.IsSelected).Select(x => x.Id).ToList(),
            WhitelistCompanyIds = WechatWhitelistCompanies.Where(x => x.IsSelected).Select(x => x.Id).ToList(),
            ContentTypes = contentTypes,
            AutomaticMode = SelectedWechatAutomaticAnalysisMode?.Mode ?? WeChatAutomaticAnalysisMode.OneMonth,
            AnalysisIntervalMinutes = WeChatAnalysisSettings.NormalizeAnalysisIntervalMinutes((int)intervalMinutes),
        };
        WechatAnalysisIntervalMinutes = _wechatAnalysisSettings.AnalysisIntervalMinutes;
        ApplyWechatAnalysisConfiguration(true);
        StatusMessage = "微信分析设置已保存。";
    }

    private void ApplyWechatAnalysisConfiguration(bool persist)
    {
        _appSettings.WeChatAnalysis = _wechatAnalysisSettings;
        _analysis.Settings = _wechatAnalysisSettings;
        _analysis.Whitelist = _wechatWhitelistCatalog;
        _scheduler.Settings = _wechatAnalysisSettings;
        _scheduler.Whitelist = _wechatWhitelistCatalog;
        if (persist) _settingsStore.Save(_appSettings);
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _qwenPaw.LoginAsync(ServerUrl, Username, Password);
            Password = string.Empty;
            IsQwenPawConnected = true;
            await RefreshAgentsAsync();
            _scheduler.Start();
            StatusMessage = $"已连接 QwenPaw，默认 Agent：{Agents.FirstOrDefault()?.Name ?? "未找到"}";
        }
        catch (Exception ex)
        {
            IsQwenPawConnected = false;
            StatusMessage = $"QwenPaw 登录失败：{ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshAgentsAsync()
    {
        if (!_qwenPaw.IsAuthenticated) return;
        try
        {
            var agents = await _qwenPaw.GetAgentsAsync();
            Agents.Clear();
            foreach (var agent in agents.Where(x => x.Enabled)) Agents.Add(agent);
            var configuredAgent = _appSettings.WeChatAnalysisAgentId;
            SelectedAgent ??= Agents.FirstOrDefault(x => string.Equals(x.Id, configuredAgent, StringComparison.OrdinalIgnoreCase))
                              ?? Agents.FirstOrDefault(x => string.Equals(x.Id, "WeChatAnalysist", StringComparison.OrdinalIgnoreCase))
                              ?? Agents.FirstOrDefault(x => string.Equals(x.Id, "default", StringComparison.OrdinalIgnoreCase))
                              ?? Agents.FirstOrDefault();
            _scheduler.AgentId = SelectedAgent?.Id ?? "default";
        }
        catch (Exception ex) { StatusMessage = $"读取 Agent 失败：{ex.Message}"; }
    }

    [RelayCommand]
    private async Task TestNotificationAsync()
    {
        await _notificationService.PublishAsync(new NotificationRequest(
            "通知测试成功",
            "本地通知中心已收到一条测试通知。",
            "local-test",
            "system",
            "success",
            "timeout",
            8,
            $"local-test:{DateTimeOffset.UtcNow:yyyyMMddHHmmss}"));
        StatusMessage = "已生成测试通知。";
    }

    [RelayCommand]
    private async Task AnalyzeWechatAsync()
    {
        if (IsBusy) return;
        SelectedPageIndex = 2;
        if (!_qwenPaw.IsAuthenticated) { StatusMessage = "请先登录 QwenPaw。"; return; }
        if (SelectedAgent is null) { StatusMessage = "请先选择 QwenPaw Agent。"; return; }
        IsBusy = true;
        StatusMessage = "正在读取最近微信会话并请求 QwenPaw 分析…";
        try
        {
            await RefreshWechatWhitelistCatalogAsync(CancellationToken.None);
            await _scheduler.AnalyzeNowAsync();
            await ReloadNotificationsAsync();
            await ReloadWorkPageAsync();
            StatusMessage = "微信分析完成，已发送本地通知。";
        }
        catch (Exception ex) { StatusMessage = $"微信分析失败：{ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SetLatestAnalysisPointAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "正在读取联系人和群聊的最新消息并设置分析点…";
        try
        {
            var count = await _scheduler.SetLatestAnalysisPointAsync();
            StatusMessage = count == 0
                ? "没有找到可设置分析点的联系人或群聊。"
                : $"已为 {count} 个联系人或群聊设置最新分析点；选择“未分析过的消息”后将从此处继续。";
        }
        catch (Exception ex) { StatusMessage = $"设置最新分析点失败：{ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenWechatAnalysisSettings()
        => SelectedPageIndex = 6;

    [RelayCommand]
    private async Task MarkReadAsync(NotificationRecord? item)
    {
        if (item is null || item.IsRead) return;
        if (!await _notificationService.MarkReadAsync(item.Id)) return;

        await Dispatcher.UIThread.InvokeAsync(() => MarkNotificationItemsRead(item.Id));
        await Task.WhenAll(ReloadNotificationsAsync(), ReloadWorkPageAsync());
    }

    private Task EditTodoAsync(TodoRecord? todo)
        => todo is null ? Task.CompletedTask : ShowTodoEditorAsync(todo, false);

    private async Task CreateTodoAsync(TodoListRecord? targetList)
    {
        var list = targetList
                   ?? _allTodoLists.FirstOrDefault(item => item.Status == "pending")
                   ?? new TodoListRecord("todo", _todoBoard.Id, "待办", 1, "pending");
        var now = DateTimeOffset.UtcNow;
        var todo = new TodoRecord(
            $"manual:{Guid.NewGuid():N}",
            string.Empty,
            string.Empty,
            "medium",
            null,
            null,
            1,
            Array.Empty<string>(),
            list.Status ?? "pending",
            now,
            now,
            null,
            "manual",
            string.Empty,
            BoardId: _todoBoard.Id,
            ListId: list.Id,
            Position: list.Position);
        await ShowTodoEditorAsync(todo, true);
    }

    [RelayCommand]
    private async Task AddTodoListAsync()
    {
        var name = NewTodoListName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "请输入列表名称。";
            return;
        }

        var list = new TodoListRecord(
            $"list:{Guid.NewGuid():N}",
            _todoBoard.Id,
            name,
            _allTodoLists.Count,
            null,
            false);
        if (await _todoService.AddListAsync(list) is null)
        {
            StatusMessage = "列表创建失败。";
            return;
        }

        NewTodoListName = string.Empty;
        await ReloadWorkPageAsync();
    }

    private async Task ShowTodoEditorAsync(TodoRecord todo, bool isNew)
    {
        if (IsBusy) return;

        var owner = (Avalonia.Application.Current?.ApplicationLifetime
                     as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var window = new Views.TodoEditWindow();
        var viewModel = new TodoEditWindowViewModel(
            todo,
            updated => isNew ? _todoService.AddCandidateAsync(updated) : SaveTodoFromEditorAsync(todo, updated),
            isNew,
            _allTodoLists
            .Where(list => !list.IsArchived || todo.IsArchived || list.Id == todo.ListId)
                .Select(list => new TodoListOption(list.Id, list.Name, list.Status))
                .ToList(),
            BuildTodoCompanyOptions(todo),
            result => window.Close(result));
        window.DataContext = viewModel;

        if (owner is null)
        {
            window.Show();
            return;
        }

        var updatedTodo = await window.ShowDialog<TodoRecord?>(owner);
        if (updatedTodo is not null)
            StatusMessage = "TODO 已更新。";
    }

    private async Task<TodoRecord?> SaveTodoFromEditorAsync(TodoRecord original, TodoRecord edited)
    {
        var moved = !string.Equals(original.BoardId, edited.BoardId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(original.ListId, edited.ListId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(original.Status, edited.Status, StringComparison.OrdinalIgnoreCase);
        var details = edited with
        {
            BoardId = original.BoardId,
            ListId = original.ListId,
            Position = original.Position,
            Status = original.Status,
        };
        var saved = await _todoService.UpdateDetailsAsync(details);
        if (saved is null || !moved) return saved;

        return await _todoService.MoveAsync(
            original.Id,
            edited.BoardId,
            edited.ListId,
            original.Position,
            edited.Status);
    }

    private async Task<TodoListRecord?> RenameTodoListAsync(TodoListRecord list, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var updated = await _todoService.UpdateListAsync(list with { Name = name.Trim() });
        if (updated is not null) await ReloadWorkPageAsync();
        return updated;
    }

    private async Task<bool> ArchiveTodoListAsync(TodoListRecord list, bool isArchived)
    {
        if (list.Status is not null) return false;
        var changed = await _todoService.ArchiveListAsync(list.Id, isArchived);
        if (changed) await ReloadWorkPageAsync();
        return changed;
    }

    private async Task<bool> DeleteTodoListAsync(TodoListRecord list)
    {
        if (list.Status is not null) return false;
        var count = _allWorkItems.Count(todo => ResolveListId(todo) == list.Id);
        var confirmed = await ShowConfirmationAsync(
            "删除列表",
            $"确定删除“{list.Name}”列表及其中 {count} 张卡片吗？此操作不可恢复。",
            "删除列表");
        if (!confirmed) return false;

        var changed = await _todoService.DeleteListAsync(list.Id);
        if (changed)
        {
            await ReloadWorkPageAsync();
            StatusMessage = $"已删除列表“{list.Name}”。";
        }
        return changed;
    }

    private async Task ArchiveTodoAsync(TodoRecord todo)
    {
        var updated = await _todoService.UpdateDetailsAsync(todo with
        {
            IsArchived = !todo.IsArchived,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        if (updated is not null)
        {
            await ReloadWorkPageAsync();
            StatusMessage = updated.IsArchived ? "卡片已归档。" : "卡片已恢复。";
        }
    }

    private async Task DeleteTodoAsync(TodoRecord todo)
    {
        var confirmed = await ShowConfirmationAsync("删除卡片", $"确定永久删除“{todo.Title}”吗？此操作不可恢复。", "删除卡片");
        if (!confirmed) return;
        if (await _todoService.DeleteAsync(todo.Id))
        {
            await ReloadWorkPageAsync();
            StatusMessage = "卡片已删除。";
        }
    }

    private async Task<bool> ShowConfirmationAsync(string title, string message, string confirmText)
    {
        var owner = (Avalonia.Application.Current?.ApplicationLifetime
                     as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return false;
        var window = new Views.ConfirmationWindow(title, message, confirmText);
        return await window.ShowDialog<bool>(owner);
    }

    private async Task HandleNotificationActionAsync(NotificationRecord notification, string actionId)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _analysis.HandleNotificationActionAsync(notification, actionId, SelectedAgent?.Id ?? "default");
            await ReloadNotificationsAsync();
            await ReloadWorkPageAsync();
            StatusMessage = "通知操作已完成。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"处理通知失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task MarkAllReadAsync()
    {
        await _notificationService.MarkAllReadAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var item in Notifications.Concat(WorkNotifications))
                item.MarkAsRead();
        });
        await Task.WhenAll(ReloadNotificationsAsync(), ReloadWorkPageAsync());
        StatusMessage = "已将全部通知标记为已读。";
    }

    [RelayCommand]
    private void NavigateToPage(string? page)
    {
        if (!int.TryParse(page, out var pageIndex)) return;
        SelectedPageIndex = Math.Clamp(pageIndex, 0, 6);
        if (SelectedPageIndex == 0)
            SelectedNotificationTabIndex = 0;
        IsNotificationSearchOpen = false;
        IsNotificationMenuOpen = false;
    }

    [RelayCommand]
    private void OpenContactManagementApp()
    {
        SelectedPageIndex = 3;
        StatusMessage = "已打开联系人管理。";
    }

    [RelayCommand]
    private void OpenTodoListApp()
        => LaunchBuiltInApp(BuiltInAppLauncher.LaunchTodoList, "Todo / 日报应用已启动。");

    private void LaunchBuiltInApp(Func<BuiltInAppLaunchResult> launchAction, string successMessage)
    {
        var result = launchAction();
        StatusMessage = result.Success ? successMessage : result.FailureReason;
    }

    [RelayCommand]
    private void SelectNotificationTab(string? tab)
    {
        if (!int.TryParse(tab, out var tabIndex)) return;
        SelectedPageIndex = 0;
        SelectedNotificationTabIndex = Math.Clamp(tabIndex, 0, 2);
        IsNotificationSearchOpen = false;
    }

    [RelayCommand]
    private void OpenNotificationSearch()
    {
        IsNotificationSearchOpen = true;
        IsNotificationMenuOpen = false;
    }

    [RelayCommand]
    private async Task SearchNotificationsAsync()
        => await ReloadNotificationsAsync();

    [RelayCommand]
    private void CloseNotificationSearch()
    {
        IsNotificationSearchOpen = false;
        NotificationSearchKeyword = string.Empty;
        _ = ReloadNotificationsAsync();
    }

    [RelayCommand]
    private void ToggleNotificationMenu() => IsNotificationMenuOpen = !IsNotificationMenuOpen;

    public string NotificationApiTokenVisibilityToolTip
        => IsNotificationApiTokenVisible ? "隐藏 Bearer Token" : "显示 Bearer Token";

    partial void OnIsNotificationApiTokenVisibleChanged(bool value)
        => OnPropertyChanged(nameof(NotificationApiTokenVisibilityToolTip));

    [RelayCommand]
    private void ToggleNotificationApiTokenVisibility()
        => IsNotificationApiTokenVisible = !IsNotificationApiTokenVisible;

    [RelayCommand]
    private async Task LoadMoreNotificationsAsync()
    {
        if (_isLoadingNotifications || !HasMoreNotifications) return;
        await LoadNotificationPageAsync(false);
    }

    [RelayCommand]
    private void Navigate(string? page)
    {
        if (!string.IsNullOrWhiteSpace(page)) CurrentPage = page;
    }

    private async Task ReloadWorkPageAsync()
    {
        _todoBoard = await _todoService.GetBoardAsync();
        var todosTask = _todoService.ListAsync(includeArchived: true);
        var listsTask = _todoService.ListListsAsync(_todoBoard.Id, includeArchived: true);
        var notificationsTask = _notificationService.ListAsync();
        await Task.WhenAll(todosTask, listsTask, notificationsTask);
        var todos = await todosTask;
        var lists = await listsTask;
        var workNotifications = (await notificationsTask)
            .Where(IsWorkPageNotification)
            .ToList();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WorkPageRefreshing?.Invoke(this, EventArgs.Empty);
            _allWorkItems = todos;
            _allTodoLists = lists;
            RefreshTodoCompanyFilters();
            RefreshWorkItems();
            var activeTodos = todos.Where(item => !item.IsArchived).ToList();
            WorkPendingCount = activeTodos.Count(item => item.Status.Equals("pending", StringComparison.OrdinalIgnoreCase));
            WorkInProgressCount = activeTodos.Count(item => item.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase));
            WorkCompletedCount = activeTodos.Count(item => item.Status.Equals("completed", StringComparison.OrdinalIgnoreCase));
            WorkOverdueCount = activeTodos.Count(IsOverdueTodo);
            WorkPendingConfirmationCount = activeTodos.Count(item => item.Status.Equals("pending_confirmation", StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged(nameof(WorkSummaryText));

            WorkNotifications.Clear();
            foreach (var notification in workNotifications)
                WorkNotifications.Add(CreateNotificationItem(notification));

            HasWorkNotifications = WorkNotifications.Count > 0;
            HasWorkItems = WorkItems.Count > 0;
            IsWorkPageEmpty = !HasWorkNotifications && !HasWorkItems;
            WorkPageRefreshed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void RefreshWorkItems()
    {
        WorkItems.Clear();
        TodoLists.Clear();
        var filter = SelectedWorkTodoFilter?.Filter ?? WorkTodoFilter.All;
        var filtered = FilterWorkItems(_allWorkItems, filter)
            .Where(MatchesTodoCompany)
            .Where(MatchesTodoSearch)
            .ToList();
        foreach (var todo in SortWorkItems(filtered, SelectedWorkTodoSort?.Sort ?? WorkTodoSort.Priority))
            WorkItems.Add(WorkItemViewModel.From(todo, EditTodoAsync, ArchiveTodoAsync, DeleteTodoAsync));

        foreach (var listRecord in _allTodoLists.Where(list => !list.IsArchived || filter == WorkTodoFilter.Archived))
        {
            var list = new TodoListViewModel(
                listRecord,
                RenameTodoListAsync,
                ArchiveTodoListAsync,
                DeleteTodoListAsync,
                CreateTodoAsync);
            foreach (var todo in SortWorkItems(
                         filtered.Where(item => ResolveListId(item) == listRecord.Id),
                         SelectedWorkTodoSort?.Sort ?? WorkTodoSort.Priority))
                list.Cards.Add(WorkItemViewModel.From(todo, EditTodoAsync, ArchiveTodoAsync, DeleteTodoAsync));
            TodoLists.Add(list);
        }

        HasWorkItems = TodoLists.Any(list => list.Cards.Count > 0);
        IsWorkPageEmpty = !HasWorkItems && !HasWorkNotifications;
    }

    private IEnumerable<TodoRecord> FilterWorkItems(IEnumerable<TodoRecord> todos, WorkTodoFilter filter)
        => filter switch
        {
            WorkTodoFilter.InProgress => todos.Where(item => item.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase)),
            WorkTodoFilter.Completed => todos.Where(item => item.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)),
            WorkTodoFilter.Overdue => todos.Where(IsOverdueTodo),
            WorkTodoFilter.Archived => todos.Where(item => item.IsArchived || _allTodoLists.Any(list => list.IsArchived && list.Id == ResolveListId(item))),
            WorkTodoFilter.All => todos.Where(item => !item.IsArchived),
            _ => todos.Where(item => !item.IsArchived
                                  && (item.Status.Equals("pending", StringComparison.OrdinalIgnoreCase)
                                  || item.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase))),
        };

    private bool MatchesTodoSearch(TodoRecord todo)
    {
        if (string.IsNullOrWhiteSpace(TodoSearchKeyword)) return true;
        var keyword = TodoSearchKeyword.Trim();
        return todo.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || todo.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (todo.Assignee?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
            || (todo.CompanyName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
            || todo.Priority.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesTodoCompany(TodoRecord todo)
    {
        var selected = SelectedTodoCompanyFilter;
        return selected is null || selected.Id == "*"
               || selected.Id is null && string.IsNullOrWhiteSpace(todo.CompanyId)
               || selected.Id is not null && selected.Id != "*" && string.Equals(todo.CompanyId, selected.Id, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshTodoCompanyFilters()
    {
        var hadPreviousSelection = SelectedTodoCompanyFilter is not null;
        var previous = SelectedTodoCompanyFilter?.Id;
        var options = new List<TodoCompanyFilterOption> { new("*", "全部公司") };
        options.AddRange(ContactManagementViewModel.Companies
            .Where(company => company.IsActive && !string.IsNullOrWhiteSpace(company.Name))
            .OrderBy(company => company.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(company => new TodoCompanyFilterOption(company.Id, company.DisplayName)));
        options.Add(new(null, "未设置公司"));

        TodoCompanyFilters.Clear();
        foreach (var option in options)
            TodoCompanyFilters.Add(option);
        SelectedTodoCompanyFilter = hadPreviousSelection
            ? TodoCompanyFilters.FirstOrDefault(option => option.Id == previous) ?? TodoCompanyFilters[0]
            : TodoCompanyFilters[0];
        TodoCompanyFilterText = SelectedTodoCompanyFilter.Name;
        RefreshFilteredTodoCompanyFilters();
    }

    private void RefreshFilteredTodoCompanyFilters()
    {
        var selectedOption = SelectedTodoCompanyFilter;
        var searchText = string.Equals(
            TodoCompanyFilterText.Trim(),
            selectedOption?.Name.Trim(),
            StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : TodoCompanyFilterText;
        var items = TodoCompanyFilters
            .Where(option => option == selectedOption
                || CompanyOptionSearch.Matches(option.Name, searchText))
            .ToList();

        for (var index = FilteredTodoCompanyFilters.Count - 1; index >= 0; index--)
        {
            if (!items.Contains(FilteredTodoCompanyFilters[index]))
                FilteredTodoCompanyFilters.RemoveAt(index);
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (index >= FilteredTodoCompanyFilters.Count || FilteredTodoCompanyFilters[index] != items[index])
            {
                if (FilteredTodoCompanyFilters.Contains(items[index]))
                    FilteredTodoCompanyFilters.Move(FilteredTodoCompanyFilters.IndexOf(items[index]), index);
                else
                    FilteredTodoCompanyFilters.Insert(index, items[index]);
            }
        }
    }

    private IReadOnlyList<TodoCompanyOption> BuildTodoCompanyOptions(TodoRecord todo)
    {
        var options = new List<TodoCompanyOption> { new(null, "未设置") };
        options.AddRange(ContactManagementViewModel.Companies
            .Where(company => company.IsActive && !string.IsNullOrWhiteSpace(company.Name))
            .OrderBy(company => company.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(company => new TodoCompanyOption(company.Id, company.DisplayName)));
        if (!string.IsNullOrWhiteSpace(todo.CompanyId) && options.All(option => option.Id != todo.CompanyId))
            options.Add(new(todo.CompanyId, todo.CompanyName ?? "已删除公司"));
        return options;
    }

    private string ResolveListId(TodoRecord todo)
    {
        if (_allTodoLists.Any(list => list.Id == todo.ListId)) return todo.ListId;
        return todo.Status switch
        {
            "in_progress" => "in-progress",
            "completed" => "done",
            _ => "todo",
        };
    }

    public async Task MoveTodoAsync(string todoId, string listId, int position)
    {
        var targetList = _allTodoLists.FirstOrDefault(list => list.Id == listId);
        var todo = _allWorkItems.FirstOrDefault(item => item.Id == todoId);
        if (targetList is null || todo is null) return;

        var status = targetList.Status ?? todo.Status;
        if (await _todoService.MoveAsync(todoId, targetList.BoardId, targetList.Id, position, status) is not null)
        {
            await ReloadWorkPageAsync();
            StatusMessage = "卡片已移动。";
        }
    }

    public async Task MoveTodoListAsync(string listId, int position)
    {
        var list = _allTodoLists.FirstOrDefault(item => item.Id == listId);
        if (list is null || list.IsArchived) return;
        if (await _todoService.MoveListAsync(list.Id, list.BoardId, position) is not null)
        {
            await ReloadWorkPageAsync();
            StatusMessage = "列表已移动。";
        }
    }

    private static IEnumerable<TodoRecord> SortWorkItems(IEnumerable<TodoRecord> todos, WorkTodoSort sort)
        => sort switch
        {
            WorkTodoSort.Position => todos
                .OrderBy(item => item.Position)
                .ThenByDescending(item => item.CreatedAt),
            WorkTodoSort.DueAt => todos
                .OrderBy(item => item.DueAt is null)
                .ThenBy(item => item.DueAt)
                .ThenByDescending(item => item.CreatedAt),
            WorkTodoSort.CreatedAt => todos.OrderByDescending(item => item.CreatedAt),
            _ => todos
                .OrderBy(item => PriorityRank(item.Priority))
                .ThenBy(item => item.DueAt is null)
                .ThenBy(item => item.DueAt)
                .ThenByDescending(item => item.CreatedAt),
        };

    private static int PriorityRank(string priority)
        => priority.Trim().ToLowerInvariant() switch
        {
            "urgent" or "critical" => 0,
            "high" or "高" => 1,
            "medium" or "normal" or "中" => 2,
            "low" or "低" => 3,
            _ => 4,
        };

    private static bool IsOverdueTodo(TodoRecord todo)
        => todo.Status is not "completed"
           && todo.DueAt is not null
           && todo.DueAt.Value < DateTimeOffset.UtcNow;

    private static bool IsWorkPageNotification(NotificationRecord notification)
        => notification.Category.Equals("work", StringComparison.OrdinalIgnoreCase)
           && !notification.IsResolved
           && (notification.Actions.Count > 0 || !notification.IsRead);

    private NotificationItemViewModel CreateNotificationItem(NotificationRecord notification)
        => new(notification, _timeZoneContext, HandleNotificationActionAsync, MarkReadAsync);

    private void MarkNotificationItemsRead(string notificationId)
    {
        foreach (var item in Notifications.Concat(WorkNotifications)
                     .Where(item => string.Equals(item.Id, notificationId, StringComparison.OrdinalIgnoreCase)))
            item.MarkAsRead();
    }

    private async Task ReloadNotificationsAsync()
    {
        await LoadNotificationPageAsync(true);
    }

    private async Task LoadNotificationPageAsync(bool reset)
    {
        if (_isLoadingNotifications) return;
        _isLoadingNotifications = true;
        if (reset)
        {
            _notificationOffset = 0;
            HasMoreNotifications = true;
        }

        var unreadOnly = SelectedNotificationTabIndex == 1;
        var readOnly = SelectedNotificationTabIndex == 2;
        var items = await _notificationService.ListPageAsync(
            string.IsNullOrWhiteSpace(NotificationSearchKeyword) ? null : NotificationSearchKeyword,
            unreadOnly,
            readOnly,
            _notificationOffset,
            NotificationPageSize);
        var unreadItems = await _notificationService.ListAsync(unreadOnly: true);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (reset) Notifications.Clear();
            foreach (var item in items)
                Notifications.Add(CreateNotificationItem(item));
            _notificationOffset += items.Count;
            HasMoreNotifications = items.Count == NotificationPageSize;
            UnreadCount = unreadItems.Count(x => !x.IsResolved);
            HasUnreadNotifications = UnreadCount > 0;
            WorkUnreadCount = unreadItems.Count(x => !x.IsResolved && x.Category.Equals("work", StringComparison.OrdinalIgnoreCase));
            WechatUnreadCount = unreadItems.Count(x => !x.IsResolved && x.Category.Equals("wechat", StringComparison.OrdinalIgnoreCase));
            RemoteUnreadCount = unreadItems.Count(x => !x.IsResolved && x.Category.Equals("remote", StringComparison.OrdinalIgnoreCase));
        });
        _isLoadingNotifications = false;
    }

    private void OnNotificationAdded(object? sender, NotificationRecord notification)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await ReloadNotificationsAsync();
            await ReloadWorkPageAsync();
            StatusMessage = $"新通知：{notification.Title}";
            ShowToast(notification);
        });
    }

    private void OnAnalysisProgressChanged(object? sender, AnalysisProgressSnapshot progress)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            _analysisStartedAt = progress.StartedAt;
            _analysisEndedAt = progress.Stages.Any(x => x.Status == "error")
                || progress.Stages.Any(x => x.Id == "complete" && x.Status == "completed")
                ? DateTimeOffset.UtcNow
                : null;
            AnalysisContextText = string.IsNullOrWhiteSpace(progress.RoundTitle)
                ? $"账号：{progress.AccountDisplayName}  ·  会话：{progress.ConversationDisplayName}"
                : $"{progress.RoundTitle}  ·  账号：{progress.AccountDisplayName}";
            AnalysisRoundSummaryText = progress.TotalContacts <= 0
                ? "正在收集本轮新增消息…"
                : $"本轮共 {progress.TotalContacts} 个联系人、{progress.TotalMessages} 条消息；当前处理第 {progress.CurrentContactIndex}/{progress.TotalContacts} 个联系人：{progress.ConversationDisplayName}（已完成 {progress.CompletedContacts} 个）"
                  + (string.IsNullOrWhiteSpace(progress.Notice) ? string.Empty : $"；{progress.Notice}");
            AnalysisStages.Clear();
            foreach (var stage in progress.Stages)
                AnalysisStages.Add(new AnalysisStageItemViewModel(stage));

            var current = progress.Stages.FirstOrDefault(x => x.Status == "running")
                          ?? progress.Stages.FirstOrDefault(x => x.Status == "error")
                          ?? progress.Stages.LastOrDefault(x => x.Status == "completed");
            AnalysisCurrentStageText = current is null ? "等待开始" : $"当前阶段：{current.Title}";
            AnalysisOverallStatusText = progress.Stages.Any(x => x.Status == "error")
                ? "执行失败"
                : progress.Stages.All(x => x.Status == "completed")
                    ? "已完成"
                    : "执行中";
            AnalysisLogText = string.Join(Environment.NewLine, progress.Stages
                .Where(x => x.Status != "pending" || !string.Equals(x.Detail, "等待开始", StringComparison.Ordinal))
                .Select(x => $"[{FormatTime(x.StartedAt ?? progress.StartedAt)}] {x.Title}：{x.Detail}{(string.IsNullOrWhiteSpace(x.Error) ? string.Empty : $" | 错误：{x.Error}")}"));
            OnPropertyChanged(nameof(AnalysisElapsedText));
        });
    }

    private void OnTodoChanged(object? sender, TodoRecord todo)
    {
        _ = ReloadWorkPageAsync();
    }

    private void ShowToast(NotificationRecord notification)
    {
        _desktopNotificationPresenter?.Invoke(notification);
    }

    public async ValueTask DisposeAsync()
    {
        _clockTimer.Stop();
        _notificationService.NotificationAdded -= OnNotificationAdded;
        _todoService.TodoChanged -= OnTodoChanged;
        _analysis.ProgressChanged -= OnAnalysisProgressChanged;
        await _notificationApi.DisposeAsync();
        if (_wechatContextApi is not null)
            await _wechatContextApi.DisposeAsync();
        await _scheduler.DisposeAsync();
        _wechat.Dispose();
        _qwenPaw.Dispose();
        _store.Dispose();
        _todoStore.Dispose();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }

    private static string FormatTime(DateTimeOffset time)
        => time.ToLocalTime().ToString("HH:mm:ss");
}

public sealed class WechatWhitelistOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public WechatWhitelistOptionViewModel(string kind, string id, string name, bool isSelected, IReadOnlyList<string>? aliases = null)
    {
        Kind = kind;
        Id = id;
        Name = name;
        Aliases = aliases ?? [];
        _isSelected = isSelected;
    }

    public string Kind { get; }
    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<string> Aliases { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class WechatAnalysisContentOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public WechatAnalysisContentOptionViewModel(WeChatAnalysisContentType contentType, string name)
    {
        ContentType = contentType;
        Name = name;
    }

    public WeChatAnalysisContentType ContentType { get; }
    public string Name { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record WechatAutomaticAnalysisModeOption(WeChatAutomaticAnalysisMode Mode, string Name);

public enum WorkTodoFilter
{
    Active,
    InProgress,
    Completed,
    All,
    Overdue,
    Archived,
}

public sealed record WorkTodoFilterOption(WorkTodoFilter Filter, string Name);

public enum WorkTodoSort
{
    Position,
    Priority,
    DueAt,
    CreatedAt,
}

public sealed record WorkTodoSortOption(WorkTodoSort Sort, string Name);

public sealed record TodoCompanyFilterOption(string? Id, string Name);

public sealed class TodoListViewModel : ObservableObject
{
    private readonly TodoListRecord _original;
    private readonly Func<TodoListRecord, string, Task<TodoListRecord?>> _renameHandler;
    private readonly Func<TodoListRecord, bool, Task<bool>> _archiveHandler;
    private readonly Func<TodoListRecord, Task<bool>> _deleteHandler;
    private readonly Func<TodoListRecord, Task> _createCardHandler;
    private string _name;
    private bool _isEditingName;

    public TodoListViewModel(
        TodoListRecord list,
        Func<TodoListRecord, string, Task<TodoListRecord?>> renameHandler,
        Func<TodoListRecord, bool, Task<bool>> archiveHandler,
        Func<TodoListRecord, Task<bool>> deleteHandler,
        Func<TodoListRecord, Task> createCardHandler)
    {
        _original = list;
        _name = list.Name;
        _renameHandler = renameHandler;
        _archiveHandler = archiveHandler;
        _deleteHandler = deleteHandler;
        _createCardHandler = createCardHandler;
        Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CardCountText));
        ArchiveCommand = new AsyncRelayCommand(ArchiveAsync, () => CanArchive || CanRestore);
        DeleteCommand = new AsyncRelayCommand(() => _deleteHandler(_original), () => CanDelete);
        BeginRenameCommand = new RelayCommand(() => IsEditingName = true, () => CanRename);
        AddCardCommand = new AsyncRelayCommand(() => _createCardHandler(_original));
    }

    public ObservableCollection<WorkItemViewModel> Cards { get; } = [];
    public TodoListRecord Record => _original;
    public IAsyncRelayCommand ArchiveCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IRelayCommand BeginRenameCommand { get; }
    public IAsyncRelayCommand AddCardCommand { get; }
    public string Id => _original.Id;
    public string Status => _original.Status switch
    {
        "pending_confirmation" => "待确认",
        "pending" => "待办",
        "in_progress" => "进行中",
        "completed" => "已完成",
        _ => string.Empty,
    };
    public bool CanRename => !string.Equals(_original.Id, "pending-confirmation", StringComparison.OrdinalIgnoreCase);
    public bool CanArchive => _original.Status is null && !_original.IsArchived;
    public bool CanRestore => _original.Status is null && _original.IsArchived;
    public bool CanDelete => _original.Status is null;
    public string ArchiveActionText => _original.IsArchived ? "恢复" : "归档";
    public string CardCountText => $"{Cards.Count} 张卡片";
    public bool IsEditingName
    {
        get => _isEditingName;
        set => SetProperty(ref _isEditingName, value);
    }

    public string Name
    {
        get => _name;
        set
        {
            SetProperty(ref _name, value);
        }
    }

    public async Task SaveNameAsync()
    {
        if (string.IsNullOrWhiteSpace(Name)) return;
        var updated = await _renameHandler(_original, Name);
        if (updated is not null)
        {
            _name = updated.Name;
            OnPropertyChanged(nameof(Name));
        }
        IsEditingName = false;
    }

    private async Task ArchiveAsync()
        => await _archiveHandler(_original, !_original.IsArchived);
}

public sealed class WorkItemViewModel : ObservableObject
{
    private WorkItemViewModel(
        TodoRecord todo,
        Func<TodoRecord, Task> editHandler,
        Func<TodoRecord, Task> archiveHandler,
        Func<TodoRecord, Task> deleteHandler)
    {
        Record = todo;
        EditCommand = new AsyncRelayCommand(() => editHandler(todo));
        ArchiveCommand = new AsyncRelayCommand(() => archiveHandler(todo));
        DeleteCommand = new AsyncRelayCommand(() => deleteHandler(todo));
        LabelBadges = (todo.Labels ?? Array.Empty<TodoLabel>())
            .Select(label => new TodoLabelBadgeViewModel(label))
            .ToList();
    }

    public TodoRecord Record { get; }
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand ArchiveCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public string Id => Record.Id;
    public string ListId => Record.ListId;
    public string Title => Record.Title;
    public string Summary => Record.Summary;
    public string Status => Record.Status switch
    {
        "pending_confirmation" => "等待确认",
        "pending" => "待处理",
        "in_progress" => "进行中",
        "completed" => "已完成",
        _ => Record.Status,
    };
    public string Priority => Record.Priority.Trim().ToLowerInvariant() switch
    {
        "high" or "urgent" or "critical" or "高" => "High",
        "low" or "低" => "Low",
        _ => "Medium",
    };
    public string Source => Record.Source;
    public string Assignee => Record.Assignee ?? string.Empty;
    public string DueAtText => Record.DueAt?.ToLocalTime().ToString("MM/dd") ?? string.Empty;
    public string CompanyName => Record.CompanyName ?? string.Empty;
    public IReadOnlyList<TodoLabel> Labels => Record.Labels ?? Array.Empty<TodoLabel>();
    public string LabelText => string.Join("  ", Labels.Select(label => label.Name));
    public IReadOnlyList<TodoLabelBadgeViewModel> LabelBadges { get; }
    public string ChecklistSummary
        => Record.Checklist is { Count: > 0 } checklist
            ? $"{checklist.Count(item => item.IsCompleted)}/{checklist.Count}"
            : string.Empty;
    public bool HasLabels => Labels.Count > 0;
    public bool HasChecklist => !string.IsNullOrWhiteSpace(ChecklistSummary);
    public string EditHint => "点击卡片编辑";

    public string ArchiveActionText => Record.IsArchived ? "恢复" : "归档";

    public static WorkItemViewModel From(
        TodoRecord todo,
        Func<TodoRecord, Task> editHandler,
        Func<TodoRecord, Task> archiveHandler,
        Func<TodoRecord, Task> deleteHandler)
        => new(todo, editHandler, archiveHandler, deleteHandler);
}

public sealed class TodoLabelBadgeViewModel
{
    public TodoLabelBadgeViewModel(TodoLabel label)
    {
        Name = label.Name;
        BackgroundBrush = TodoLabelColors.CreateBrush(label.Color);
    }

    public string Name { get; }
    public SolidColorBrush BackgroundBrush { get; }
    public SolidColorBrush ForegroundBrush { get; } = TodoLabelColors.CreateBrush("#FFFFFF");
}

public sealed record TimeZoneOption(string Id, string DisplayName, string OffsetText);

public sealed class NotificationItemViewModel : ObservableObject
{
    private readonly TimeZoneContext _timeZoneContext;
    private bool _isRead;
    public NotificationRecord Record { get; }
    public IReadOnlyList<NotificationActionItemViewModel> Actions { get; }
    public IAsyncRelayCommand MarkReadCommand { get; }

    public NotificationItemViewModel(
        NotificationRecord record,
        TimeZoneContext timeZoneContext,
        Func<NotificationRecord, string, Task> actionHandler,
        Func<NotificationRecord, Task> markReadHandler)
    {
        Record = record;
        _isRead = record.IsRead;
        _timeZoneContext = timeZoneContext;
        Actions = record.Actions
            .Select(action => new NotificationActionItemViewModel(action, () => actionHandler(record, action.Id)))
            .ToList();
        MarkReadCommand = new AsyncRelayCommand(() => markReadHandler(record), () => !IsRead);
    }

    public string Id => Record.Id;
    public string Title => Record.Title;
    public string Body => Record.Body;
    public string Source => Record.Source;
    public string Category => Record.Category;
    public bool IsRead => _isRead;
    public bool IsResolved => Record.IsResolved;
    public bool HasActions => Actions.Count > 0 && !Record.IsResolved;
    public string CreatedAtText => _timeZoneContext.Format(Record.CreatedAt, "yyyy-MM-dd HH:mm");

    public void RefreshTime()
        => OnPropertyChanged(nameof(CreatedAtText));

    public void MarkAsRead()
    {
        if (_isRead) return;
        _isRead = true;
        OnPropertyChanged(nameof(IsRead));
        MarkReadCommand.NotifyCanExecuteChanged();
    }
}

public sealed class NotificationActionItemViewModel : ObservableObject
{
    private readonly Func<Task> _execute;

    public NotificationActionItemViewModel(NotificationAction action, Func<Task> execute)
    {
        Action = action;
        _execute = execute;
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync);
    }

    public NotificationAction Action { get; }
    public string Id => Action.Id;
    public string Label => Action.Label;
    public IAsyncRelayCommand ExecuteCommand { get; }

    private Task ExecuteAsync() => _execute();
}

public sealed class AnalysisStageItemViewModel : ObservableObject
{
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _completedAt;

    public AnalysisStageItemViewModel(AnalysisStageSnapshot stage)
    {
        Id = stage.Id;
        Title = stage.Title;
        Status = stage.Status;
        Detail = stage.Detail;
        Error = stage.Error ?? string.Empty;
        _startedAt = stage.StartedAt;
        _completedAt = stage.CompletedAt;
    }

    public string Id { get; }
    public string Title { get; }
    public string Status { get; }
    public string Detail { get; }
    public string Error { get; }
    public bool IsRunning => Status == "running";
    public bool IsCompleted => Status == "completed";
    public bool IsError => Status == "error";
    public bool IsPending => Status == "pending";
    public string Indicator => IsCompleted ? "✓" : IsError ? "!" : IsRunning ? "•" : "○";
    public string DurationText => FormatDuration((_completedAt ?? DateTimeOffset.UtcNow) - (_startedAt ?? DateTimeOffset.UtcNow));

    public void RefreshDuration(DateTimeOffset now)
        => OnPropertyChanged(nameof(DurationText));

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }
}

file static class TimeZoneDisplayHelpers
{
    public static string GetDisplayName(TimeZoneInfo zone)
        => zone.Id is "China Standard Time" or "Asia/Shanghai"
            ? "北京时间（中国标准时间）"
            : zone.DisplayName;

    public static string GetOffsetText(TimeZoneInfo zone)
    {
        var offset = zone.GetUtcOffset(DateTimeOffset.UtcNow);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"UTC{sign}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}";
    }
}
