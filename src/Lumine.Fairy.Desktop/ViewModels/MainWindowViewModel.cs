using System.Collections.ObjectModel;
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

namespace Lumine.Fairy.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly SqliteNotificationStore _store;
    private readonly SqliteTodoStore _todoStore;
    private readonly NotificationService _notificationService;
    private readonly TodoService _todoService;
    private readonly QwenPawClient _qwenPaw;
    private readonly WeChatDataAnalysisClient _wechat;
    private readonly WeChatAnalysisCoordinator _analysis;
    private readonly IncrementalAnalysisScheduler _scheduler;
    private readonly NotificationHttpServer _notificationApi;
    private readonly AppSettingsStore _settingsStore;
    private readonly ContactManagementSqliteStore _contactManagementStore;
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
    [ObservableProperty] private WechatAutomaticAnalysisModeOption? _selectedWechatAutomaticAnalysisMode;
    [ObservableProperty] private string _notificationApiEndpoint = "http://127.0.0.1:18765/api/v1/notifications";
    [ObservableProperty] private string _notificationApiToken = "local-dev-notification-token";
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
    [ObservableProperty] private TimeZoneOption? _selectedTimeZone;
    [ObservableProperty] private string _analysisContextText = "尚未运行微信分析";
    [ObservableProperty] private string _analysisCurrentStageText = "等待开始";
    [ObservableProperty] private string _analysisOverallStatusText = "未开始";
    [ObservableProperty] private string _analysisLogText = string.Empty;
    [ObservableProperty] private bool _isAnalysisLogExpanded;
    private const int NotificationPageSize = 100;
    private int _notificationOffset;
    private bool _isLoadingNotifications;
    private DateTimeOffset? _analysisStartedAt;
    private DateTimeOffset? _analysisEndedAt;
    private Action<NotificationRecord>? _desktopNotificationPresenter;

    public MainWindowViewModel()
    {
        _settingsStore = new AppSettingsStore();
        _appSettings = _settingsStore.Load();
        _wechatAnalysisSettings = _appSettings.WeChatAnalysis ?? new WeChatAnalysisSettings();
        IsWechatWhitelistEnabled = _wechatAnalysisSettings.EnableWhitelist;
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
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(CurrentTimeText));
            OnPropertyChanged(nameof(AnalysisElapsedText));
            foreach (var stage in AnalysisStages) stage.RefreshDuration(DateTimeOffset.UtcNow);
        };
        _clockTimer.Start();

        if (int.TryParse(ReadEnvironment("QwenPawInitialPage"), out var initialPage) && initialPage is >= 0 and <= 5)
            SelectedPageIndex = initialPage;
        if (int.TryParse(ReadEnvironment("QwenPawInitialNotificationTab"), out var initialNotificationTab) && initialNotificationTab is >= 0 and <= 2)
            SelectedNotificationTabIndex = initialNotificationTab;
        IsNotificationSearchOpen = ReadEnvironment("QwenPawInitialSearch") == "1";
        IsNotificationMenuOpen = ReadEnvironment("QwenPawInitialNotificationMenu") == "1";
        _store = new SqliteNotificationStore();
        _todoStore = new SqliteTodoStore();
        _contactManagementStore = new ContactManagementSqliteStore(new ContactManagementStorageOptions());
        _notificationService = new NotificationService(_store);
        _todoService = new TodoService(_todoStore);
        _qwenPaw = new QwenPawClient(new ProtectedTokenStore());
        _wechat = new WeChatDataAnalysisClient();
        _analysis = new WeChatAnalysisCoordinator(_wechat, _qwenPaw, _notificationService, _todoService);
        _scheduler = new IncrementalAnalysisScheduler(_wechat, _qwenPaw, _analysis, _notificationService);
        _scheduler.WhitelistLoader = RefreshWechatWhitelistCatalogAsync;
        ApplyWechatAnalysisConfiguration(false);
        _notificationApi = new NotificationHttpServer(_notificationService, token: NotificationApiToken, analysis: _analysis);
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

    partial void OnSelectedAgentChanged(QwenPawAgent? value)
    {
        if (_scheduler is not null)
            _scheduler.AgentId = value?.Id ?? "default";
    }

    partial void OnSelectedNotificationTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAllNotificationTabSelected));
        OnPropertyChanged(nameof(IsUnreadNotificationTabSelected));
        OnPropertyChanged(nameof(IsReadNotificationTabSelected));
        _ = ReloadNotificationsAsync();
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
        3 => "App",
        4 => "远程设备",
        5 => "设置",
        _ => "通知中心",
    };

    public string PageSubtitle => SelectedPageIndex switch
    {
        0 => "统一查看本地应用、微信分析和 QwenPaw 任务通知",
        1 => "聚合 TODO、待跟进事项和工作类通知",
        2 => "选择 QwenPaw Agent，分析最近微信会话并生成工作通知",
        3 => "打开联系人管理和 Todo / 日报应用",
        4 => "设备连接、远程控制和设备管理",
        5 => "管理 QwenPaw 连接、本地通知接口和微信分析设置",
        _ => string.Empty,
    };

    partial void OnSelectedPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsNotificationPageSelected));
        OnPropertyChanged(nameof(IsWorkPageSelected));
        OnPropertyChanged(nameof(IsWechatPageSelected));
        OnPropertyChanged(nameof(IsAppPageSelected));
        OnPropertyChanged(nameof(IsRemotePageSelected));
        OnPropertyChanged(nameof(IsSettingsPageSelected));
        OnPropertyChanged(nameof(IsNotificationCenterNavSelected));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    }

    public bool IsNotificationPageSelected => SelectedPageIndex == 0;
    public bool IsWorkPageSelected => SelectedPageIndex == 1;
    public bool IsWechatPageSelected => SelectedPageIndex == 2;
    public bool IsAppPageSelected => SelectedPageIndex == 3;
    public bool IsRemotePageSelected => SelectedPageIndex == 4;
    public bool IsSettingsPageSelected => SelectedPageIndex == 5;

    private async Task InitializeAsync()
    {
        try
        {
            await _notificationApi.StartAsync();
            IsNotificationApiRunning = true;
            await ReloadNotificationsAsync();
            await ReloadTodosAsync();
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

        _wechatAnalysisSettings = new WeChatAnalysisSettings
        {
            EnableWhitelist = IsWechatWhitelistEnabled,
            WhitelistContactIds = WechatWhitelistContacts.Where(x => x.IsSelected).Select(x => x.Id).ToList(),
            WhitelistCompanyIds = WechatWhitelistCompanies.Where(x => x.IsSelected).Select(x => x.Id).ToList(),
            ContentTypes = contentTypes,
            AutomaticMode = SelectedWechatAutomaticAnalysisMode?.Mode ?? WeChatAutomaticAnalysisMode.OneMonth,
        };
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
            SelectedAgent ??= Agents.FirstOrDefault(x => x.Id == "default") ?? Agents.FirstOrDefault();
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
            await _analysis.AnalyzeLatestAsync(SelectedAgent.Id);
            await ReloadNotificationsAsync();
            await ReloadTodosAsync();
            StatusMessage = "微信分析完成，已发送本地通知。";
        }
        catch (Exception ex) { StatusMessage = $"微信分析失败：{ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenWechatAnalysisSettings()
        => SelectedPageIndex = 5;

    [RelayCommand]
    private async Task MarkReadAsync(NotificationRecord? item)
    {
        if (item is null || item.IsRead) return;
        await _notificationService.MarkReadAsync(item.Id);
        await ReloadNotificationsAsync();
    }

    private async Task HandleNotificationActionAsync(NotificationRecord notification, string actionId)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _analysis.HandleNotificationActionAsync(notification, actionId, SelectedAgent?.Id ?? "default");
            await ReloadNotificationsAsync();
            await ReloadTodosAsync();
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
        await ReloadNotificationsAsync();
        StatusMessage = "已将全部通知标记为已读。";
    }

    [RelayCommand]
    private void NavigateToPage(string? page)
    {
        if (!int.TryParse(page, out var pageIndex)) return;
        SelectedPageIndex = Math.Clamp(pageIndex, 0, 5);
        if (SelectedPageIndex == 0)
            SelectedNotificationTabIndex = 0;
        IsNotificationSearchOpen = false;
        IsNotificationMenuOpen = false;
    }

    [RelayCommand]
    private void OpenContactManagementApp()
        => LaunchBuiltInApp(BuiltInAppLauncher.LaunchContactManagement, "联系人管理已启动。");

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

    private async Task ReloadTodosAsync()
    {
        var todos = await _todoService.ListAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WorkItems.Clear();
            foreach (var todo in todos)
                WorkItems.Add(WorkItemViewModel.From(todo));
        });
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
                Notifications.Add(new NotificationItemViewModel(item, _timeZoneContext, HandleNotificationActionAsync));
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
            await ReloadTodosAsync();
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
            AnalysisContextText = $"账号：{progress.AccountDisplayName}  ·  会话：{progress.ConversationDisplayName}";
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
        _ = ReloadTodosAsync();
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

public sealed record WorkItemViewModel(
    string Id,
    string Title,
    string Summary,
    string Status,
    string Priority,
    string Source,
    string Assignee,
    string DueAtText)
{
    public static WorkItemViewModel From(TodoRecord todo)
        => new(
            todo.Id,
            todo.Title,
            todo.Summary,
            todo.Status switch
            {
                "pending_confirmation" => "等待确认",
                "pending" => "待处理",
                "in_progress" => "进行中",
                "completed" => "已完成",
                _ => todo.Status,
            },
            todo.Priority,
            todo.Source,
            todo.Assignee ?? string.Empty,
            todo.DueAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty);
}

public sealed record TimeZoneOption(string Id, string DisplayName, string OffsetText);

public sealed class NotificationItemViewModel : ObservableObject
{
    private readonly TimeZoneContext _timeZoneContext;
    public NotificationRecord Record { get; }
    public IReadOnlyList<NotificationActionItemViewModel> Actions { get; }

    public NotificationItemViewModel(NotificationRecord record, TimeZoneContext timeZoneContext, Func<NotificationRecord, string, Task> actionHandler)
    {
        Record = record;
        _timeZoneContext = timeZoneContext;
        Actions = record.Actions
            .Select(action => new NotificationActionItemViewModel(action, () => actionHandler(record, action.Id)))
            .ToList();
    }

    public string Id => Record.Id;
    public string Title => Record.Title;
    public string Body => Record.Body;
    public string Source => Record.Source;
    public string Category => Record.Category;
    public bool IsRead => Record.IsRead;
    public bool IsResolved => Record.IsResolved;
    public bool HasActions => Actions.Count > 0 && !Record.IsResolved;
    public string CreatedAtText => _timeZoneContext.Format(Record.CreatedAt, "yyyy-MM-dd HH:mm");

    public void RefreshTime()
        => OnPropertyChanged(nameof(CreatedAtText));
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
