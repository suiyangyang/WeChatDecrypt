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
    private readonly ProtectedCredentialStore _qwenpawCredentials;
    private readonly WeChatDataAnalysisClient _wechat;
    private readonly WeChatAnalysisCoordinator _analysis;
    private readonly IncrementalAnalysisScheduler _scheduler;
    private readonly ArchivedTodoCleanupScheduler _todoCleanup;
    private readonly NotificationHttpServer _notificationApi;
    private readonly WeChatContextHttpServer? _wechatContextApi;
    private readonly AppSettingsStore _settingsStore;
    private readonly WindowsStartupService _startupService;
    private readonly ContactManagementSqliteStore _contactManagementStore;
    private readonly WeChatAnalysisContextResolver _wechatAnalysisContextResolver = new();
    private readonly TimeZoneContext _timeZoneContext;
    private readonly DispatcherTimer _clockTimer;
    private DispatcherTimer? _topToastTimer;
    private AppSettings _appSettings;
    private WeChatAnalysisSettings _wechatAnalysisSettings;
    private WorkSettings _workSettings = new();
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
        new(WorkTodoSort.CompletedAt, "按完成时间"),
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
    [ObservableProperty] private int _selectedSettingsSectionIndex;
    [ObservableProperty] private string _serverUrl = "http://127.0.0.1:8088";
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _isQwenPawPasswordVisible;
    [ObservableProperty] private string _statusMessage = "正在初始化本地 Agent…";
    [ObservableProperty] private string _topToastText = string.Empty;
    [ObservableProperty] private bool _isTopToastShown;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isQwenPawConnected;
    [ObservableProperty] private bool _isNotificationApiRunning;
    [ObservableProperty] private QwenPawAgent? _selectedAgent;
    [ObservableProperty] private bool _isWechatWhitelistEnabled;
    [ObservableProperty] private decimal? _wechatAnalysisIntervalMinutes;
    [ObservableProperty] private WechatAutomaticAnalysisModeOption? _selectedWechatAutomaticAnalysisMode;
    [ObservableProperty] private string _wechatDataDirectory = string.Empty;
    [ObservableProperty] private string _wechatDataAccessStatusText = "正在检测";
    [ObservableProperty] private string _wechatDataAccessDetailText = "正在检查微信数据库密钥和解析服务。";
    [ObservableProperty] private bool _isWechatDataAccessReady;
    [ObservableProperty] private bool _isWechatDataAccessChecking;
    [ObservableProperty] private bool _isWechatDataDirectoryRestartRequired;
    [ObservableProperty] private string _notificationApiEndpoint = "http://<Fairy-IP>:18765/api/v1/notifications";
    [ObservableProperty] private string _notificationApiToken = "local-dev-notification-token";
    [ObservableProperty] private bool _isNotificationApiTokenVisible;
    [ObservableProperty] private bool _notifyAfterEveryAnalysis;
    [ObservableProperty] private bool _notifyWhenWorkContentFound;
    [ObservableProperty] private int _unreadCount;
    [ObservableProperty] private int _selectedNotificationTabIndex;
    [ObservableProperty] private bool _hasUnreadNotifications;
    [ObservableProperty] private bool _hasMoreNotifications;
    [ObservableProperty] private bool _isNotificationSearchOpen;
    [ObservableProperty] private string _notificationSearchKeyword = string.Empty;
    [ObservableProperty] private bool _isNotificationMenuOpen;
    [ObservableProperty] private int _workUnreadCount;
    [ObservableProperty] private int _remoteUnreadCount;
    [ObservableProperty] private bool _hasWorkNotifications;
    [ObservableProperty] private bool _hasMoreWorkNotifications;
    [ObservableProperty] private bool _hasWorkItems;
    [ObservableProperty] private bool _isWorkPageEmpty = true;
    [ObservableProperty] private int _selectedWorkTabIndex;
    [ObservableProperty] private WorkTodoFilterOption? _selectedWorkTodoFilter;
    [ObservableProperty] private WorkTodoSortOption? _selectedWorkTodoSort;
    [ObservableProperty] private bool _isWorkTodoSortDescending;
    [ObservableProperty] private TodoCompanyFilterOption? _selectedTodoCompanyFilter;
    [ObservableProperty] private DateTimeOffset? _completedFromDate;
    [ObservableProperty] private DateTimeOffset? _completedToDate;
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
    [ObservableProperty] private bool _isArchivedCleanupEnabled = true;
    [ObservableProperty] private decimal? _archivedCleanupRetentionDays = WorkSettings.DefaultArchivedRetentionDays;
    [ObservableProperty] private string _analysisContextText = "尚未运行微信分析";
    [ObservableProperty] private string _analysisRoundSummaryText = "本轮尚未建立";
    [ObservableProperty] private string _analysisCurrentStageText = "等待开始";
    [ObservableProperty] private string _analysisOverallStatusText = "未开始";
    [ObservableProperty] private string _analysisLogText = string.Empty;
    [ObservableProperty] private bool _isAnalysisLogExpanded;
    private const int NotificationPageSize = 100;
    private DateTimeOffset? _notificationCursorCreatedAt;
    private string? _notificationCursorId;
    private int _notificationLoadVersion;
    private int _notificationLoadsInFlight;
    private DateTimeOffset? _workNotificationCursorCreatedAt;
    private string? _workNotificationCursorId;
    private int _workNotificationLoadVersion;
    private bool _isLoadingWorkNotifications;
    private bool _suppressStartupSettingChanged;
    private DateTimeOffset? _analysisStartedAt;
    private DateTimeOffset? _analysisEndedAt;
    private DateTimeOffset? _lastAnalysisEndedAt;
    private readonly string _lastAnalysisEndPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lumine.Fairy",
        "last-analysis-end.txt");
    private Action<NotificationRecord>? _desktopNotificationPresenter;
    private IReadOnlyList<TodoRecord> _allWorkItems = [];
    private IReadOnlyList<TodoListRecord> _allTodoLists = [];
    private TodoBoardRecord _todoBoard = new("default", "工作看板", "#1769D1");
    private bool _isTodoEditorOpen;
    private bool _isWechatAnalysisRunning;
    private bool _isSettingAnalysisPoint;
    private int _previousMainPageIndex;

    public MainWindowViewModel()
    {
        _settingsStore = new AppSettingsStore();
        _startupService = new WindowsStartupService();
        LifecycleLog.Info("主窗口构造：开始读取本地设置。");
        _appSettings = _settingsStore.Load(ex =>
        {
            LifecycleLog.Warn("本地设置无法加载，使用默认配置（文件缺失、内容无效或读取异常）。");
            if (ex is not null) LifecycleLog.InitializationFailure("读取本地设置", ex);
        });
        LifecycleLog.Info($"主窗口构造：本地设置读取返回；数据根目录={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumine.Fairy")}");
        _wechatAnalysisSettings = _appSettings.WeChatAnalysis ?? new WeChatAnalysisSettings();
        _notifyAfterEveryAnalysis = _wechatAnalysisSettings.NotifyAfterEveryAnalysis;
        _notifyWhenWorkContentFound = _wechatAnalysisSettings.NotifyWhenWorkContentFound;
        IsWechatWhitelistEnabled = _wechatAnalysisSettings.EnableWhitelist;
        WechatAnalysisIntervalMinutes = WeChatAnalysisSettings.NormalizeAnalysisIntervalMinutes(_wechatAnalysisSettings.AnalysisIntervalMinutes);
        _workSettings = _appSettings.Work;
        ArchivedCleanupRetentionDays = WorkSettings.NormalizeArchivedRetentionDays(_workSettings.ArchivedRetentionDays);
        IsArchivedCleanupEnabled = _workSettings.EnableArchivedAutoCleanup;
        _lastAnalysisEndedAt = LoadLastAnalysisEnd(_lastAnalysisEndPath);
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
        var lastTodoDate = DateTime.Today;
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(CurrentTimeText));
            OnPropertyChanged(nameof(AnalysisElapsedText));
            foreach (var stage in AnalysisStages) stage.RefreshDuration(DateTimeOffset.UtcNow);
            if (DateTime.Today != lastTodoDate)
            {
                lastTodoDate = DateTime.Today;
                foreach (var list in TodoLists) list.RefreshDueDateStates();
            }
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
        _qwenpawCredentials = new ProtectedCredentialStore();
        _wechat = new WeChatDataAnalysisClient(dataRoot: _appSettings.WeChatDataDirectory);
        LifecycleLog.Info($"主窗口构造：存储服务已创建；微信数据目录={_wechat.DataRoot}");
        WechatDataDirectory = _wechat.DataRoot;
        var contactWorkspaceService = new ContactManagementWorkspaceService(_contactManagementStore, contactValidationService);
        IDataImportService contactImportService = new ContactDomainDataImportService(_contactManagementStore, contactStorageOptions, contactValidationService);
        IDataExportService contactExportService = new ContactDomainDataExportService(_contactManagementStore, contactStorageOptions);
        var contactGroupChatDirectoryService = new WeChatGroupChatDirectoryService(
            _contactManagementStore,
            new WeChatGroupChatProvider(_wechat));
        var contactSnapshot = contactWorkspaceService.LoadAsync().GetAwaiter().GetResult();
        LifecycleLog.Info("主窗口构造：联系人快照已加载。");
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
        _todoCleanup = new ArchivedTodoCleanupScheduler(_todoService, _notificationService);
        _todoCleanup.Settings = _workSettings;
        _todoCleanup.CleanupCompleted += OnArchivedTodoCleanupCompleted;
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

    public string LastAnalysisEndText
    {
        get
        {
            var local = _lastAnalysisEndedAt?.ToLocalTime();
            return local is null
                ? "上次分析结束：尚未完成"
                : $"上次分析结束：{local.Value:yyyy-MM-dd HH:mm:ss}";
        }
    }

    public string CurrentIntervalText => $"解析间隔：{_wechatAnalysisSettings.AnalysisIntervalMinutes} 分钟";

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
        if (IsQwenPawConnected)
            StatusMessage = $"已连接 QwenPaw，分析 Agent：{value?.Name ?? "未找到"}";
        OnPropertyChanged(nameof(AgentLoginStatusToolTip));
    }

    partial void OnSelectedNotificationTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAllNotificationTabSelected));
        OnPropertyChanged(nameof(IsUnreadNotificationTabSelected));
        OnPropertyChanged(nameof(IsReadNotificationTabSelected));
        _ = ReloadNotificationsAsync();
    }

    partial void OnSelectedSettingsSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsGeneralSettingsSectionSelected));
        OnPropertyChanged(nameof(IsWorkSettingsSectionSelected));
        OnPropertyChanged(nameof(IsWechatSettingsSectionSelected));
        OnPropertyChanged(nameof(IsNotificationSettingsSectionSelected));
        OnPropertyChanged(nameof(SettingsSectionTitle));
        OnPropertyChanged(nameof(SettingsSectionSubtitle));
        if (value == 2)
            _ = RefreshWechatDataAccessAuthorizationCoreAsync(false);
    }

    partial void OnNotifyAfterEveryAnalysisChanged(bool value)
        => SaveWechatNotificationSettings();

    partial void OnNotifyWhenWorkContentFoundChanged(bool value)
        => SaveWechatNotificationSettings();

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
        OnPropertyChanged(nameof(CanToggleWorkTodoSortDirection));
    }

    partial void OnSelectedTodoCompanyFilterChanged(TodoCompanyFilterOption? value)
    {
        if (value is not null)
            TodoCompanyFilterText = value.Name;

        RefreshFilteredTodoCompanyFilters();
        RefreshWorkItems();
    }

    partial void OnCompletedFromDateChanged(DateTimeOffset? value)
        => RefreshWorkItems();

    partial void OnCompletedToDateChanged(DateTimeOffset? value)
        => RefreshWorkItems();

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

    [RelayCommand]
    private void ClearCompletedDateFilter()
    {
        CompletedFromDate = null;
        CompletedToDate = null;
    }

    [RelayCommand]
    private void SelectWorkTodoSort(string sortName)
    {
        if (!Enum.TryParse<WorkTodoSort>(sortName, ignoreCase: true, out var sort))
            return;

        var option = WorkTodoSorts.FirstOrDefault(item => item.Sort == sort);
        if (option is null)
            return;

        SelectedWorkTodoSort = option;
        OnPropertyChanged(nameof(SelectedWorkTodoSort));
    }

    [RelayCommand]
    private void ToggleWorkTodoSortDirection()
    {
        IsWorkTodoSortDescending = !IsWorkTodoSortDescending;
        OnPropertyChanged(nameof(SortDirectionTooltip));
        RefreshWorkItems();
    }

    public bool IsAllNotificationTabSelected => SelectedNotificationTabIndex == 0;
    public bool IsUnreadNotificationTabSelected => SelectedNotificationTabIndex == 1;
    public bool IsReadNotificationTabSelected => SelectedNotificationTabIndex == 2;
    public bool IsNotificationCenterNavSelected => SelectedPageIndex == 0;
    public bool IsMainNavigationVisible => !IsSettingsPageSelected;
    public bool IsSettingsNavigationVisible => IsSettingsPageSelected;
    public bool IsGeneralSettingsSectionSelected => SelectedSettingsSectionIndex == 0;
    public bool IsWorkSettingsSectionSelected => SelectedSettingsSectionIndex == 1;
    public bool IsWechatSettingsSectionSelected => SelectedSettingsSectionIndex == 2;
    public bool IsNotificationSettingsSectionSelected => SelectedSettingsSectionIndex == 3;
    public string SettingsSectionTitle => SelectedSettingsSectionIndex switch
    {
        1 => "工作设置",
        2 => "微信分析",
        3 => "通知",
        _ => "常规",
    };
    public string SettingsSectionSubtitle => SelectedSettingsSectionIndex switch
    {
        1 => "管理归档任务的自动清理规则",
        2 => "管理微信数据访问授权、QwenPaw 登录、分析 Agent 和自动分析规则",
        3 => "管理微信分析通知与第三方通知接口",
        _ => "管理启动行为和时间显示",
    };

    public int ArchivedTaskCount => _allWorkItems.Count(item => item.IsArchived);

    public int ArchivedCleanupPendingCount => ArchivedTodoCleanupPolicy
        .SelectCleanupCandidates(_allWorkItems, ArchivedTodoCleanupPolicy.GetCutoff(DateTimeOffset.UtcNow, _workSettings.ArchivedRetentionDays))
        .Count;

    public string ArchivedCleanupSummaryText => ArchivedCleanupPendingCount > 0
        ? $"当前已归档 {ArchivedTaskCount} 个任务，其中 {ArchivedCleanupPendingCount} 个已超过 {_workSettings.ArchivedRetentionDays} 天保留期，可立即清理。"
        : $"当前已归档 {ArchivedTaskCount} 个任务，暂无超过 {_workSettings.ArchivedRetentionDays} 天保留期的任务。";

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
        OnPropertyChanged(nameof(IsMainNavigationVisible));
        OnPropertyChanged(nameof(IsSettingsNavigationVisible));
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
    public bool IsPageHeaderVisible => !IsContactManagementPageSelected && !IsWorkPageSelected && !IsSettingsPageSelected;

    public string WorkSummaryText
        => $"待办 {WorkPendingCount}  ·  进行中 {WorkInProgressCount}  ·  已完成 {WorkCompletedCount}  ·  逾期 {WorkOverdueCount}  ·  待确认 {WorkPendingConfirmationCount}";

    public bool CanReorderTodos
        => SelectedWorkTodoSort?.Sort == WorkTodoSort.Position
           && SelectedWorkTodoFilter?.Filter == WorkTodoFilter.All
           && string.IsNullOrWhiteSpace(TodoSearchKeyword)
           && !CompletedFromDate.HasValue
           && !CompletedToDate.HasValue;

    public bool CanToggleWorkTodoSortDirection
        => SelectedWorkTodoSort?.Sort is not WorkTodoSort.Position;

    public string SortDirectionTooltip
        => IsWorkTodoSortDescending ? "当前降序，点击切换为升序" : "当前升序，点击切换为降序";

    private async Task InitializeAsync()
    {
        LifecycleLog.Info($"开始主窗口异步初始化；通知接口绑定={_notificationApi.BindAddress} 端口={_notificationApi.Port}");
        try
        {
            await RunInitializationStageAsync("启动通知 HTTP 接口", () => _notificationApi.StartAsync());
            IsNotificationApiRunning = true;
            if (_wechatContextApi is not null)
            {
                try
                {
                    await RunInitializationStageAsync("启动微信上下文 MCP", () => _wechatContextApi.StartAsync());
                }
                catch (Exception ex)
                {
                    StatusMessage = $"微信上下文 MCP 未启动：{ex.Message}";
                }
            }
            await RunInitializationStageAsync("加载通知", ReloadNotificationsAsync);
            LifecycleLog.Info($"通知加载返回；当前页数量={Notifications.Count} 未读数量={UnreadCount}");
            await RunInitializationStageAsync("加载工作看板", ReloadWorkPageAsync);
            LifecycleLog.Info($"工作看板加载返回；任务数量={_allWorkItems.Count} 列数量={TodoLists.Count}");
            await RunInitializationStageAsync("加载微信白名单", LoadWechatWhitelistAsync);
            await RunInitializationStageAsync("检查微信数据访问授权", () => RefreshWechatDataAccessAuthorizationCoreAsync(false));
            LifecycleLog.Info($"微信数据访问检查返回；状态={WechatDataAccessStatusText}");
            _todoCleanup.Start();
            LifecycleLog.Info("归档任务清理调度器已启动。");

            await RunInitializationStageAsync("恢复 QwenPaw 登录", TryAutoLoginAsync);
            LifecycleLog.Info($"QwenPaw 登录恢复返回；已连接={IsQwenPawConnected}");
            LifecycleLog.Info("主窗口异步初始化完成。");
        }
        catch (Exception ex)
        {
            LifecycleLog.Info("主窗口异步初始化中断，后续阶段未执行。");
            StatusMessage = $"初始化失败：{ex.Message}";
        }
    }

    private static async Task RunInitializationStageAsync(string stage, Func<Task> action)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        LifecycleLog.Info($"初始化阶段开始：{stage}");
        try
        {
            await action();
            LifecycleLog.Info($"初始化阶段完成：{stage}；耗时={timer.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            LifecycleLog.InitializationFailure(stage, ex);
            throw;
        }
    }

    private static string ReadEnvironment(string name)
        => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
           ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
           ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
           ?? string.Empty;

    private async Task TryAutoLoginAsync()
    {
        var saved = await LoadSavedCredentialAsync();
        ServerUrl = FirstNonBlank(saved?.ServerUrl, ReadEnvironment("QwenPawServerUrl"), ServerUrl);
        Username = FirstNonBlank(saved?.Username, ReadEnvironment("QwenPawUser"));
        Password = FirstNonBlank(saved?.Password, ReadEnvironment("QwenPawPwd"));
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            StatusMessage = "本地 Agent 已运行，请登录 QwenPaw。";
            return;
        }
        await LoginCoreAsync(true);
    }

    private async Task<QwenPawCredential?> LoadSavedCredentialAsync()
    {
        try
        {
            return await _qwenpawCredentials.LoadAsync();
        }
        catch (Exception ex)
        {
            LifecycleLog.InitializationFailure("读取已保存的 QwenPaw 登录信息", ex);
            StatusMessage = $"读取已保存的 QwenPaw 登录信息失败：{ex.Message}";
            return null;
        }
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

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
    private async Task RefreshWechatDataAccessAuthorizationAsync()
    {
        await RefreshWechatDataAccessAuthorizationCoreAsync(true);
        StatusMessage = WechatDataAccessDetailText;
    }

    private async Task RefreshWechatDataAccessAuthorizationCoreAsync(bool ensureBackend)
    {
        if (IsWechatDataAccessChecking || IsWechatDataDirectoryRestartRequired) return;

        IsWechatDataAccessChecking = true;
        WechatDataAccessStatusText = "正在检测";
        WechatDataAccessDetailText = "正在检查微信数据库密钥和解析服务。";
        string? connectionError = null;
        try
        {
            if (ensureBackend && File.Exists(_wechat.AccountKeysPath))
            {
                try
                {
                    await _wechat.GetAccountsAsync();
                }
                catch (Exception ex)
                {
                    connectionError = ex.Message;
                }
            }

            var status = await _wechat.GetDataAccessAuthorizationStatusAsync();
            ApplyWechatDataAccessAuthorizationStatus(status, connectionError);
        }
        catch (Exception ex)
        {
            IsWechatDataAccessReady = false;
            WechatDataAccessStatusText = "检测失败";
            WechatDataAccessDetailText = $"无法检测微信数据访问授权：{ex.Message}";
        }
        finally
        {
            IsWechatDataAccessChecking = false;
        }
    }

    private void ApplyWechatDataAccessAuthorizationStatus(
        WeChatDataAccessAuthorizationStatus status,
        string? connectionError)
    {
        WechatDataDirectory = status.DataRoot;
        IsWechatDataAccessReady = status.IsReady;
        IsWechatDataDirectoryRestartRequired = false;

        if (status.BackendUsesCurrentDataRoot == false)
        {
            WechatDataAccessStatusText = "目录不一致";
            WechatDataAccessDetailText =
                $"10492 端口上的微信解析服务正在使用其他数据目录。请选择与现有服务一致的数据根目录，或退出旧的微信数据工具后重新检测。当前目录：{status.DataRoot}";
            return;
        }

        if (!status.AccountKeysPresent)
        {
            WechatDataAccessStatusText = "未授权";
            WechatDataAccessDetailText =
                $"尚未找到数据库密钥文件。请保持微信登录，打开微信数据工具完成检测、获取数据库密钥和数据库解密。期望位置：{status.AccountKeysPath}";
            return;
        }

        if (status.IsReady)
        {
            WechatDataAccessStatusText = "已授权";
            WechatDataAccessDetailText = status.DatabaseFilesPresent
                ? $"已连接微信解析服务，检测到 {status.AccountCount} 个可用账号，数据库密钥和解密数据均已就绪。"
                : $"已连接微信解析服务，检测到 {status.AccountCount} 个可用账号；当前使用实时微信数据。";
            return;
        }

        if (!string.IsNullOrWhiteSpace(connectionError))
        {
            WechatDataAccessStatusText = "连接失败";
            WechatDataAccessDetailText = connectionError;
            return;
        }

        if (status.BackendHealthy)
        {
            WechatDataAccessStatusText = "未检测到账号";
            WechatDataAccessDetailText = "数据库密钥已存在，但解析服务没有检测到可用微信账号。请确认微信已登录后重新检测。";
            return;
        }

        if (!string.IsNullOrWhiteSpace(status.BackendVersionStatus))
        {
            WechatDataAccessStatusText = "连接失败";
            WechatDataAccessDetailText = $"数据库密钥已存在，但无法连接微信解析服务。版本检查：{status.BackendVersionStatus}"
                                         + "点击“重新检测”重试连接，并查看后端日志。";
            return;
        }

        WechatDataAccessStatusText = "待连接";
        WechatDataAccessDetailText = "数据库密钥已存在。点击“重新检测”启动并连接微信解析服务。";
    }

    [RelayCommand]
    private void OpenWechatDataAccessAuthorizationTool()
    {
        try
        {
            _wechat.OpenDataAccessAuthorizationTool();
            StatusMessage = "微信数据工具已打开。请完成微信检测、获取数据库密钥和数据库解密，然后返回此页重新检测。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开微信数据工具失败：{ex.Message}";
        }
    }

    public void SetWechatDataDirectory(string selectedPath)
    {
        try
        {
            var normalized = Path.GetFullPath(selectedPath.Trim());
            if (new DirectoryInfo(normalized).Name.Equals("output", StringComparison.OrdinalIgnoreCase))
                normalized = Directory.GetParent(normalized)?.FullName ?? normalized;

            WechatDataDirectory = normalized;
            _appSettings.WeChatDataDirectory = normalized;
            _settingsStore.Save(_appSettings);
            IsWechatDataAccessReady = false;
            IsWechatDataDirectoryRestartRequired = !normalized.Equals(_wechat.DataRoot, StringComparison.OrdinalIgnoreCase);
            if (IsWechatDataDirectoryRestartRequired)
            {
                WechatDataAccessStatusText = "等待重启";
                WechatDataAccessDetailText = "微信数据目录已保存。请完全退出 Fairy（包括系统托盘实例）并重新启动，之后再执行检测。";
                StatusMessage = WechatDataAccessDetailText;
            }
            else
            {
                _ = RefreshWechatDataAccessAuthorizationCoreAsync(false);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存微信数据目录失败：{ex.Message}";
        }
    }

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
            NotifyAfterEveryAnalysis = NotifyAfterEveryAnalysis,
            NotifyWhenWorkContentFound = NotifyWhenWorkContentFound,
        };
        WechatAnalysisIntervalMinutes = _wechatAnalysisSettings.AnalysisIntervalMinutes;
        ApplyWechatAnalysisConfiguration(true);
        OnPropertyChanged(nameof(CurrentIntervalText));
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

    private void SaveWechatNotificationSettings()
    {
        if (_analysis is null)
            return;

        _wechatAnalysisSettings.NotifyAfterEveryAnalysis = NotifyAfterEveryAnalysis;
        _wechatAnalysisSettings.NotifyWhenWorkContentFound = NotifyWhenWorkContentFound;
        ApplyWechatAnalysisConfiguration(true);
        StatusMessage = "微信分析通知设置已保存。";
    }

    [RelayCommand]
    private void SaveWorkSettings()
    {
        var retentionDays = ArchivedCleanupRetentionDays ?? WorkSettings.DefaultArchivedRetentionDays;
        if (retentionDays != decimal.Truncate(retentionDays))
        {
            StatusMessage = "归档保留天数必须是整数天。";
            return;
        }

        _workSettings = new WorkSettings
        {
            EnableArchivedAutoCleanup = IsArchivedCleanupEnabled,
            ArchivedRetentionDays = WorkSettings.NormalizeArchivedRetentionDays((int)retentionDays),
        };
        ArchivedCleanupRetentionDays = _workSettings.ArchivedRetentionDays;
        _appSettings.Work = _workSettings;
        _settingsStore.Save(_appSettings);
        _todoCleanup.Settings = _workSettings;
        OnPropertyChanged(nameof(ArchivedCleanupSummaryText));
        StatusMessage = _workSettings.EnableArchivedAutoCleanup
            ? $"工作设置已保存，归档超过 {_workSettings.ArchivedRetentionDays} 天的任务会自动清理。"
            : "工作设置已保存，已关闭归档任务自动清理。";
    }

    [RelayCommand]
    private async Task CleanupArchivedTodosAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await _todoCleanup.RunNowAsync();
            await ReloadWorkPageAsync();
            StatusMessage = result.DeletedCount > 0
                ? $"已清理 {result.DeletedCount} 个归档超过 {result.RetentionDays} 天的任务。"
                : "没有需要清理的归档任务。";
            ShowTopToast(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"清理归档任务失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnArchivedTodoCleanupCompleted(object? sender, ArchivedTodoCleanupResult result)
        => _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await ReloadWorkPageAsync();
            StatusMessage = $"已自动清理 {result.DeletedCount} 个归档超过 {result.RetentionDays} 天的任务。";
        });

    [RelayCommand]
    private Task LoginAsync() => LoginCoreAsync(false);

    private async Task LoginCoreAsync(bool isAutoLogin)
    {
        if (IsBusy) return;
        IsBusy = true;
        var serverUrl = ServerUrl.Trim();
        try
        {
            await _qwenPaw.LoginAsync(serverUrl, Username, Password);
            await _qwenpawCredentials.SaveAsync(new QwenPawCredential(serverUrl, Username, Password));
            IsQwenPawConnected = true;
            await RefreshAgentsAsync();
            _scheduler.Start();
            StatusMessage = isAutoLogin
                ? $"已自动登录 QwenPaw（{serverUrl}），分析 Agent：{SelectedAgent?.Name ?? "未找到"}"
                : $"已连接 QwenPaw，分析 Agent：{SelectedAgent?.Name ?? "未找到"}";
        }
        catch (Exception ex)
        {
            IsQwenPawConnected = false;
            LifecycleLog.InitializationFailure("QwenPaw 登录", ex);
            StatusMessage = isAutoLogin
                ? $"QwenPaw 自动登录失败：{ex.Message}，请到设置-微信分析检查服务器地址和密码。"
                : $"QwenPaw 登录失败：{ex.Message}";
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
            var selectedAgentId = SelectedAgent?.Id ?? configuredAgent;
            SelectedAgent = Agents.FirstOrDefault(x => string.Equals(x.Id, selectedAgentId, StringComparison.OrdinalIgnoreCase))
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
        SelectedPageIndex = 2;
        if (_isWechatAnalysisRunning)
        {
            ShowAnalysisNotice("微信分析正在进行中，请等待本轮完成。");
            return;
        }
        if (!_qwenPaw.IsAuthenticated)
        {
            ShowAnalysisNotice("请先在“设置”中登录 QwenPaw。");
            return;
        }
        if (SelectedAgent is null)
        {
            await RefreshAgentsAsync();
            if (SelectedAgent is null)
            {
                ShowAnalysisNotice("没有可用的 QwenPaw Agent，请检查 Agent 是否已启用。");
                return;
            }
        }

        _isWechatAnalysisRunning = true;
        ShowAnalysisNotice(_scheduler.IsRoundRunning
            ? "自动分析正在执行，本次请求会排队，结束后立即补跑。"
            : "正在读取最近微信会话并请求 QwenPaw 分析…");
        try
        {
            await RefreshWechatWhitelistCatalogAsync(CancellationToken.None);
            await _scheduler.AnalyzeNowAsync();
            await ReloadNotificationsAsync();
            await ReloadWorkPageAsync();
            ShowAnalysisNotice("微信分析已完成。");
        }
        catch (NoPendingWeChatMessagesException)
        {
            ShowAnalysisNotice("最近会话没有待分析的新消息，可在设置中调整分析范围或“设置最新分析点”。");
        }
        catch (WeChatAnalysisRoundInProgressException ex)
        {
            ShowAnalysisNotice(ex.Message);
        }
        catch (Exception ex)
        {
            ShowAnalysisNotice($"微信分析失败：{ex.Message}");
        }
        finally
        {
            _isWechatAnalysisRunning = false;
        }
    }

    /// <summary>分析相关反馈同时写入状态栏和顶部提示，避免只有一行小字被忽略。</summary>
    private void ShowAnalysisNotice(string message)
    {
        StatusMessage = message;
        ShowTopToast(message);
    }

    [RelayCommand]
    private async Task SetLatestAnalysisPointAsync()
    {
        if (_isSettingAnalysisPoint)
        {
            ShowAnalysisNotice("正在设置最新分析点，请等待本次完成。");
            return;
        }

        _isSettingAnalysisPoint = true;
        ShowAnalysisNotice(_scheduler.IsRoundRunning
            ? "自动分析正在执行，等它结束后再把分析点推进到最新消息。"
            : "正在读取联系人和群聊的最新消息并设置分析点…");
        try
        {
            var count = await _scheduler.SetLatestAnalysisPointAsync();
            ShowAnalysisNotice(count == 0
                ? "没有找到可设置分析点的联系人或群聊。"
                : $"已为 {count} 个联系人或群聊设置最新分析点；选择“未分析过的消息”后将从此处继续。");
        }
        catch (Exception ex)
        {
            ShowAnalysisNotice($"设置最新分析点失败：{ex.Message}");
        }
        finally
        {
            _isSettingAnalysisPoint = false;
        }
    }

    [RelayCommand]
    private void OpenWechatAnalysisSettings()
        => OpenSettings(2);

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

    private async Task AddCardAboveAsync(TodoRecord clicked)
    {
        var listId = ResolveListId(clicked);
        var list = _allTodoLists.FirstOrDefault(item => item.Id == listId);
        if (list is null) return;

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
        await ShowTodoEditorAsync(todo, true, saved => MoveCardAboveAsync(saved, list.Id, clicked.Position));
    }

    private async Task MoveCardAboveAsync(TodoRecord saved, string listId, int targetPosition)
    {
        var list = _allTodoLists.FirstOrDefault(item => item.Id == listId);
        if (list is null) return;

        var status = list.Status ?? saved.Status;
        if (await _todoService.MoveAsync(saved.Id, list.BoardId, list.Id, targetPosition, status) is not null)
            await ReloadWorkPageAsync();
    }

    private async Task AddCardToTopAsync(TodoListRecord record)
    {
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
            record.Status ?? "pending",
            now,
            now,
            null,
            "manual",
            string.Empty,
            BoardId: record.BoardId,
            ListId: record.Id,
            Position: record.Position);
        await ShowTodoEditorAsync(todo, true, saved => MoveCardToTopAsync(saved, record));
    }

    private async Task MoveCardToTopAsync(TodoRecord saved, TodoListRecord record)
    {
        var status = record.Status ?? saved.Status;
        if (await _todoService.MoveAsync(saved.Id, record.BoardId, record.Id, 0, status) is not null)
            await ReloadWorkPageAsync();
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

    private async Task ShowTodoEditorAsync(TodoRecord todo, bool isNew, Func<TodoRecord, Task>? afterCreatedAsync = null)
    {
        if (_isTodoEditorOpen) return;

        _isTodoEditorOpen = true;
        try
        {
            await ShowTodoEditorCoreAsync(todo, isNew, afterCreatedAsync);
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开 TODO 编辑器失败：{ex.Message}";
        }
        finally
        {
            _isTodoEditorOpen = false;
        }
    }

    private async Task ShowTodoEditorCoreAsync(TodoRecord todo, bool isNew, Func<TodoRecord, Task>? afterCreatedAsync = null)
    {

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
        if (updatedTodo is null) return;

        StatusMessage = "TODO 已更新。";
        if (isNew && afterCreatedAsync is not null)
            await afterCreatedAsync(updatedTodo);
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

    private async Task ArchiveAllInListAsync(TodoListRecord list)
    {
        var targets = _allWorkItems
            .Where(todo => ResolveListId(todo) == list.Id && !todo.IsArchived)
            .ToList();
        if (targets.Count == 0)
        {
            StatusMessage = $"列表“{list.Name}”中没有可归档的卡片。";
            return;
        }

        var confirmed = await ShowConfirmationAsync(
            "全部归档",
            $"确定归档“{list.Name}”列表中的 {targets.Count} 张卡片吗？归档后可在“已归档”筛选中恢复。",
            "全部归档");
        if (!confirmed) return;

        var count = 0;
        foreach (var todo in targets)
        {
            var updated = await _todoService.UpdateDetailsAsync(todo with
            {
                IsArchived = true,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            if (updated is not null) count++;
        }

        await ReloadWorkPageAsync();
        StatusMessage = $"已归档“{list.Name}”列表中的 {count} 张卡片。";
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

    private static readonly string ProcessingLabelColor = TodoLabelColors.CommonColors
        .Single(option => option.Name == "绿色").Color;

    private async Task AddProcessingLabelAsync(TodoRecord todo)
    {
        var labels = (todo.Labels ?? Array.Empty<TodoLabel>()).ToList();
        if (labels.Any(label => label.Name.Trim()
                .Equals(WorkItemViewModel.ProcessingLabelName, StringComparison.OrdinalIgnoreCase)))
            return;
        if (labels.Count >= TodoEditWindowViewModel.MaxLabelCount)
        {
            StatusMessage = $"标签最多添加 {TodoEditWindowViewModel.MaxLabelCount} 个。";
            return;
        }

        labels.Add(new TodoLabel(
            $"label:{Guid.NewGuid():N}", WorkItemViewModel.ProcessingLabelName, ProcessingLabelColor));
        var updated = await _todoService.UpdateDetailsAsync(todo with
        {
            Labels = labels,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        if (updated is not null)
        {
            await ReloadWorkPageAsync();
            StatusMessage = $"已添加「{WorkItemViewModel.ProcessingLabelName}」标签。";
        }
    }

    private async Task RemoveProcessingLabelAsync(TodoRecord todo)
    {
        var labels = (todo.Labels ?? Array.Empty<TodoLabel>()).ToList();
        var kept = labels
            .Where(label => !label.Name.Trim().Equals(WorkItemViewModel.ProcessingLabelName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (kept.Count == labels.Count)
            return;

        var updated = await _todoService.UpdateDetailsAsync(todo with
        {
            Labels = kept,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        if (updated is not null)
        {
            await ReloadWorkPageAsync();
            StatusMessage = $"已移除「{WorkItemViewModel.ProcessingLabelName}」标签。";
        }
    }

    public async Task AddTodoReferencesAsync(TodoRecord todo, IReadOnlyList<TodoFileReference> references)
    {
        var merged = new List<TodoFileReference>(todo.FileReferences ?? Array.Empty<TodoFileReference>());
        var added = 0;
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Path)
                || merged.Any(existing => string.Equals(existing.Path, reference.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            merged.Add(reference);
            added++;
        }

        if (added == 0)
        {
            StatusMessage = "所选内容已添加过引用，未重复添加。";
            return;
        }

        var updated = await _todoService.UpdateDetailsAsync(todo with
        {
            FileReferences = merged,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        if (updated is not null)
        {
            await ReloadWorkPageAsync();
            StatusMessage = added == 1 ? "已添加 1 个引用。" : $"已添加 {added} 个引用。";
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
            var result = await _analysis.HandleNotificationActionAsync(notification, actionId, SelectedAgent?.Id ?? "default");
            await ReloadNotificationsAsync();
            await ReloadWorkPageAsync();
            ShowTopToast(result.Message);
            StatusMessage = result.AlreadyHandled ? result.Message : "通知操作已完成。";
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
        pageIndex = Math.Clamp(pageIndex, 0, 6);
        if (pageIndex == 6)
        {
            OpenSettings(0);
            return;
        }

        SelectedPageIndex = pageIndex;
        if (SelectedPageIndex == 0)
            SelectedNotificationTabIndex = 0;
        IsNotificationSearchOpen = false;
        IsNotificationMenuOpen = false;
    }

    [RelayCommand]
    private void SelectSettingsSection(string? section)
    {
        if (!int.TryParse(section, out var sectionIndex)) return;
        SelectedSettingsSectionIndex = Math.Clamp(sectionIndex, 0, 3);
    }

    [RelayCommand]
    private void ReturnToMainPage()
    {
        SelectedPageIndex = Math.Clamp(_previousMainPageIndex, 0, 5);
        IsNotificationSearchOpen = false;
        IsNotificationMenuOpen = false;
    }

    private void OpenSettings(int sectionIndex)
    {
        if (!IsSettingsPageSelected)
            _previousMainPageIndex = Math.Clamp(SelectedPageIndex, 0, 5);
        SelectedSettingsSectionIndex = Math.Clamp(sectionIndex, 0, 3);
        SelectedPageIndex = 6;
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

    public string QwenPawPasswordVisibilityToolTip
        => IsQwenPawPasswordVisible ? "隐藏密码" : "显示密码";

    public string AgentLoginStatusToolTip => IsQwenPawConnected
        ? SelectedAgent is null
            ? "已登录 QwenPaw，尚未选择分析 Agent"
            : $"已登录 QwenPaw，分析 Agent：{SelectedAgent.Name}"
        : "未登录 QwenPaw，请在「设置 > 微信分析」登录";

    partial void OnIsQwenPawConnectedChanged(bool value)
        => OnPropertyChanged(nameof(AgentLoginStatusToolTip));

    partial void OnIsNotificationApiTokenVisibleChanged(bool value)
        => OnPropertyChanged(nameof(NotificationApiTokenVisibilityToolTip));

    partial void OnIsQwenPawPasswordVisibleChanged(bool value)
        => OnPropertyChanged(nameof(QwenPawPasswordVisibilityToolTip));

    [RelayCommand]
    private void ToggleNotificationApiTokenVisibility()
        => IsNotificationApiTokenVisible = !IsNotificationApiTokenVisible;

    [RelayCommand]
    private void ToggleQwenPawPasswordVisibility()
        => IsQwenPawPasswordVisible = !IsQwenPawPasswordVisible;

    [RelayCommand]
    private async Task LoadMoreNotificationsAsync()
    {
        if (_notificationLoadsInFlight > 0 || !HasMoreNotifications) return;
        await LoadNotificationPageAsync(false);
    }

    [RelayCommand]
    private async Task LoadMoreWorkNotificationsAsync()
    {
        if (_isLoadingWorkNotifications || !HasMoreWorkNotifications) return;

        var version = _workNotificationLoadVersion;
        _isLoadingWorkNotifications = true;
        try
        {
            var items = await _notificationService.ListPageAsync(
                null,
                false,
                false,
                _workNotificationCursorCreatedAt,
                _workNotificationCursorId,
                NotificationPageSize,
                workPageOnly: true);
            if (version != _workNotificationLoadVersion) return;

            await Dispatcher.UIThread.InvokeAsync(() => AppendWorkNotificationPage(items, reset: false));
        }
        finally
        {
            if (version == _workNotificationLoadVersion)
                _isLoadingWorkNotifications = false;
        }
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
        var workNotificationLoadVersion = ++_workNotificationLoadVersion;
        _isLoadingWorkNotifications = true;
        var notificationsTask = _notificationService.ListPageAsync(
            null,
            false,
            false,
            null,
            null,
            NotificationPageSize,
            workPageOnly: true);
        await Task.WhenAll(todosTask, listsTask, notificationsTask);
        var todos = await todosTask;
        var lists = await listsTask;
        var workNotifications = await notificationsTask;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (workNotificationLoadVersion != _workNotificationLoadVersion) return;
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
            OnPropertyChanged(nameof(ArchivedTaskCount));
            OnPropertyChanged(nameof(ArchivedCleanupPendingCount));
            OnPropertyChanged(nameof(ArchivedCleanupSummaryText));

            AppendWorkNotificationPage(workNotifications, reset: true);
            HasWorkItems = WorkItems.Count > 0;
            IsWorkPageEmpty = !HasWorkNotifications && !HasWorkItems;
            WorkPageRefreshed?.Invoke(this, EventArgs.Empty);
        });
        if (workNotificationLoadVersion == _workNotificationLoadVersion)
            _isLoadingWorkNotifications = false;
    }

    private void RefreshWorkItems()
    {
        WorkItems.Clear();
        TodoLists.Clear();
        var filter = SelectedWorkTodoFilter?.Filter ?? WorkTodoFilter.All;
        var filtered = FilterWorkItems(_allWorkItems, filter)
            .Where(MatchesTodoCompany)
            .Where(MatchesCompletedDateRange)
            .Where(MatchesTodoSearch)
            .ToList();
        foreach (var todo in SortWorkItems(filtered, SelectedWorkTodoSort?.Sort ?? WorkTodoSort.Priority, IsWorkTodoSortDescending))
            WorkItems.Add(WorkItemViewModel.From(todo, EditTodoAsync, ArchiveTodoAsync, DeleteTodoAsync, MoveTodoToTopAsync, MoveTodoToCompletedAsync, AddProcessingLabelAsync, RemoveProcessingLabelAsync, AddCardAboveAsync));

        foreach (var listRecord in _allTodoLists.Where(list => !list.IsArchived || filter == WorkTodoFilter.Archived))
        {
            var listCards = SortWorkItems(
                    filtered.Where(item => ResolveListId(item) == listRecord.Id),
                    SelectedWorkTodoSort?.Sort ?? WorkTodoSort.Priority,
                    IsWorkTodoSortDescending)
                .ToList();
            TodoLists.Add(new TodoListViewModel(
                listRecord,
                RenameTodoListAsync,
                ArchiveTodoListAsync,
                ArchiveAllInListAsync,
                DeleteTodoListAsync,
                CreateTodoAsync,
                AddCardToTopAsync,
                listCards,
                todo => WorkItemViewModel.From(todo, EditTodoAsync, ArchiveTodoAsync, DeleteTodoAsync, MoveTodoToTopAsync, MoveTodoToCompletedAsync, AddProcessingLabelAsync, RemoveProcessingLabelAsync, AddCardAboveAsync)));
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

    private bool MatchesCompletedDateRange(TodoRecord todo)
    {
        if (!CompletedFromDate.HasValue && !CompletedToDate.HasValue)
            return true;

        if (!todo.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || !todo.CompletedAt.HasValue)
            return false;

        var completedDate = todo.CompletedAt.Value.ToLocalTime().Date;
        var fromDate = CompletedFromDate?.Date;
        var toDate = CompletedToDate?.Date;
        return (!fromDate.HasValue || completedDate >= fromDate.Value)
            && (!toDate.HasValue || completedDate <= toDate.Value);
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

    private async Task MoveTodoToTopAsync(TodoRecord todo)
    {
        var list = _allTodoLists.FirstOrDefault(item => item.Id == ResolveListId(todo));
        if (list is null)
            return;

        var status = list.Status ?? todo.Status;
        if (await _todoService.MoveAsync(todo.Id, list.BoardId, list.Id, 0, status) is not null)
        {
            await ReloadWorkPageAsync();
            StatusMessage = "卡片已移动到列表顶端。";
        }
    }

    private async Task MoveTodoToCompletedAsync(TodoRecord todo)
    {
        if (!todo.Status.Equals("pending", StringComparison.OrdinalIgnoreCase)
            && !todo.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "仅待办或进行中的卡片可以移入已完成。";
            return;
        }

        var doneList = _allTodoLists.FirstOrDefault(item => item.Id == "done");
        if (doneList is null)
            return;

        var position = _allWorkItems.Count(item => ResolveListId(item) == "done");
        var status = doneList.Status ?? "completed";
        if (await _todoService.MoveAsync(todo.Id, doneList.BoardId, doneList.Id, position, status) is not null)
        {
            await ReloadWorkPageAsync();
            StatusMessage = "卡片已移入已完成。";
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

    private static IEnumerable<TodoRecord> SortWorkItems(IEnumerable<TodoRecord> todos, WorkTodoSort sort, bool descending)
        => sort switch
        {
            WorkTodoSort.Position => todos
                .OrderBy(item => item.Position)
                .ThenByDescending(item => item.CreatedAt),
            WorkTodoSort.DueAt => descending
                ? todos
                    .OrderBy(item => item.DueAt is null)
                    .ThenByDescending(item => item.DueAt)
                    .ThenByDescending(item => item.CreatedAt)
                : todos
                    .OrderBy(item => item.DueAt is null)
                    .ThenBy(item => item.DueAt)
                    .ThenByDescending(item => item.CreatedAt),
            WorkTodoSort.CreatedAt => descending
                ? todos
                    .OrderByDescending(item => item.CreatedAt)
                    .ThenBy(item => item.Position)
                : todos
                    .OrderBy(item => item.CreatedAt)
                    .ThenBy(item => item.Position),
            WorkTodoSort.CompletedAt => descending
                ? todos
                    .OrderBy(item => item.CompletedAt is null)
                    .ThenByDescending(item => item.CompletedAt)
                    .ThenByDescending(item => item.CreatedAt)
                : todos
                    .OrderBy(item => item.CompletedAt is null)
                    .ThenBy(item => item.CompletedAt)
                    .ThenByDescending(item => item.CreatedAt),
            _ => descending
                ? todos
                    .OrderByDescending(item => PriorityRank(item.Priority))
                    .ThenBy(item => item.DueAt is null)
                    .ThenBy(item => item.DueAt)
                    .ThenByDescending(item => item.CreatedAt)
                : todos
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
        => !string.Equals(todo.Status, "completed", StringComparison.OrdinalIgnoreCase)
           && !todo.IsArchived
           && todo.DueAt is not null
           && todo.DueAt.Value < DateTimeOffset.UtcNow;

    private NotificationItemViewModel CreateNotificationItem(NotificationRecord notification)
        => new(notification, _timeZoneContext, HandleNotificationActionAsync, MarkReadAsync);

    private void AppendWorkNotificationPage(IReadOnlyList<NotificationRecord> items, bool reset)
    {
        if (reset) WorkNotifications.Clear();
        foreach (var item in items)
            WorkNotifications.Add(CreateNotificationItem(item));

        var last = items.LastOrDefault();
        if (last is not null)
        {
            _workNotificationCursorCreatedAt = last.CreatedAt;
            _workNotificationCursorId = last.Id;
        }
        else if (reset)
        {
            _workNotificationCursorCreatedAt = null;
            _workNotificationCursorId = null;
        }

        HasMoreWorkNotifications = items.Count == NotificationPageSize;
        HasWorkNotifications = WorkNotifications.Count > 0;
        IsWorkPageEmpty = !HasWorkItems && !HasWorkNotifications;
    }

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
        if (!reset && _notificationLoadsInFlight > 0) return;
        var version = reset ? ++_notificationLoadVersion : _notificationLoadVersion;
        _notificationLoadsInFlight++;
        if (reset)
        {
            _notificationCursorCreatedAt = null;
            _notificationCursorId = null;
            HasMoreNotifications = true;
        }

        try
        {
            var unreadOnly = SelectedNotificationTabIndex == 1;
            var readOnly = SelectedNotificationTabIndex == 2;
            var items = await _notificationService.ListPageAsync(
                string.IsNullOrWhiteSpace(NotificationSearchKeyword) ? null : NotificationSearchKeyword,
                unreadOnly,
                readOnly,
                _notificationCursorCreatedAt,
                _notificationCursorId,
                NotificationPageSize);
            var unreadItems = await _notificationService.ListAsync(unreadOnly: true);
            if (version != _notificationLoadVersion) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (reset) Notifications.Clear();
                foreach (var item in items)
                    Notifications.Add(CreateNotificationItem(item));

                var last = items.LastOrDefault();
                if (last is not null)
                {
                    _notificationCursorCreatedAt = last.CreatedAt;
                    _notificationCursorId = last.Id;
                }
                HasMoreNotifications = items.Count == NotificationPageSize;
                UnreadCount = unreadItems.Count(x => !x.IsResolved);
                HasUnreadNotifications = UnreadCount > 0;
                WorkUnreadCount = unreadItems.Count(x => !x.IsResolved && x.Category.Equals("work", StringComparison.OrdinalIgnoreCase));
                RemoteUnreadCount = unreadItems.Count(x => !x.IsResolved && x.Category.Equals("remote", StringComparison.OrdinalIgnoreCase));
            });
        }
        finally
        {
            _notificationLoadsInFlight--;
        }
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
            var isEnded = progress.Stages.Any(x => x.Status == "error")
                || progress.Stages.Any(x => x.Id == "complete" && x.Status == "completed");
            DateTimeOffset? endedAt = isEnded ? DateTimeOffset.UtcNow : null;
            _analysisEndedAt = endedAt;
            if (endedAt.HasValue)
            {
                _lastAnalysisEndedAt = endedAt;
                SaveLastAnalysisEnd(endedAt.Value);
                OnPropertyChanged(nameof(LastAnalysisEndText));
            }
            AnalysisContextText = string.IsNullOrWhiteSpace(progress.RoundTitle)
                ? $"账号：{progress.AccountDisplayName}  ·  会话：{progress.ConversationDisplayName}"
                : $"{progress.RoundTitle}  ·  账号：{progress.AccountDisplayName}";
            AnalysisRoundSummaryText = progress.TotalContacts <= 0
                ? (string.IsNullOrWhiteSpace(progress.Notice) ? "正在收集本轮新增消息…" : progress.Notice)
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
                : progress.IsFinished || progress.Stages.All(x => x.Status == "completed")
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

    private const int TopToastMaxLength = 96;

    private void ShowTopToast(string message)
    {
        TopToastText = message.Length <= TopToastMaxLength ? message : $"{message[..TopToastMaxLength]}…";
        IsTopToastShown = true;
        _topToastTimer ??= CreateTopToastTimer();
        _topToastTimer.Stop();
        _topToastTimer.Start();
    }

    private DispatcherTimer CreateTopToastTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => HideTopToast();
        return timer;
    }

    [RelayCommand]
    private void HideTopToast()
    {
        _topToastTimer?.Stop();
        IsTopToastShown = false;
    }

    public async ValueTask DisposeAsync()
    {
        _clockTimer.Stop();
        _topToastTimer?.Stop();
        _notificationService.NotificationAdded -= OnNotificationAdded;
        _todoService.TodoChanged -= OnTodoChanged;
        _analysis.ProgressChanged -= OnAnalysisProgressChanged;
        await _notificationApi.DisposeAsync();
        if (_wechatContextApi is not null)
            await _wechatContextApi.DisposeAsync();
        await _scheduler.DisposeAsync();
        _todoCleanup.CleanupCompleted -= OnArchivedTodoCleanupCompleted;
        await _todoCleanup.DisposeAsync();
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

    private void SaveLastAnalysisEnd(DateTimeOffset time)
    {
        try
        {
            var directory = Path.GetDirectoryName(_lastAnalysisEndPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_lastAnalysisEndPath, time.ToString("O"));
        }
        catch (IOException) { }
    }

    private static DateTimeOffset? LoadLastAnalysisEnd(string path)
    {
        try
        {
            if (File.Exists(path) && DateTimeOffset.TryParse(File.ReadAllText(path), null, System.Globalization.DateTimeStyles.RoundtripKind, out var time))
                return time;
        }
        catch (IOException) { }
        return null;
    }
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
    CompletedAt,
}

public sealed record WorkTodoSortOption(WorkTodoSort Sort, string Name);

public sealed record TodoCompanyFilterOption(string? Id, string Name);

public sealed class TodoListViewModel : ObservableObject
{
    private readonly TodoListRecord _original;
    private readonly Func<TodoListRecord, string, Task<TodoListRecord?>> _renameHandler;
    private readonly Func<TodoListRecord, bool, Task<bool>> _archiveHandler;
    private readonly Func<TodoListRecord, Task> _archiveAllCardsHandler;
    private readonly Func<TodoListRecord, Task<bool>> _deleteHandler;
    private readonly Func<TodoListRecord, Task> _createCardHandler;
    private readonly Func<TodoListRecord, Task> _createCardAtTopHandler;
    private readonly IReadOnlyList<TodoRecord> _cards;
    private readonly Func<TodoRecord, WorkItemViewModel> _cardFactory;
    private int _loadedCardCount;
    private string _name;
    private bool _isEditingName;

    public const int InitialCardCount = 50;
    public const int CardBatchSize = 50;

    public TodoListViewModel(
        TodoListRecord list,
        Func<TodoListRecord, string, Task<TodoListRecord?>> renameHandler,
        Func<TodoListRecord, bool, Task<bool>> archiveHandler,
        Func<TodoListRecord, Task> archiveAllCardsHandler,
        Func<TodoListRecord, Task<bool>> deleteHandler,
        Func<TodoListRecord, Task> createCardHandler,
        Func<TodoListRecord, Task> createCardAtTopHandler,
        IReadOnlyList<TodoRecord> cards,
        Func<TodoRecord, WorkItemViewModel> cardFactory)
    {
        _original = list;
        _name = list.Name;
        _renameHandler = renameHandler;
        _archiveHandler = archiveHandler;
        _archiveAllCardsHandler = archiveAllCardsHandler;
        _deleteHandler = deleteHandler;
        _createCardHandler = createCardHandler;
        _createCardAtTopHandler = createCardAtTopHandler;
        _cards = cards;
        _cardFactory = cardFactory;
        for (var i = 0; i < Math.Min(InitialCardCount, cards.Count); i++)
        {
            Cards.Add(cardFactory(cards[i]));
            _loadedCardCount = i + 1;
        }
        ArchiveCommand = new AsyncRelayCommand(ArchiveAsync, () => CanArchive || CanRestore);
        ArchiveAllCardsCommand = new AsyncRelayCommand(() => _archiveAllCardsHandler(_original), () => CanArchiveAllCards);
        DeleteCommand = new AsyncRelayCommand(() => _deleteHandler(_original), () => CanDelete);
        BeginRenameCommand = new RelayCommand(() => IsEditingName = true, () => CanRename);
        AddCardCommand = new AsyncRelayCommand(() => _createCardHandler(_original));
        AddCardToTopCommand = new AsyncRelayCommand(() => _createCardAtTopHandler(_original));
    }

    public ObservableCollection<WorkItemViewModel> Cards { get; } = [];
    public TodoListRecord Record => _original;
    public IAsyncRelayCommand ArchiveCommand { get; }
    public IAsyncRelayCommand ArchiveAllCardsCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IRelayCommand BeginRenameCommand { get; }
    public IAsyncRelayCommand AddCardCommand { get; }
    public IAsyncRelayCommand AddCardToTopCommand { get; }
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
    public bool CanArchiveAllCards => Cards.Any(card => !card.Record.IsArchived);
    public bool CanRestore => _original.Status is null && _original.IsArchived;
    public bool CanDelete => _original.Status is null;
    public string ArchiveActionText => _original.IsArchived ? "恢复" : "归档";
    public string CardCountText => $"{TotalCardCount} 张卡片";
    public int TotalCardCount => _cards.Count;
    public bool HasMoreCards => _loadedCardCount < _cards.Count;
    public int AppendPosition => _cards.Count == 0 ? 0 : _cards.Max(c => c.Position) + 1;
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

    public void LoadNextCardBatch()
    {
        if (_loadedCardCount >= _cards.Count) return;
        var take = Math.Min(CardBatchSize, _cards.Count - _loadedCardCount);
        for (var i = 0; i < take; i++)
        {
            Cards.Add(_cardFactory(_cards[_loadedCardCount]));
            _loadedCardCount++;
        }
        OnPropertyChanged(nameof(HasMoreCards));
    }

    public void RefreshDueDateStates()
    {
        foreach (var card in Cards) card.RefreshDueDateState();
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
    public const string ProcessingLabelName = "处理中";

    private WorkItemViewModel(
        TodoRecord todo,
        Func<TodoRecord, Task> editHandler,
        Func<TodoRecord, Task> archiveHandler,
        Func<TodoRecord, Task> deleteHandler,
        Func<TodoRecord, Task> moveToTopHandler,
        Func<TodoRecord, Task> moveToCompletedHandler,
        Func<TodoRecord, Task> addProcessingLabelHandler,
        Func<TodoRecord, Task> removeProcessingLabelHandler,
        Func<TodoRecord, Task> addCardAboveHandler)
    {
        Record = todo;
        EditCommand = new AsyncRelayCommand(() => editHandler(todo));
        MoveToTopCommand = new AsyncRelayCommand(() => moveToTopHandler(todo));
        MoveToCompletedCommand = new AsyncRelayCommand(() => moveToCompletedHandler(todo));
        ArchiveCommand = new AsyncRelayCommand(() => archiveHandler(todo));
        DeleteCommand = new AsyncRelayCommand(() => deleteHandler(todo));
        AddProcessingLabelCommand = new AsyncRelayCommand(() => addProcessingLabelHandler(todo));
        RemoveProcessingLabelCommand = new AsyncRelayCommand(() => removeProcessingLabelHandler(todo));
        AddCardAboveCommand = new AsyncRelayCommand(() => addCardAboveHandler(todo));
        LabelBadges = (todo.Labels ?? Array.Empty<TodoLabel>())
            .Select(label => new TodoLabelBadgeViewModel(label))
            .ToList();
        ChecklistTooltipItems = (todo.Checklist ?? Array.Empty<Lumine.Fairy.Protocol.TodoChecklistItem>())
            .Select((item, index) => new TodoChecklistTooltipItemViewModel(item, index + 1))
            .ToList();
        var referenceLinkColor = (todo.Labels ?? Array.Empty<TodoLabel>()).FirstOrDefault()?.Color;
        FileReferences = (todo.FileReferences ?? Array.Empty<TodoFileReference>())
            .Select(reference => new TodoFileReferenceViewModel(reference, linkColor: referenceLinkColor))
            .ToList();
    }

    public TodoRecord Record { get; }
    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand MoveToTopCommand { get; }
    public IAsyncRelayCommand MoveToCompletedCommand { get; }
    public IAsyncRelayCommand ArchiveCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand AddProcessingLabelCommand { get; }
    public IAsyncRelayCommand RemoveProcessingLabelCommand { get; }
    public IAsyncRelayCommand AddCardAboveCommand { get; }
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
    public bool IsDueToday => GetLocalDueDate() is { } dueDate && dueDate == DateTime.Today;
    public bool IsDueThisWeek => GetLocalDueDate() is { } dueDate
        && dueDate > DateTime.Today
        && dueDate <= GetEndOfCurrentWeek(DateTime.Today);
    public bool IsDueOverdue => GetLocalDueDate() is { } dueDate && dueDate < DateTime.Today;
    public string CompletedAtText => Record.CompletedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? string.Empty;
    public string CompanyName => Record.CompanyName ?? string.Empty;
    public IReadOnlyList<TodoLabel> Labels => Record.Labels ?? Array.Empty<TodoLabel>();
    public string LabelText => string.Join("  ", Labels.Select(label => label.Name));
    public IReadOnlyList<TodoLabelBadgeViewModel> LabelBadges { get; }
    public IReadOnlyList<TodoChecklistTooltipItemViewModel> ChecklistTooltipItems { get; }
    public IReadOnlyList<TodoFileReferenceViewModel> FileReferences { get; }
    public string ChecklistSummary
        => Record.Checklist is { Count: > 0 } checklist
            ? $"{checklist.Count(item => item.IsCompleted)}/{checklist.Count}"
            : string.Empty;
    public bool HasLabels => Labels.Count > 0;
    public bool HasProcessingLabel => Labels.Any(label =>
        label.Name.Trim().Equals(ProcessingLabelName, StringComparison.OrdinalIgnoreCase));
    public bool HasChecklist => !string.IsNullOrWhiteSpace(ChecklistSummary);
    public bool HasCompletedAt => Record.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
        && Record.CompletedAt.HasValue;
    public bool HasFileReferences => FileReferences.Count > 0;
    public string EditHint => "点击卡片编辑";

    public string ArchiveActionText => Record.IsArchived ? "恢复" : "归档";
    public bool CanMoveToCompleted => Record.Status.Equals("pending", StringComparison.OrdinalIgnoreCase)
        || Record.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 待确认列中的卡片在悬浮时显示删除按钮，点击后走与“更多”菜单一致的确认删除流程。
    /// </summary>
    public bool CanQuickDelete => Record.Status.Equals("pending_confirmation", StringComparison.OrdinalIgnoreCase);

    public void RefreshDueDateState()
    {
        OnPropertyChanged(nameof(IsDueToday));
        OnPropertyChanged(nameof(IsDueThisWeek));
        OnPropertyChanged(nameof(IsDueOverdue));
    }

    private DateTime? GetLocalDueDate()
        => Record.DueAt?.ToLocalTime().Date;

    private static DateTime GetEndOfCurrentWeek(DateTime date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(6 - daysSinceMonday);
    }

    public static WorkItemViewModel From(
        TodoRecord todo,
        Func<TodoRecord, Task> editHandler,
        Func<TodoRecord, Task> archiveHandler,
        Func<TodoRecord, Task> deleteHandler,
        Func<TodoRecord, Task> moveToTopHandler,
        Func<TodoRecord, Task> moveToCompletedHandler,
        Func<TodoRecord, Task> addProcessingLabelHandler,
        Func<TodoRecord, Task> removeProcessingLabelHandler,
        Func<TodoRecord, Task> addCardAboveHandler)
        => new(todo, editHandler, archiveHandler, deleteHandler, moveToTopHandler, moveToCompletedHandler,
            addProcessingLabelHandler, removeProcessingLabelHandler, addCardAboveHandler);
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

public sealed class TodoChecklistTooltipItemViewModel
{
    public TodoChecklistTooltipItemViewModel(Lumine.Fairy.Protocol.TodoChecklistItem item, int number)
    {
        Number = number;
        Text = item.Text;
        IsCompleted = item.IsCompleted;
        StatusBrush = TodoLabelColors.CreateBrush(item.IsCompleted ? "#16A34A" : "#64748B");
    }

    public int Number { get; }
    public string Text { get; }
    public bool IsCompleted { get; }
    public string StatusIcon => IsCompleted ? "√" : "⭕";
    public string DisplayText => $"{Number}. {Text}";
    public SolidColorBrush StatusBrush { get; }
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
    public bool ShowsReadLabel => _isRead && !Record.IsResolved;
    public string HandledText => string.IsNullOrWhiteSpace(Record.ResolvedLabel) ? "已处理" : Record.ResolvedLabel!;
    public bool HasActions => Actions.Count > 0 && !Record.IsResolved;
    public string CreatedAtText => _timeZoneContext.Format(Record.CreatedAt, "yyyy-MM-dd HH:mm");

    public void RefreshTime()
        => OnPropertyChanged(nameof(CreatedAtText));

    public void MarkAsRead()
    {
        if (_isRead) return;
        _isRead = true;
        OnPropertyChanged(nameof(IsRead));
        OnPropertyChanged(nameof(ShowsReadLabel));
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
    public bool IsSkipped => Status == "skipped";
    public string Indicator => IsCompleted ? "✓" : IsError ? "!" : IsRunning ? "•" : IsSkipped ? "–" : "○";
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
