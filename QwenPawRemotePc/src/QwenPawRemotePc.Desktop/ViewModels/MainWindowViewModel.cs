using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QwenPawRemotePc.Application;
using QwenPawRemotePc.Domain;
using QwenPawRemotePc.Infrastructure;
using QwenPawRemotePc.Protocol;

namespace QwenPawRemotePc.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly SqliteNotificationStore _store;
    private readonly NotificationService _notificationService;
    private readonly QwenPawClient _qwenPaw;
    private readonly WeChatDataAnalysisClient _wechat;
    private readonly WeChatAnalysisCoordinator _analysis;
    private readonly IncrementalAnalysisScheduler _scheduler;
    private readonly NotificationHttpServer _notificationApi;

    public ObservableCollection<NotificationRecord> Notifications { get; } = [];
    public ObservableCollection<QwenPawAgent> Agents { get; } = [];
    public ObservableCollection<WorkItemViewModel> WorkItems { get; } =
    [
        new("整理项目需求", "等待用户确认", "高", "微信分析"),
        new("跟进会议结论", "待处理", "中", "工作通知"),
        new("准备测试报告", "已归档", "低", "分析任务"),
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
    private const int NotificationPageSize = 100;
    private int _notificationOffset;
    private bool _isLoadingNotifications;
    private Action<NotificationRecord>? _desktopNotificationPresenter;

    public MainWindowViewModel()
    {
        if (int.TryParse(ReadEnvironment("QwenPawInitialPage"), out var initialPage) && initialPage is >= 0 and <= 4)
            SelectedPageIndex = initialPage;
        if (int.TryParse(ReadEnvironment("QwenPawInitialNotificationTab"), out var initialNotificationTab) && initialNotificationTab is >= 0 and <= 2)
            SelectedNotificationTabIndex = initialNotificationTab;
        IsNotificationSearchOpen = ReadEnvironment("QwenPawInitialSearch") == "1";
        IsNotificationMenuOpen = ReadEnvironment("QwenPawInitialNotificationMenu") == "1";
        _store = new SqliteNotificationStore();
        _notificationService = new NotificationService(_store);
        _qwenPaw = new QwenPawClient(new ProtectedTokenStore());
        _wechat = new WeChatDataAnalysisClient();
        _analysis = new WeChatAnalysisCoordinator(_wechat, _qwenPaw, _notificationService);
        _scheduler = new IncrementalAnalysisScheduler(_wechat, _qwenPaw, _analysis, _notificationService);
        _notificationApi = new NotificationHttpServer(_notificationService, token: NotificationApiToken, analysis: _analysis);
        _notificationService.NotificationAdded += OnNotificationAdded;
        _ = InitializeAsync();
    }

    public void SetDesktopNotificationPresenter(Action<NotificationRecord> presenter)
        => _desktopNotificationPresenter = presenter;

    partial void OnSelectedNotificationTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAllNotificationTabSelected));
        OnPropertyChanged(nameof(IsUnreadNotificationTabSelected));
        OnPropertyChanged(nameof(IsReadNotificationTabSelected));
        OnPropertyChanged(nameof(IsAllNotificationsNavSelected));
        OnPropertyChanged(nameof(IsUnreadNotificationsNavSelected));
        _ = ReloadNotificationsAsync();
    }

    public bool IsAllNotificationTabSelected => SelectedNotificationTabIndex == 0;
    public bool IsUnreadNotificationTabSelected => SelectedNotificationTabIndex == 1;
    public bool IsReadNotificationTabSelected => SelectedNotificationTabIndex == 2;
    public bool IsAllNotificationsNavSelected => SelectedPageIndex == 0 && SelectedNotificationTabIndex == 0;
    public bool IsUnreadNotificationsNavSelected => SelectedPageIndex == 0 && SelectedNotificationTabIndex == 1;

    partial void OnSelectedPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsNotificationPageSelected));
        OnPropertyChanged(nameof(IsWorkPageSelected));
        OnPropertyChanged(nameof(IsWechatPageSelected));
        OnPropertyChanged(nameof(IsRemotePageSelected));
        OnPropertyChanged(nameof(IsSettingsPageSelected));
        OnPropertyChanged(nameof(IsAllNotificationsNavSelected));
        OnPropertyChanged(nameof(IsUnreadNotificationsNavSelected));
    }

    public bool IsNotificationPageSelected => SelectedPageIndex == 0;
    public bool IsWorkPageSelected => SelectedPageIndex == 1;
    public bool IsWechatPageSelected => SelectedPageIndex == 2;
    public bool IsRemotePageSelected => SelectedPageIndex == 3;
    public bool IsSettingsPageSelected => SelectedPageIndex == 4;

    partial void OnNotificationSearchKeywordChanged(string value)
        => _ = ReloadNotificationsAsync();

    private async Task InitializeAsync()
    {
        try
        {
            await _notificationApi.StartAsync();
            IsNotificationApiRunning = true;
            await ReloadNotificationsAsync();

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
        if (!_qwenPaw.IsAuthenticated) { StatusMessage = "请先登录 QwenPaw。"; CurrentPage = "微信分析"; return; }
        if (SelectedAgent is null) { StatusMessage = "请先选择 QwenPaw Agent。"; CurrentPage = "微信分析"; return; }
        IsBusy = true;
        CurrentPage = "微信分析";
        StatusMessage = "正在读取最近微信会话并请求 QwenPaw 分析…";
        try
        {
            await _analysis.AnalyzeLatestAsync(SelectedAgent.Id);
            await ReloadNotificationsAsync();
            StatusMessage = "微信分析完成，已发送本地通知。";
        }
        catch (Exception ex) { StatusMessage = $"微信分析失败：{ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task MarkReadAsync(NotificationRecord? item)
    {
        if (item is null || item.IsRead) return;
        await _notificationService.MarkReadAsync(item.Id);
        await ReloadNotificationsAsync();
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
        SelectedPageIndex = Math.Clamp(pageIndex, 0, 4);
        IsNotificationSearchOpen = false;
        IsNotificationMenuOpen = false;
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
        _ = ReloadNotificationsAsync();
    }

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
            foreach (var item in items) Notifications.Add(item);
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
            StatusMessage = $"新通知：{notification.Title}";
            ShowToast(notification);
        });
    }

    private void ShowToast(NotificationRecord notification)
    {
        _desktopNotificationPresenter?.Invoke(notification);
    }

    public async ValueTask DisposeAsync()
    {
        _notificationService.NotificationAdded -= OnNotificationAdded;
        await _notificationApi.DisposeAsync();
        await _scheduler.DisposeAsync();
        _wechat.Dispose();
        _qwenPaw.Dispose();
        _store.Dispose();
    }
}

public sealed record WorkItemViewModel(string Title, string Status, string Priority, string Source);
