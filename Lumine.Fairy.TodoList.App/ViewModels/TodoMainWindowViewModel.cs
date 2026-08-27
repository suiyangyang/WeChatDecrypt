using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumine.Fairy.ContactManagement.Core.Models;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.TodoList.App.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;

namespace Lumine.Fairy.TodoList.App.ViewModels;

public sealed partial class TodoMainWindowViewModel : ObservableObject
{
    private readonly ContactManagementWorkspaceService _workspaceService;
    private readonly ScratchNoteAutoArrangeCoordinator _scratchNoteAutoArrangeCoordinator = new();
    private readonly DailyReportFormatter _dailyReportFormatter = new();
    private readonly IRecentDailyReportUploadService _recentDailyReportUploadService;
    private readonly IDailyReportUploadRangeSelectionService _dailyReportUploadRangeSelectionService;
    private readonly ITodoListSettingsService _todoListSettingsService;
    private readonly List<ProjectProfile> _projectProfiles = [];
    private List<TodoEntry> _allTodoEntries = [];
    private bool _suppressScratchNoteAutoArrange;
    private bool _suppressTodoWorkDateRefresh;
    private bool _suppressTodoDerivedRefresh;
    private bool _suppressScratchNoteSectionSync;
    private bool _suppressScratchSectionTabNormalization;
    private DateTimeOffset? _lastScratchNoteEditedAtUtc;
    private DateTimeOffset? _lastScratchNoteArrangedAtUtc;
    private DateTimeOffset _currentScratchNoteCreatedAtUtc = DateTimeOffset.UtcNow;
    private List<DailyReport> _allDailyReports = [];
    private List<ScratchNoteDocument> _allScratchNoteDocuments = [];
    private CancellationTokenSource? _realtimeArrangeCancellationTokenSource;
    private CancellationTokenSource? _idleArrangeCancellationTokenSource;
    private CancellationTokenSource? _scratchNoteAutoSaveCancellationTokenSource;
    private readonly SemaphoreSlim _workspaceSaveSemaphore = new(1, 1);
    private readonly TimeSpan _scratchNoteAutoSaveDelay;

    public TodoMainWindowViewModel(
        string databaseFilePath,
        ContactManagementWorkspaceService workspaceService,
        ContactManagementSnapshot initialSnapshot,
        TimeSpan? scratchNoteAutoSaveDelay = null,
        IRecentDailyReportUploadService? recentDailyReportUploadService = null,
        IDailyReportUploadRangeSelectionService? dailyReportUploadRangeSelectionService = null,
        ITodoListSettingsService? todoListSettingsService = null)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
        _scratchNoteAutoSaveDelay = scratchNoteAutoSaveDelay ?? TimeSpan.FromMilliseconds(800);
        _todoListSettingsService = todoListSettingsService ?? new JsonTodoListSettingsService();
        _recentDailyReportUploadService = recentDailyReportUploadService ?? new ReportMindRecentDailyReportUploadService(_todoListSettingsService);
        _dailyReportUploadRangeSelectionService = dailyReportUploadRangeSelectionService ?? new DailyReportUploadRangeSelectionService();
        DataDirectoryText = $"数据库文件：{databaseFilePath}";

        TodoStatusOptions = Enum.GetValues<TodoStatus>();
        TodoWorkDate = CreateDateOffset(DateOnly.FromDateTime(DateTime.Today));
        SelectedTodoViewMode = TodoWorkspaceViewMode.Compare;
        TodoEntries.CollectionChanged += OnTodoEntriesCollectionChanged;
        ClientIssueEntries.CollectionChanged += OnClientIssueEntriesCollectionChanged;

        ApplySnapshot(initialSnapshot);
        StatusText = "SQLite 数据已加载，可以开始维护 Todo / 日报工作区。";
    }

    public string DataDirectoryText { get; }

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _scratchNoteText = string.Empty;

    [ObservableProperty]
    private int _scratchNoteCaretIndex;

    [ObservableProperty]
    private string _dailyTodoText = string.Empty;

    [ObservableProperty]
    private string _longTermTodoText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodoStatusBarText))]
    private string _temporaryIssueText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFormattedDailyReportText))]
    private string _formattedDailyReportText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodoStatusBarText))]
    private string _todoAutoArrangeStatusText = "实时解析已开启。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodoStatusBarText))]
    private int _recognizedTodoCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodoStatusBarText))]
    private int _recognizedClientIssueCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodoStatusBarText))]
    private int _unclassifiedFragmentCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TodoStatusBarText))]
    private int _temporaryIssueCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingDailyWorkHours))]
    [NotifyPropertyChangedFor(nameof(MissingDailyWorkHoursHintText))]
    [NotifyPropertyChangedFor(nameof(TodoStatusBarText))]
    private int _missingDailyWorkHoursCount;

    [ObservableProperty]
    private DateTimeOffset? _todoWorkDate;

    [ObservableProperty]
    private string _scratchNoteTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScratchNoteOnlyView))]
    [NotifyPropertyChangedFor(nameof(IsFormattedOnlyView))]
    [NotifyPropertyChangedFor(nameof(IsCompareView))]
    [NotifyPropertyChangedFor(nameof(SelectedTodoViewModeTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedTodoViewModeDescription))]
    private TodoWorkspaceViewMode _selectedTodoViewMode = TodoWorkspaceViewMode.Compare;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeekBrowseScope))]
    [NotifyPropertyChangedFor(nameof(IsMonthBrowseScope))]
    [NotifyPropertyChangedFor(nameof(BrowseRangeTitle))]
    private DailyReportBrowseScope _selectedBrowseScope = DailyReportBrowseScope.Week;

    [ObservableProperty]
    private string _dailyReportBrowseSearchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<string> ProjectNameSuggestions { get; } = [];

    public ObservableCollection<TodoEntryItemViewModel> TodoEntries { get; } = [];

    public ObservableCollection<TodoEntryItemViewModel> DailyTodoEntries { get; } = [];

    public ObservableCollection<ClientIssueEntryItemViewModel> ClientIssueEntries { get; } = [];

    public ObservableCollection<UnclassifiedFragmentItemViewModel> UnclassifiedFragments { get; } = [];

    public ObservableCollection<DailyReportBrowseItemViewModel> BrowseReports { get; } = [];

    public IReadOnlyList<TodoStatus> TodoStatusOptions { get; }

    public bool HasFormattedDailyReportText => !string.IsNullOrWhiteSpace(FormattedDailyReportText);

    public bool HasDailyTodoEntries => DailyTodoEntries.Count > 0;

    public bool HasTodoEntries => TodoEntries.Count > 0;

    public bool HasClientIssues => ClientIssueEntries.Count > 0;

    public bool HasUnclassifiedFragments => UnclassifiedFragments.Count > 0;

    public bool HasBrowseReports => BrowseReports.Count > 0;

    public bool HasMissingDailyWorkHours => MissingDailyWorkHoursCount > 0;

    public bool IsScratchNoteOnlyView => SelectedTodoViewMode == TodoWorkspaceViewMode.ScratchNoteOnly;

    public bool IsFormattedOnlyView => SelectedTodoViewMode == TodoWorkspaceViewMode.FormattedOnly;

    public bool IsCompareView => SelectedTodoViewMode == TodoWorkspaceViewMode.Compare;

    public bool IsWeekBrowseScope => SelectedBrowseScope == DailyReportBrowseScope.Week;

    public bool IsMonthBrowseScope => SelectedBrowseScope == DailyReportBrowseScope.Month;

    public string SelectedTodoViewModeTitle => SelectedTodoViewMode switch
    {
        TodoWorkspaceViewMode.ScratchNoteOnly => "原始随手记",
        TodoWorkspaceViewMode.FormattedOnly => "格式化结果",
        _ => "左右对照"
    };

    public string SelectedTodoViewModeDescription => SelectedTodoViewMode switch
    {
        TodoWorkspaceViewMode.ScratchNoteOnly => "只看原始 txt 输入，适合连续记录。",
        TodoWorkspaceViewMode.FormattedOnly => "只看结构化 Todo / 客户问题 / 日报结果。",
        _ => "左侧输入、右侧结果同步对照。"
    };

    public string TodayTodoTitle => $"今日 Todo（{ResolveSelectedTodoWorkDate():M/d}）";

    public string BrowseRangeTitle
    {
        get
        {
            var anchor = ResolveSelectedTodoWorkDate();
            return SelectedBrowseScope switch
            {
                DailyReportBrowseScope.Week => $"近 7 天日报（{anchor.AddDays(-6):yyyy-MM-dd} ~ {anchor:yyyy-MM-dd}）",
                _ => $"近 31 天日报（{anchor.AddDays(-30):yyyy-MM-dd} ~ {anchor:yyyy-MM-dd}）"
            };
        }
    }

    public string DefaultUploadRangePreviewText
    {
        get
        {
            var endDate = ResolveSelectedTodoWorkDate();
            var startDate = endDate.AddDays(-6);
            return $"默认上传范围：{startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}";
        }
    }

    public string UploadDailyReportsToolTipText
    {
        get
        {
            var endDate = ResolveSelectedTodoWorkDate();
            var startDate = endDate.AddDays(-6);
            return $"选择时间段上传日报（默认 {startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}）";
        }
    }

    public string MissingDailyWorkHoursHintText => MissingDailyWorkHoursCount switch
    {
        <= 0 => "今日日报项工时已填写完整。",
        1 => "还有 1 条今日日报项未填写工时，保存前建议补齐。",
        _ => $"还有 {MissingDailyWorkHoursCount} 条今日日报项未填写工时，保存前建议补齐。"
    };

    public string TodoStatusBarText => $"识别到 {RecognizedTodoCount} 条 Todo，{UnclassifiedFragmentCount} 条未识别片段，{TemporaryIssueCount} 条临时问题，{MissingDailyWorkHoursCount} 条待补工时；{TodoAutoArrangeStatusText}";

    public void UpdateStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText = message;
        }
    }

    [RelayCommand]
    private void SetTodoDateToToday()
    {
        TodoWorkDate = CreateDateOffset(DateOnly.FromDateTime(DateTime.Today));
        ScratchNoteTitle = ResolveScratchNoteTitle(ResolveSelectedTodoWorkDate());
        StatusText = "已切换到今天的 Todo / 日报工作区。";
    }

    [RelayCommand]
    private void ParseScratchNote()
    {
        CancelPendingAutoArrange();
        ArrangeScratchNote(ScratchNoteArrangeTrigger.Manual);
        StatusText = "已完成随手记解析并归置。";
    }

    [RelayCommand]
    private void InsertScratchNoteExampleTemplate()
    {
        var exampleTemplate = BuildScratchNoteExampleTemplate(ResolveSelectedTodoWorkDate());
        DailyTodoText = AppendScratchSectionText(DailyTodoText, exampleTemplate.DailyTodoText);
        LongTermTodoText = AppendScratchSectionText(LongTermTodoText, exampleTemplate.LongTermTodoText);
        TemporaryIssueText = AppendScratchSectionText(TemporaryIssueText, exampleTemplate.TemporaryIssueText);
        StatusText = "已插入示例模板，可直接改写为今天的实际记录。";
    }

    [RelayCommand]
    private void ClassifyFragmentAsTodo(UnclassifiedFragmentItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var selectedDate = ResolveSelectedTodoWorkDate();
        var (projectName, content) = SplitFragmentText(item.Text);
        var normalizedContent = string.IsNullOrWhiteSpace(content) ? item.Text.Trim() : content;

        TodoEntries.Add(new TodoEntryItemViewModel
        {
            WorkDate = CreateDateOffset(selectedDate),
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? InferPreferredProjectName() : projectName,
            Title = normalizedContent,
            Content = normalizedContent,
            IsDaily = true,
            IsFromScratchNote = true,
            SourceText = item.Text,
            SortOrder = NextTodoSortOrder(),
            ShouldFocusProjectNameEditor = true
        });

        SelectedTodoViewMode = TodoWorkspaceViewMode.FormattedOnly;
        UnclassifiedFragments.Remove(item);
        UnclassifiedFragmentCount = UnclassifiedFragments.Count;
        RefreshFormattedDailyReportPreview();
        RefreshTodoSectionState();
        StatusText = "未归类片段已转为 Todo，请补充项目名和工时。";
    }

    [RelayCommand]
    private void ClassifyFragmentAsClientIssue(UnclassifiedFragmentItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var selectedDate = ResolveSelectedTodoWorkDate();
        var (projectName, issueText) = SplitFragmentText(item.Text);

        ClientIssueEntries.Add(new ClientIssueEntryItemViewModel
        {
            WorkDate = CreateDateOffset(selectedDate),
            ProjectName = string.IsNullOrWhiteSpace(projectName) ? InferPreferredProjectName() : projectName,
            IssueLinesText = string.IsNullOrWhiteSpace(issueText) ? item.Text.Trim() : issueText,
            ShouldFocusProjectNameEditor = true
        });

        SelectedTodoViewMode = TodoWorkspaceViewMode.FormattedOnly;
        UnclassifiedFragments.Remove(item);
        UnclassifiedFragmentCount = UnclassifiedFragments.Count;
        RefreshTodoSectionState();
        StatusText = "未归类片段已转为客户问题，请继续补充客户名和解决方案。";
    }

    [RelayCommand]
    private void CycleTodoViewMode()
    {
        SelectedTodoViewMode = SelectedTodoViewMode switch
        {
            TodoWorkspaceViewMode.ScratchNoteOnly => TodoWorkspaceViewMode.FormattedOnly,
            TodoWorkspaceViewMode.FormattedOnly => TodoWorkspaceViewMode.Compare,
            _ => TodoWorkspaceViewMode.ScratchNoteOnly
        };
    }

    [RelayCommand]
    private void SetScratchNoteOnlyView()
    {
        SelectedTodoViewMode = TodoWorkspaceViewMode.ScratchNoteOnly;
    }

    [RelayCommand]
    private void SetFormattedOnlyView()
    {
        SelectedTodoViewMode = TodoWorkspaceViewMode.FormattedOnly;
    }

    [RelayCommand]
    private void SetCompareView()
    {
        SelectedTodoViewMode = TodoWorkspaceViewMode.Compare;
    }

    [RelayCommand]
    private void SetWeekBrowseScope()
    {
        SelectedBrowseScope = DailyReportBrowseScope.Week;
    }

    [RelayCommand]
    private void SetMonthBrowseScope()
    {
        SelectedBrowseScope = DailyReportBrowseScope.Month;
    }

    [RelayCommand]
    private async Task UploadRecentWeekDailyReportsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var defaultEndDate = ResolveSelectedTodoWorkDate();
        var defaultStartDate = defaultEndDate.AddDays(-6);
        var selectedRange = await _dailyReportUploadRangeSelectionService.SelectRangeAsync(defaultStartDate, defaultEndDate, CancellationToken.None);
        if (selectedRange is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var startDate = selectedRange.StartDate;
            var endDate = selectedRange.EndDate;
            var reports = BuildMergedDailyReportSnapshot()
                .Where(report => report.Date >= startDate && report.Date <= endDate)
                .OrderBy(static report => report.Date)
                .ToList();

            if (reports.Count == 0)
            {
                throw new InvalidOperationException($"{startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd} 没有可上传的日报内容。");
            }

            var result = await _recentDailyReportUploadService.UploadAsync(reports, startDate, endDate, CancellationToken.None);
            var successMessage = BuildUploadSuccessMessage(result);
            StatusText = successMessage;
            await ShowOperationFeedbackDialogAsync(
                title: "日报上传成功",
                message: string.Join(Environment.NewLine,
                [
                    successMessage,
                    $"服务地址：{result.ServerBaseUrl}",
                    $"服务端路径：{result.RelativePath}",
                    $"上传日报数：{result.UploadedReportCount}"
                ]));
        }
        catch (Exception ex)
        {
            var failureMessage = $"日报上传失败：{ex.Message}";
            StatusText = failureMessage;
            await ShowOperationFeedbackDialogAsync(
                title: "日报上传失败",
                message: failureMessage,
                isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearDailyReportBrowseSearch()
    {
        DailyReportBrowseSearchText = string.Empty;
    }

    [RelayCommand]
    private void ToggleBrowseReportExpanded(DailyReportBrowseItemViewModel? item)
    {
        if (item is not null)
        {
            item.IsExpanded = !item.IsExpanded;
            OnPropertyChanged(nameof(BrowseExpandAllButtonText));
            OnPropertyChanged(nameof(BrowseExpandAllIconPathData));
        }
    }

    [RelayCommand]
    private void ToggleAllBrowseReportsExpanded()
    {
        var shouldExpand = BrowseReports.Any(static r => !r.IsExpanded);
        foreach (var report in BrowseReports)
        {
            report.IsExpanded = shouldExpand;
        }

        OnPropertyChanged(nameof(BrowseExpandAllButtonText));
        OnPropertyChanged(nameof(BrowseExpandAllIconPathData));
    }

    public string BrowseExpandAllButtonText
        => BrowseReports.Count > 0 && BrowseReports.All(static r => r.IsExpanded) ? "全部收起" : "全部展开";

    public string BrowseExpandAllIconPathData
        => BrowseReports.Count > 0 && BrowseReports.All(static r => r.IsExpanded)
            ? "M4.5,8.25 L8,4.75 L11.5,8.25 M4.5,12.75 L8,9.25 L11.5,12.75"
            : "M4.5,4.75 L8,8.25 L11.5,4.75 M4.5,9.25 L8,12.75 L11.5,9.25";

    [RelayCommand]
    private void OpenBrowseScratchNote(DailyReportBrowseItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var workDate = item.Date;
        CancelPendingAutoArrange();
        _suppressTodoWorkDateRefresh = true;
        _suppressScratchNoteAutoArrange = true;
        try
        {
            TodoWorkDate = CreateDateOffset(workDate);
            var matchingReport = _allDailyReports.FirstOrDefault(report => report.Date == workDate);
            var matchingDocument = _allScratchNoteDocuments
                .Where(document => document.WorkDate == workDate)
                .OrderByDescending(static document => document.UpdatedAtUtc)
                .FirstOrDefault();

            ScratchNoteTitle = matchingDocument?.Title ?? ResolveScratchNoteTitle(workDate);
            LoadScratchSections(matchingDocument?.RawText ?? matchingReport?.RawScratchText ?? BuildStoredScratchNoteText(CreateDefaultScratchNoteSections(workDate)));
            FormattedDailyReportText = matchingReport?.FormattedReportText ?? string.Empty;
            _currentScratchNoteCreatedAtUtc = matchingDocument?.CreatedAtUtc ?? DateTimeOffset.UtcNow;
            _lastScratchNoteArrangedAtUtc = matchingDocument?.LastArrangedAtUtc;
            RefreshDailyTodoEntries();
            RefreshFormattedDailyReportPreview();
            StatusText = $"已定位到 {workDate:yyyy-MM-dd} 的原始随手记。";
        }
        finally
        {
            _suppressScratchNoteAutoArrange = false;
            _suppressTodoWorkDateRefresh = false;
        }
    }

    [RelayCommand]
    private async Task SaveTodoWorkspaceAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            CancelPendingAutoSave();
            await PersistTodoWorkspaceAsync(CaptureTodoWorkspaceSaveState(), refreshSnapshot: true, updateStatusMessage: true, cancellationToken: CancellationToken.None);
        });
    }

    [RelayCommand]
    private void OpenHistoryWindow()
    {
        var historyWindow = new HistoryWindow();
        var viewModel = new HistoryWindowViewModel(_allTodoEntries, () => historyWindow.Close(), SaveHistoryWindowEntriesAsync);
        historyWindow.DataContext = viewModel;
        historyWindow.Show();
    }

    [RelayCommand]
    private async Task OpenSettingsWindowAsync()
    {
        var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var settingsWindow = new TodoListSettingsWindow();
        var saved = false;
        settingsWindow.DataContext = new TodoListSettingsWindowViewModel(_todoListSettingsService, wasSaved =>
        {
            saved = wasSaved;
            settingsWindow.Close();
        });

        if (owner is not null)
        {
            await settingsWindow.ShowDialog(owner);
        }
        else
        {
            settingsWindow.Show();
            return;
        }

        if (saved)
        {
            StatusText = "TodoList 设置已保存。";
        }
    }

    private async Task SaveHistoryWindowEntriesAsync(IReadOnlyList<TodoEntry> entries)
    {
        _suppressTodoDerivedRefresh = true;
        try
        {
            _allTodoEntries = entries.Select(CloneTodoEntry).ToList();
            ReplaceCollection(TodoEntries, _allTodoEntries.Select(TodoEntryItemViewModel.FromModel));
        }
        finally
        {
            _suppressTodoDerivedRefresh = false;
        }

        RefreshDailyTodoEntries();
        RefreshProjectNameSuggestions();
        RefreshFormattedDailyReportPreview();
        RefreshBrowseReports();
        RefreshTodoSectionState();

        CancelPendingAutoSave();
        await PersistTodoWorkspaceAsync(CaptureTodoWorkspaceSaveState(), refreshSnapshot: false, updateStatusMessage: true, cancellationToken: CancellationToken.None);
    }

    partial void OnScratchNoteTextChanged(string value)
    {
        if (_suppressScratchNoteSectionSync)
        {
            return;
        }

        if (NormalizeScratchSectionTabs(value, static (viewModel, normalizedText) => viewModel.ScratchNoteText = normalizedText))
        {
            return;
        }

        SplitUnifiedTextToSections(value);

        if (_suppressScratchNoteAutoArrange)
        {
            return;
        }

        _lastScratchNoteEditedAtUtc = DateTimeOffset.UtcNow;
        ScheduleScratchNoteAutoSave();
        ScheduleAutoArrange();
    }

    partial void OnTodoWorkDateChanged(DateTimeOffset? value)
    {
        if (_suppressTodoWorkDateRefresh)
        {
            return;
        }

        RefreshDailyTodoEntries();
        RefreshFormattedDailyReportPreview();
        RefreshBrowseReports();
        ScratchNoteTitle = ResolveScratchNoteTitle(ResolveSelectedTodoWorkDate());
        OnPropertyChanged(nameof(TodayTodoTitle));
        OnPropertyChanged(nameof(BrowseRangeTitle));
        OnPropertyChanged(nameof(DefaultUploadRangePreviewText));
        OnPropertyChanged(nameof(UploadDailyReportsToolTipText));
    }

    partial void OnSelectedTodoViewModeChanged(TodoWorkspaceViewMode value)
    {
        OnPropertyChanged(nameof(IsScratchNoteOnlyView));
        OnPropertyChanged(nameof(IsFormattedOnlyView));
        OnPropertyChanged(nameof(IsCompareView));
        OnPropertyChanged(nameof(SelectedTodoViewModeTitle));
        OnPropertyChanged(nameof(SelectedTodoViewModeDescription));
        StatusText = $"已切换到 Todo 视图：{SelectedTodoViewModeTitle}。";
    }

    partial void OnSelectedBrowseScopeChanged(DailyReportBrowseScope value)
    {
        OnPropertyChanged(nameof(IsWeekBrowseScope));
        OnPropertyChanged(nameof(IsMonthBrowseScope));
        OnPropertyChanged(nameof(BrowseRangeTitle));
        RefreshBrowseReports();
    }

    partial void OnDailyReportBrowseSearchTextChanged(string value)
    {
        RefreshBrowseReports();
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

    private static string BuildUploadSuccessMessage(RecentDailyReportUploadResult result)
    {
        return $"已上传日报（{result.StartDate:yyyy-MM-dd} ~ {result.EndDate:yyyy-MM-dd}）：{result.EmployeeName} / {result.FileName}";
    }

    private async Task ShowOperationFeedbackDialogAsync(string title, string message, bool isError = false)
    {
        var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var closeButton = new Button
        {
            Content = "确定",
            MinWidth = 88,
            Padding = new Thickness(14, 6)
        };

        var headerText = new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = isError
                ? Avalonia.Media.Brush.Parse("#B91C1C")
                : Avalonia.Media.Brush.Parse("#166534")
        };

        var messagePanel = new Border
        {
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(8),
            Background = isError
                ? Avalonia.Media.Brush.Parse("#FEF2F2")
                : Avalonia.Media.Brush.Parse("#F0FDF4"),
            BorderBrush = isError
                ? Avalonia.Media.Brush.Parse("#FECACA")
                : Avalonia.Media.Brush.Parse("#BBF7D0"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = Avalonia.Media.Brush.Parse("#334155")
            }
        };
        Grid.SetRow(messagePanel, 1);

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { closeButton }
        };
        Grid.SetRow(buttonPanel, 2);

        var layoutGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 14
        };
        layoutGrid.Children.Add(headerText);
        layoutGrid.Children.Add(messagePanel);
        layoutGrid.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Title = title,
            Width = 460,
            MinWidth = 420,
            Height = 240,
            MinHeight = 220,
            CanResize = false,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Padding = new Thickness(18),
                Child = layoutGrid
            }
        };

        closeButton.Click += (_, _) => dialog.Close();

        if (owner is not null)
        {
            await dialog.ShowDialog(owner);
            return;
        }

        dialog.Show();
    }

    private void ApplySnapshot(ContactManagementSnapshot snapshot)
    {
        _projectProfiles.Clear();
        _projectProfiles.AddRange(snapshot.Projects.Select(static project => new ProjectProfile
        {
            Id = project.Id,
            Name = project.Name,
            CompanyId = project.CompanyId,
            OwnerContactId = project.OwnerContactId,
            Tags = [.. project.Tags],
            Notes = project.Notes,
            IsActive = project.IsActive
        }));

        ApplyTodoWorkspace(snapshot);
    }

    private void ApplyTodoWorkspace(ContactManagementSnapshot snapshot)
    {
        CancelPendingAutoArrange();
        CancelPendingAutoSave();
        _suppressScratchNoteAutoArrange = true;
        _suppressTodoWorkDateRefresh = true;
        _suppressTodoDerivedRefresh = true;

        try
        {
            _allTodoEntries = [.. snapshot.TodoEntries];
            ReplaceCollection(TodoEntries, snapshot.TodoEntries.Select(TodoEntryItemViewModel.FromModel));
            ReplaceCollection(ClientIssueEntries, snapshot.ClientIssues.Select(ClientIssueEntryItemViewModel.FromModel));
            ReplaceCollection(UnclassifiedFragments, []);
            _allDailyReports = DeduplicateDailyReports(snapshot.DailyReports);
            _allScratchNoteDocuments = snapshot.ScratchNoteDocuments.OrderByDescending(static item => item.UpdatedAtUtc).Select(CloneScratchNoteDocument).ToList();

            var latestReport = snapshot.DailyReports.OrderByDescending(static item => item.Date).FirstOrDefault();
            var latestDocument = snapshot.ScratchNoteDocuments.OrderByDescending(static item => item.UpdatedAtUtc).FirstOrDefault();
            var selectedDate = latestReport?.Date ?? latestDocument?.WorkDate ?? DateOnly.FromDateTime(DateTime.Today);

            TodoWorkDate = CreateDateOffset(selectedDate);
            ScratchNoteTitle = latestDocument?.Title ?? ResolveScratchNoteTitle(selectedDate);
            LoadScratchSections(latestDocument?.RawText ?? latestReport?.RawScratchText ?? BuildStoredScratchNoteText(CreateDefaultScratchNoteSections(selectedDate)));
            FormattedDailyReportText = latestReport?.FormattedReportText ?? string.Empty;
            _currentScratchNoteCreatedAtUtc = latestDocument?.CreatedAtUtc ?? DateTimeOffset.UtcNow;
            _lastScratchNoteArrangedAtUtc = latestDocument?.LastArrangedAtUtc;
            RecognizedTodoCount = TodoEntries.Count;
            RecognizedClientIssueCount = ClientIssueEntries.Count;
            UnclassifiedFragmentCount = UnclassifiedFragments.Count;
            RefreshTemporaryIssueCount();
            TodoAutoArrangeStatusText = latestDocument is null ? "实时解析已开启。" : "已加载保存的 Todo / 日报 数据。";
        }
        finally
        {
            _suppressTodoDerivedRefresh = false;
            _suppressScratchNoteAutoArrange = false;
            _suppressTodoWorkDateRefresh = false;
        }

        RefreshDailyTodoEntries();
        RefreshProjectNameSuggestions();
        RefreshFormattedDailyReportPreview();
        RefreshBrowseReports();
        RefreshTodoSectionState();
    }

    private void ArrangeScratchNote(ScratchNoteArrangeTrigger trigger)
    {
        var now = DateTimeOffset.UtcNow;
        var decision = _scratchNoteAutoArrangeCoordinator.GetArrangeDecision(trigger, now, _lastScratchNoteEditedAtUtc, _lastScratchNoteArrangedAtUtc);
        if (!decision.ShouldArrange)
        {
            TodoAutoArrangeStatusText = BuildPendingArrangeStatusText(trigger, decision.RecommendedDelay);
            if (trigger != ScratchNoteArrangeTrigger.Manual)
            {
                ScheduleArrangeAttempt(trigger, decision.RecommendedDelay);
            }

            RefreshTodoSectionState();
            return;
        }

        var workDate = ResolveSelectedTodoWorkDate();
        var document = new ScratchNoteDocument
        {
            Id = BuildScratchNoteDocumentId(workDate),
            WorkDate = workDate,
            Title = ResolveScratchNoteTitle(workDate),
            RawText = BuildParserScratchNoteText(),
            CreatedAtUtc = _currentScratchNoteCreatedAtUtc,
            UpdatedAtUtc = now,
            LastArrangedAtUtc = _lastScratchNoteArrangedAtUtc
        };

        var arranged = _scratchNoteAutoArrangeCoordinator.Arrange(document, arrangedAtUtc: now);
        _lastScratchNoteArrangedAtUtc = arranged.Document.LastArrangedAtUtc;

        _suppressTodoWorkDateRefresh = true;
        try
        {
            TodoWorkDate = CreateDateOffset(arranged.DailyReport.Date);
        }
        finally
        {
            _suppressTodoWorkDateRefresh = false;
        }

        _suppressTodoDerivedRefresh = true;
        try
        {
            ScratchNoteTitle = arranged.Document.Title;
            ReplaceCollection(TodoEntries, arranged.ParseResult.TodoEntries.Select(TodoEntryItemViewModel.FromModel));
            ReplaceCollection(ClientIssueEntries, arranged.ParseResult.ClientIssues.Select(ClientIssueEntryItemViewModel.FromModel));
            ReplaceCollection(UnclassifiedFragments, arranged.ParseResult.UnclassifiedFragments.Select(static item => new UnclassifiedFragmentItemViewModel { Text = item.Text }));
            FormattedDailyReportText = arranged.DailyReport.FormattedReportText;
            RecognizedTodoCount = arranged.ParseResult.TodoEntries.Count;
            RecognizedClientIssueCount = arranged.ParseResult.ClientIssues.Count;
            UnclassifiedFragmentCount = arranged.ParseResult.UnclassifiedFragments.Count;
            TodoAutoArrangeStatusText = decision.Depth == ScratchNoteArrangeDepth.Full
                ? "空闲/手动归置已完成。"
                : "实时结构化预览已更新。";

            UpsertInMemoryDailyReport(arranged.DailyReport);
            UpsertInMemoryScratchNote(arranged.Document);
        }
        finally
        {
            _suppressTodoDerivedRefresh = false;
        }

        RefreshDailyTodoEntries();
        RefreshFormattedDailyReportPreview();
        RefreshBrowseReports();
        RefreshTodoSectionState();
        ScheduleScratchNoteAutoSave();
    }

    private void RefreshDailyTodoEntries()
    {
        ReplaceCollection(DailyTodoEntries, TodoEntries
            .Where(IsCurrentWorkDateDailyTodo)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase));

        RefreshMissingWorkHoursState();
        OnPropertyChanged(nameof(HasDailyTodoEntries));
    }

    private bool IsCurrentWorkDateDailyTodo(TodoEntryItemViewModel item)
    {
        if (!item.IsDaily || !item.WorkDate.HasValue)
        {
            return false;
        }

        return DateOnly.FromDateTime(item.WorkDate.Value.DateTime) == ResolveSelectedTodoWorkDate();
    }

    private void RefreshProjectNameSuggestions()
    {
        var recencyOrder = BuildProjectNameRecencyOrder();
        var suggestions = _projectProfiles
            .Select(static item => item.Name)
            .Concat(TodoEntries.Select(static item => item.ProjectName))
            .Concat(ClientIssueEntries.Select(static item => item.ProjectName))
            .Concat(_allDailyReports.SelectMany(static report => report.Items.Select(static item => item.ProjectName)))
            .Concat(_allDailyReports.SelectMany(static report => report.ClientIssues.Select(static item => item.ProjectName)))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => recencyOrder.TryGetValue(name, out var order) ? order : int.MaxValue)
            .ThenBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceCollection(ProjectNameSuggestions, suggestions);
        SyncProjectNameSuggestionsToItems();
    }

    private void SyncProjectNameSuggestionsToItems()
    {
        foreach (var item in TodoEntries)
        {
            item.SetProjectNameSuggestions(ProjectNameSuggestions);
        }

        foreach (var item in ClientIssueEntries)
        {
            item.SetProjectNameSuggestions(ProjectNameSuggestions);
        }
    }

    private Dictionary<string, int> BuildProjectNameRecencyOrder()
    {
        var orderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nextOrder = 0;

        void Register(string? projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                return;
            }

            var normalizedName = projectName.Trim();
            if (orderMap.ContainsKey(normalizedName))
            {
                return;
            }

            orderMap[normalizedName] = nextOrder++;
        }

        foreach (var item in DailyTodoEntries)
        {
            Register(item.ProjectName);
        }

        foreach (var item in TodoEntries.OrderByDescending(item => item.WorkDate ?? DateTimeOffset.MinValue).ThenBy(item => item.SortOrder))
        {
            Register(item.ProjectName);
        }

        foreach (var item in ClientIssueEntries.OrderByDescending(item => item.WorkDate ?? DateTimeOffset.MinValue))
        {
            Register(item.ProjectName);
        }

        foreach (var report in _allDailyReports.OrderByDescending(static item => item.Date))
        {
            foreach (var item in report.Items.OrderBy(static item => item.SortOrder))
            {
                Register(item.ProjectName);
            }

            foreach (var issue in report.ClientIssues)
            {
                Register(issue.ProjectName);
            }
        }

        return orderMap;
    }

    private void RefreshFormattedDailyReportPreview()
    {
        var selectedDate = ResolveSelectedTodoWorkDate();
        var dailyItems = DailyTodoEntries
            .Where(item => item.WorkDate.HasValue && DateOnly.FromDateTime(item.WorkDate.Value.DateTime) == selectedDate)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.ToModel())
            .ToList();

        FormattedDailyReportText = dailyItems.Count == 0
            ? string.Empty
            : _dailyReportFormatter.Format(dailyItems);
    }

    private void RefreshMissingWorkHoursState()
    {
        MissingDailyWorkHoursCount = DailyTodoEntries.Count(static item => string.IsNullOrWhiteSpace(item.WorkHoursText));
    }

    private void RefreshTemporaryIssueCount()
    {
        TemporaryIssueCount = MultilineTextFieldHelper.Split(TemporaryIssueText).Count;
    }

    private void RefreshTodoSectionState()
    {
        OnPropertyChanged(nameof(HasFormattedDailyReportText));
        OnPropertyChanged(nameof(HasTodoEntries));
        OnPropertyChanged(nameof(HasClientIssues));
        OnPropertyChanged(nameof(HasUnclassifiedFragments));
        OnPropertyChanged(nameof(HasMissingDailyWorkHours));
        OnPropertyChanged(nameof(MissingDailyWorkHoursHintText));
        OnPropertyChanged(nameof(HasBrowseReports));
        OnPropertyChanged(nameof(BrowseRangeTitle));
        OnPropertyChanged(nameof(TodoStatusBarText));
    }

    private void OnTodoEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<TodoEntryItemViewModel>())
            {
                item.PropertyChanged -= OnTodoEntryPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<TodoEntryItemViewModel>())
            {
                item.PropertyChanged += OnTodoEntryPropertyChanged;
            }
        }

        if (_suppressTodoDerivedRefresh)
        {
            return;
        }

        RefreshProjectNameSuggestions();
        RefreshDailyTodoEntries();
        RefreshFormattedDailyReportPreview();
        RefreshTodoSectionState();
    }

    private void OnClientIssueEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<ClientIssueEntryItemViewModel>())
            {
                item.PropertyChanged -= OnClientIssueEntryPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<ClientIssueEntryItemViewModel>())
            {
                item.PropertyChanged += OnClientIssueEntryPropertyChanged;
            }
        }

        if (_suppressTodoDerivedRefresh)
        {
            return;
        }

        RefreshProjectNameSuggestions();
        RefreshTodoSectionState();
    }

    private void OnTodoEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressTodoDerivedRefresh)
        {
            return;
        }

        if (e.PropertyName is nameof(TodoEntryItemViewModel.WorkDate)
            or nameof(TodoEntryItemViewModel.ProjectName)
            or nameof(TodoEntryItemViewModel.Title)
            or nameof(TodoEntryItemViewModel.Content)
            or nameof(TodoEntryItemViewModel.Status)
            or nameof(TodoEntryItemViewModel.WorkHoursText)
            or nameof(TodoEntryItemViewModel.IsDaily)
            or nameof(TodoEntryItemViewModel.SortOrder))
        {
            if (e.PropertyName == nameof(TodoEntryItemViewModel.ProjectName))
            {
                RefreshProjectNameSuggestions();
            }

            RefreshDailyTodoEntries();
            RefreshFormattedDailyReportPreview();
            RefreshTodoSectionState();
        }
    }

    private void OnClientIssueEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressTodoDerivedRefresh)
        {
            return;
        }

        if (e.PropertyName == nameof(ClientIssueEntryItemViewModel.ProjectName))
        {
            RefreshProjectNameSuggestions();
            RefreshTodoSectionState();
        }
    }

    private void ScheduleAutoArrange()
    {
        var now = _lastScratchNoteEditedAtUtc ?? DateTimeOffset.UtcNow;
        var realtimeDecision = _scratchNoteAutoArrangeCoordinator.GetArrangeDecision(
            ScratchNoteArrangeTrigger.RealtimeEdit,
            now,
            _lastScratchNoteEditedAtUtc,
            _lastScratchNoteArrangedAtUtc);
        var idleDecision = _scratchNoteAutoArrangeCoordinator.GetArrangeDecision(
            ScratchNoteArrangeTrigger.IdleTimeout,
            now,
            _lastScratchNoteEditedAtUtc,
            _lastScratchNoteArrangedAtUtc);

        TodoAutoArrangeStatusText = BuildPendingArrangeStatusText(ScratchNoteArrangeTrigger.RealtimeEdit, realtimeDecision.RecommendedDelay);
        ScheduleArrangeAttempt(ScratchNoteArrangeTrigger.RealtimeEdit, realtimeDecision.RecommendedDelay ?? ScratchNoteAutoArrangeCoordinator.DefaultRealtimeDebounce);
        ScheduleArrangeAttempt(ScratchNoteArrangeTrigger.IdleTimeout, idleDecision.RecommendedDelay ?? ScratchNoteAutoArrangeCoordinator.DefaultStableArrangeIdle);
        RefreshTodoSectionState();
    }

    private void ScheduleArrangeAttempt(ScratchNoteArrangeTrigger trigger, TimeSpan? delay)
    {
        if (!delay.HasValue)
        {
            return;
        }

        var normalizedDelay = delay.Value < TimeSpan.Zero ? TimeSpan.Zero : delay.Value;
        var cancellationTokenSource = new CancellationTokenSource();

        if (trigger == ScratchNoteArrangeTrigger.IdleTimeout)
        {
            _idleArrangeCancellationTokenSource?.Cancel();
            _idleArrangeCancellationTokenSource?.Dispose();
            _idleArrangeCancellationTokenSource = cancellationTokenSource;
        }
        else
        {
            _realtimeArrangeCancellationTokenSource?.Cancel();
            _realtimeArrangeCancellationTokenSource?.Dispose();
            _realtimeArrangeCancellationTokenSource = cancellationTokenSource;
        }

        _ = RunScheduledArrangeAsync(trigger, normalizedDelay, cancellationTokenSource.Token);
    }

    private void ScheduleScratchNoteAutoSave()
    {
        if (_scratchNoteAutoSaveDelay < TimeSpan.Zero)
        {
            return;
        }

        var saveState = CaptureTodoWorkspaceSaveState();
        var cancellationTokenSource = new CancellationTokenSource();
        _scratchNoteAutoSaveCancellationTokenSource?.Cancel();
        _scratchNoteAutoSaveCancellationTokenSource?.Dispose();
        _scratchNoteAutoSaveCancellationTokenSource = cancellationTokenSource;
        var token = cancellationTokenSource.Token;
        _ = Task.Run(() => RunScheduledScratchNoteAutoSaveAsync(_scratchNoteAutoSaveDelay, saveState, token));
    }

    private async Task RunScheduledArrangeAsync(ScratchNoteArrangeTrigger trigger, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => ArrangeScratchNote(trigger));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunScheduledScratchNoteAutoSaveAsync(TimeSpan delay, TodoWorkspaceSaveState saveState, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await PersistTodoWorkspaceAsync(saveState, refreshSnapshot: false, updateStatusMessage: false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusText = $"随手记自动保存失败：{ex.Message}";
        }
    }

    private void CancelPendingAutoArrange()
    {
        _realtimeArrangeCancellationTokenSource?.Cancel();
        _realtimeArrangeCancellationTokenSource?.Dispose();
        _realtimeArrangeCancellationTokenSource = null;

        _idleArrangeCancellationTokenSource?.Cancel();
        _idleArrangeCancellationTokenSource?.Dispose();
        _idleArrangeCancellationTokenSource = null;
    }

    private void CancelPendingAutoSave()
    {
        _scratchNoteAutoSaveCancellationTokenSource?.Cancel();
        _scratchNoteAutoSaveCancellationTokenSource?.Dispose();
        _scratchNoteAutoSaveCancellationTokenSource = null;
    }

    private static string BuildPendingArrangeStatusText(ScratchNoteArrangeTrigger trigger, TimeSpan? delay)
    {
        if (!delay.HasValue)
        {
            return trigger == ScratchNoteArrangeTrigger.IdleTimeout ? "等待空闲后归置。" : "等待自动归置。";
        }

        return trigger == ScratchNoteArrangeTrigger.IdleTimeout
            ? $"等待空闲归置：约 {Math.Ceiling(delay.Value.TotalSeconds)} 秒。"
            : $"等待自动归置：{Math.Ceiling(delay.Value.TotalMilliseconds)}ms。";
    }

    private void RefreshBrowseReports()
    {
        var anchor = ResolveSelectedTodoWorkDate();
        var (startDate, endDate) = SelectedBrowseScope switch
        {
            DailyReportBrowseScope.Month => (anchor.AddDays(-30), anchor),
            _ => (anchor.AddDays(-6), anchor)
        };

        var keyword = DailyReportBrowseSearchText.Trim();
        var reports = _allDailyReports
            .GroupBy(static report => report.Date)
            .Select(static g => g.OrderByDescending(static r => r.TotalHours).First())
            .Where(report => report.Date >= startDate && report.Date <= endDate)
            .Where(report => string.IsNullOrWhiteSpace(keyword) || DailyReportMatchesKeyword(report, keyword))
            .OrderByDescending(static report => report.Date)
            .Select(report => new DailyReportBrowseItemViewModel
            {
                Date = report.Date,
                TotalHours = report.TotalHours,
                PreviewText = BuildBrowsePreviewText(report),
                FormattedReportText = report.FormattedReportText,
                ProjectNamesText = string.Join(" / ", report.Items.Select(static item => item.ProjectName).Where(static name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)),
                SearchText = BuildBrowseSearchText(report),
                HasScratchNote = _allScratchNoteDocuments.Any(document => document.WorkDate == report.Date),
                IsExpanded = false
            })
            .ToList();

        ReplaceCollection(BrowseReports, reports);
        OnPropertyChanged(nameof(HasBrowseReports));
        OnPropertyChanged(nameof(BrowseRangeTitle));
        OnPropertyChanged(nameof(BrowseExpandAllButtonText));
        OnPropertyChanged(nameof(BrowseExpandAllIconPathData));
    }

    private TodoWorkspaceSaveState CaptureTodoWorkspaceSaveState()
        => new(
            ResolveSelectedTodoWorkDate(),
            ResolveScratchNoteTitle(ResolveSelectedTodoWorkDate()),
            TodoEntries.Select(static item => item.ToModel()).ToList(),
            DailyTodoEntries.Select(static item => item.ToModel()).ToList(),
            ClientIssueEntries.Select(static item => item.ToModel()).ToList(),
            BuildStoredScratchNoteText(),
            FormattedDailyReportText,
            _currentScratchNoteCreatedAtUtc,
            _lastScratchNoteArrangedAtUtc);

    private async Task PersistTodoWorkspaceAsync(TodoWorkspaceSaveState saveState, bool refreshSnapshot, bool updateStatusMessage, CancellationToken cancellationToken)
    {
        await _workspaceSaveSemaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await _workspaceService.LoadAsync();
            cancellationToken.ThrowIfCancellationRequested();

            var now = DateTimeOffset.UtcNow;
            var documentId = BuildScratchNoteDocumentId(saveState.WorkDate);
            var existingDocument = snapshot.ScratchNoteDocuments.FirstOrDefault(item => string.Equals(item.Id, documentId, StringComparison.OrdinalIgnoreCase));
            var currentDocument = new ScratchNoteDocument
            {
                Id = documentId,
                WorkDate = saveState.WorkDate,
                Title = saveState.ScratchNoteTitle,
                RawText = saveState.RawScratchNoteText,
                CreatedAtUtc = existingDocument?.CreatedAtUtc ?? saveState.CreatedAtUtc,
                UpdatedAtUtc = now,
                LastArrangedAtUtc = saveState.LastArrangedAtUtc ?? now
            };

            var todoSnapshot = new ContactManagementSnapshot
            {
                Contacts = snapshot.Contacts,
                Companies = snapshot.Companies,
                Projects = snapshot.Projects,
                RelationshipLinks = snapshot.RelationshipLinks,
                TodoEntries = saveState.TodoEntries.Select(CloneTodoEntry).ToList(),
                ClientIssues = saveState.ClientIssues.Select(CloneClientIssueEntry).ToList(),
                DailyReports = MergeDailyReports(snapshot.DailyReports, saveState),
                ScratchNoteDocuments = MergeScratchNoteDocuments(snapshot.ScratchNoteDocuments, currentDocument)
            };

            var latestSnapshot = await _workspaceService.SaveSnapshotAsync(todoSnapshot);
            _currentScratchNoteCreatedAtUtc = currentDocument.CreatedAtUtc;
            _lastScratchNoteArrangedAtUtc = currentDocument.LastArrangedAtUtc;

            if (refreshSnapshot)
            {
                ApplySnapshot(latestSnapshot);
            }
            else
            {
                var deduplicatedReports = DeduplicateDailyReports(latestSnapshot.DailyReports);
                var orderedDocuments = latestSnapshot.ScratchNoteDocuments.OrderByDescending(static item => item.UpdatedAtUtc).Select(CloneScratchNoteDocument).ToList();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _allDailyReports = deduplicatedReports;
                    _allScratchNoteDocuments = orderedDocuments;
                    RefreshBrowseReports();
                });
            }

            if (updateStatusMessage)
            {
                StatusText = "Todo / 日报 数据已保存到 SQLite。";
            }
        }
        finally
        {
            _workspaceSaveSemaphore.Release();
        }
    }

    private static List<DailyReport> BuildDailyReports(TodoWorkspaceSaveState saveState)
    {
        var formatter = new DailyReportFormatter();
        var allDailyItems = saveState.TodoEntries
            .Where(static item => item.IsDaily && item.WorkDate.HasValue)
            .Select(CloneTodoEntry)
            .ToList();

        var dateGroups = allDailyItems
            .GroupBy(item => item.WorkDate!.Value)
            .ToList();

        if (dateGroups.Count == 0 && string.IsNullOrWhiteSpace(saveState.FormattedDailyReportText))
        {
            return [];
        }

        if (dateGroups.Count == 0)
        {
            return
            [
                new DailyReport
                {
                    Date = saveState.WorkDate,
                    Items = [],
                    ClientIssues = saveState.ClientIssues
                        .Where(item => !item.WorkDate.HasValue || item.WorkDate.Value == saveState.WorkDate)
                        .Select(CloneClientIssueEntry)
                        .ToList(),
                    TotalHours = 0m,
                    RawScratchText = saveState.RawScratchNoteText,
                    FormattedReportText = saveState.FormattedDailyReportText
                }
            ];
        }

        return dateGroups.Select(group =>
        {
            var items = group
                .OrderBy(static item => item.SortOrder)
                .ThenBy(static item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var date = group.Key;
            return new DailyReport
            {
                Date = date,
                Items = items,
                ClientIssues = saveState.ClientIssues
                    .Where(item => !item.WorkDate.HasValue || item.WorkDate.Value == date)
                    .Select(CloneClientIssueEntry)
                    .ToList(),
                TotalHours = items.Sum(static item => item.WorkHours ?? 0m),
                RawScratchText = date == saveState.WorkDate ? saveState.RawScratchNoteText : string.Empty,
                FormattedReportText = date == saveState.WorkDate
                    ? saveState.FormattedDailyReportText
                    : formatter.Format(items)
            };
        }).ToList();
    }

    private static List<DailyReport> MergeDailyReports(IReadOnlyList<DailyReport> existingReports, TodoWorkspaceSaveState saveState)
    {
        var newReports = BuildDailyReports(saveState);
        var updatedDates = new HashSet<DateOnly>(newReports.Select(static r => r.Date));
        var merged = existingReports
            .Where(report => !updatedDates.Contains(report.Date))
            .Select(CloneDailyReport)
            .ToList();
        merged.AddRange(newReports);
        return merged.OrderBy(static report => report.Date).ToList();
    }

    private List<ScratchNoteDocument> MergeScratchNoteDocuments(IReadOnlyList<ScratchNoteDocument> existingDocuments, ScratchNoteDocument currentDocument)
    {
        var merged = existingDocuments
            .Where(document => !string.Equals(document.Id, currentDocument.Id, StringComparison.OrdinalIgnoreCase))
            .Select(CloneScratchNoteDocument)
            .ToList();

        merged.Add(CloneScratchNoteDocument(currentDocument));
        return merged.OrderBy(static document => document.WorkDate).ThenBy(static document => document.UpdatedAtUtc).ToList();
    }

    private void UpsertInMemoryDailyReport(DailyReport report)
    {
        _allDailyReports = _allDailyReports
            .Where(existing => existing.Date != report.Date)
            .Append(CloneDailyReport(report))
            .OrderByDescending(static item => item.Date)
            .ToList();
    }

    private static List<DailyReport> DeduplicateDailyReports(IReadOnlyList<DailyReport> reports)
    {
        return reports
            .GroupBy(static r => r.Date)
            .Select(static g => g.OrderByDescending(static r => r.TotalHours).First())
            .OrderByDescending(static item => item.Date)
            .Select(CloneDailyReport)
            .ToList();
    }

    private List<DailyReport> BuildMergedDailyReportSnapshot()
    {
        return DeduplicateDailyReports(MergeDailyReports(_allDailyReports, CaptureTodoWorkspaceSaveState()));
    }

    private void UpsertInMemoryScratchNote(ScratchNoteDocument document)
    {
        _allScratchNoteDocuments = _allScratchNoteDocuments
            .Where(existing => !string.Equals(existing.Id, document.Id, StringComparison.OrdinalIgnoreCase))
            .Append(CloneScratchNoteDocument(document))
            .OrderByDescending(static item => item.UpdatedAtUtc)
            .ToList();
    }

    private static DailyReport CloneDailyReport(DailyReport report)
    {
        return new DailyReport
        {
            Date = report.Date,
            Items = report.Items.Select(CloneTodoEntry).ToList(),
            ClientIssues = report.ClientIssues.Select(CloneClientIssueEntry).ToList(),
            TotalHours = report.TotalHours,
            RawScratchText = report.RawScratchText,
            FormattedReportText = report.FormattedReportText
        };
    }

    private static ScratchNoteDocument CloneScratchNoteDocument(ScratchNoteDocument document)
    {
        return new ScratchNoteDocument
        {
            Id = document.Id,
            WorkDate = document.WorkDate,
            Title = document.Title,
            RawText = document.RawText,
            CreatedAtUtc = document.CreatedAtUtc,
            UpdatedAtUtc = document.UpdatedAtUtc,
            LastArrangedAtUtc = document.LastArrangedAtUtc
        };
    }

    private static TodoEntry CloneTodoEntry(TodoEntry item)
    {
        return new TodoEntry
        {
            Id = item.Id,
            WorkDate = item.WorkDate,
            ProjectName = item.ProjectName,
            Title = item.Title,
            Content = item.Content,
            Status = item.Status,
            WorkHours = item.WorkHours,
            IsDaily = item.IsDaily,
            IsFromScratchNote = item.IsFromScratchNote,
            SourceText = item.SourceText,
            SortOrder = item.SortOrder,
            Notes = item.Notes
        };
    }

    private static ClientIssueEntry CloneClientIssueEntry(ClientIssueEntry item)
    {
        return new ClientIssueEntry
        {
            Id = item.Id,
            WorkDate = item.WorkDate,
            ProjectName = item.ProjectName,
            ClientName = item.ClientName,
            IssueLines = [.. item.IssueLines],
            SolutionLines = [.. item.SolutionLines],
            Notes = item.Notes
        };
    }

    private static bool DailyReportMatchesKeyword(DailyReport report, string keyword)
    {
        return BuildBrowseSearchText(report).Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || report.Date.ToString("yyyy-MM-dd").Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildBrowseSearchText(DailyReport report)
    {
        return string.Join(
            " ",
            report.Items.SelectMany(item => new[] { item.ProjectName, item.Title, item.Content, item.Notes, item.SourceText })
                .Concat(report.ClientIssues.SelectMany(issue => new[]
                {
                    issue.ProjectName,
                    issue.ClientName,
                    issue.Notes,
                    string.Join(" ", issue.IssueLines),
                    string.Join(" ", issue.SolutionLines)
                }))
                .Concat([report.FormattedReportText, report.RawScratchText])
                .Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string BuildBrowsePreviewText(DailyReport report)
    {
        if (!string.IsNullOrWhiteSpace(report.FormattedReportText))
        {
            var firstLine = report.FormattedReportText.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstLine))
            {
                return firstLine;
            }
        }

        return report.Items.Select(static item => item.Content)
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text))
            ?? "暂无日报内容";
    }

    private DateOnly ResolveSelectedTodoWorkDate()
        => TodoWorkDate.HasValue ? DateOnly.FromDateTime(TodoWorkDate.Value.DateTime) : DateOnly.FromDateTime(DateTime.Today);

    private int NextTodoSortOrder()
        => TodoEntries.Count == 0 ? 1 : TodoEntries.Max(static item => item.SortOrder) + 1;

    private string InferPreferredProjectName()
        => _projectProfiles.Select(static item => item.Name)
            .Concat(DailyTodoEntries.Select(static item => item.ProjectName))
            .Concat(TodoEntries.Select(static item => item.ProjectName))
            .Concat(ClientIssueEntries.Select(static item => item.ProjectName))
            .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name))
            ?? "待补项目";

    private static (string ProjectName, string Content) SplitFragmentText(string text)
    {
        var normalized = text.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return (string.Empty, string.Empty);
        }

        var separators = new[] { '：', ':' };
        var separatorIndex = normalized.IndexOfAny(separators);
        if (separatorIndex <= 0 || separatorIndex >= normalized.Length - 1)
        {
            return (string.Empty, normalized);
        }

        return (normalized[..separatorIndex].Trim(), normalized[(separatorIndex + 1)..].Trim());
    }

    private static string BuildScratchNoteDocumentId(DateOnly workDate)
        => $"scratch-note-{workDate:yyyyMMdd}";

    private static string ResolveScratchNoteTitle(DateOnly workDate)
        => $"{workDate:yyyy-MM-dd} 随手记";

    private static ScratchNoteSections CreateDefaultScratchNoteSections(DateOnly workDate)
        => new($"1.Lumine.Fairy: ", string.Empty, string.Empty);

    private static ScratchNoteSections BuildScratchNoteExampleTemplate(DateOnly workDate)
        => new(
            DailyTodoText: $"1.Lumine.Fairy: 完成 Todo / 日报体验优化 1.5{Environment.NewLine}2.Lumine.Fairy: 整理周月浏览联调 ← 0.5",
            LongTermTodoText: $"Lumine.Fairy{Environment.NewLine}1.补未归类片段手工归置{Environment.NewLine}2.补日报工时校验提示 √",
            TemporaryIssueText: $"客户/项目: 客户A{Environment.NewLine}问题: 提交按钮点击后无反馈{Environment.NewLine}方案: 补充错误提示并记录日志");

    private bool NormalizeScratchSectionTabs(string value, Action<TodoMainWindowViewModel, string> applyNormalizedText)
    {
        if (_suppressScratchSectionTabNormalization || string.IsNullOrEmpty(value) || !value.Contains('\t'))
        {
            return false;
        }

        var normalizedText = MultilineTextFieldHelper.ConvertTabsToAlignedWhitespace(value);
        if (string.Equals(normalizedText, value, StringComparison.Ordinal))
        {
            return false;
        }

        _suppressScratchSectionTabNormalization = true;
        try
        {
            applyNormalizedText(this, normalizedText);
        }
        finally
        {
            _suppressScratchSectionTabNormalization = false;
        }

        return true;
    }

    private void LoadScratchSections(string rawText)
    {
        var (sections, caretIndex) = ParseStoredOrLegacyScratchNote(rawText);
        _suppressScratchNoteSectionSync = true;
        try
        {
            DailyTodoText = sections.DailyTodoText;
            LongTermTodoText = sections.LongTermTodoText;
            TemporaryIssueText = sections.TemporaryIssueText;
            ScratchNoteText = BuildUnifiedDisplayText(sections);
            ScratchNoteCaretIndex = Math.Min(caretIndex, ScratchNoteText.Length);
        }
        finally
        {
            _suppressScratchNoteSectionSync = false;
        }

        RefreshTemporaryIssueCount();
    }

    private void SplitUnifiedTextToSections(string unifiedText)
    {
        var sections = SplitUnifiedTextBySeparators(unifiedText);
        _suppressScratchNoteSectionSync = true;
        try
        {
            DailyTodoText = sections.DailyTodoText;
            LongTermTodoText = sections.LongTermTodoText;
            TemporaryIssueText = sections.TemporaryIssueText;
        }
        finally
        {
            _suppressScratchNoteSectionSync = false;
        }

        RefreshTemporaryIssueCount();
    }

    private static ScratchNoteSections SplitUnifiedTextBySeparators(string unifiedText)
    {
        var lines = unifiedText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sections = new List<List<string>> { new() };

        foreach (var line in lines)
        {
            if (IsSeparatorLine(line.Trim()))
            {
                sections.Add(new List<string>());
            }
            else
            {
                sections[^1].Add(line);
            }
        }

        var dailyTodoText = sections.Count > 0
            ? string.Join(Environment.NewLine, sections[0])
            : string.Empty;

        var temporaryIssueText = sections.Count >= 3
            ? string.Join(Environment.NewLine, sections[^2])
            : string.Empty;

        var longTermTodoText = sections.Count >= 3
            ? string.Join(Environment.NewLine, sections[^1])
            : sections.Count == 2
                ? string.Join(Environment.NewLine, sections[^1])
                : string.Empty;

        return new ScratchNoteSections(dailyTodoText, longTermTodoText, temporaryIssueText);
    }

    private static string BuildUnifiedDisplayText(ScratchNoteSections sections)
    {
        var parts = new List<string>();

        parts.Add(sections.DailyTodoText);

        if (!string.IsNullOrWhiteSpace(sections.TemporaryIssueText) || !string.IsNullOrWhiteSpace(sections.LongTermTodoText))
        {
            parts.Add("==============================================================================");
            parts.Add(sections.TemporaryIssueText);
            parts.Add("==============================================================================");
            parts.Add(sections.LongTermTodoText);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private string BuildParserScratchNoteText()
    {
        var workDate = ResolveSelectedTodoWorkDate();
        var builder = new List<string>();

        var normalizedDailyTodoText = NormalizeDailyTodoText(DailyTodoText);

        if (!string.IsNullOrWhiteSpace(normalizedDailyTodoText))
        {
            if (!ContainsDateLine(normalizedDailyTodoText))
            {
                builder.Add($"{workDate:M/d}");
            }

            builder.Add(normalizedDailyTodoText);
        }
        else
        {
            builder.Add($"{workDate:M/d}");
        }

        return string.Join(Environment.NewLine, builder).TrimEnd();
    }

    private string BuildStoredScratchNoteText()
        => BuildStoredScratchNoteText(new ScratchNoteSections(DailyTodoText, LongTermTodoText, TemporaryIssueText), ScratchNoteCaretIndex);

    private static string BuildStoredScratchNoteText(ScratchNoteSections sections, int caretIndex = 0)
    {
        return string.Join(
            Environment.NewLine,
            [
                "@@fairy-todolist-v2",
                $"@@caret-index:{caretIndex}",
                "@@daily-todo",
                sections.DailyTodoText,
                "@@long-term-todo",
                sections.LongTermTodoText,
                "@@temporary-issues",
                sections.TemporaryIssueText
            ]);
    }

    private (ScratchNoteSections Sections, int CaretIndex) ParseStoredOrLegacyScratchNote(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return (CreateDefaultScratchNoteSections(ResolveSelectedTodoWorkDate()), 0);
        }

        var normalizedText = rawText.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalizedText.Contains("@@fairy-todolist-v2", StringComparison.Ordinal))
        {
            return ParseStoredScratchNote(normalizedText);
        }

        return (ParseLegacyScratchNote(normalizedText), 0);
    }

    private static (ScratchNoteSections Sections, int CaretIndex) ParseStoredScratchNote(string rawText)
    {
        var lines = rawText.Split('\n');
        var dailyLines = new List<string>();
        var longTermLines = new List<string>();
        var temporaryIssueLines = new List<string>();
        var currentSection = string.Empty;
        var caretIndex = 0;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            switch (trimmedLine)
            {
                case "@@fairy-todolist-v2":
                    continue;
                case "@@daily-todo":
                    currentSection = "daily";
                    continue;
                case "@@long-term-todo":
                    currentSection = "long";
                    continue;
                case "@@temporary-issues":
                    currentSection = "temp";
                    continue;
            }

            if (trimmedLine.StartsWith("@@caret-index:", StringComparison.Ordinal))
            {
                int.TryParse(trimmedLine.AsSpan("@@caret-index:".Length), out caretIndex);
                continue;
            }

            switch (currentSection)
            {
                case "daily":
                    dailyLines.Add(line);
                    break;
                case "long":
                    longTermLines.Add(line);
                    break;
                case "temp":
                    temporaryIssueLines.Add(line);
                    break;
            }
        }

        return (new ScratchNoteSections(
            string.Join(Environment.NewLine, dailyLines),
            string.Join(Environment.NewLine, longTermLines),
            string.Join(Environment.NewLine, temporaryIssueLines)), caretIndex);
    }

    private static ScratchNoteSections ParseLegacyScratchNote(string rawText)
    {
        var lines = rawText.Split('\n').ToList();
        var separatorIndex = lines.FindIndex(static line => line.Trim().Length >= 5 && line.Trim().All(static ch => ch == '='));
        var temporaryIssueIndex = lines.FindIndex(static line => IsTemporaryIssueHeader(line));

        var dailyEndIndex = separatorIndex >= 0
            ? separatorIndex
            : temporaryIssueIndex >= 0
                ? temporaryIssueIndex
                : lines.Count;

        var dailyLines = lines.Take(dailyEndIndex).Where(static line => !IsDateLine(line)).ToList();

        List<string> longTermLines = [];
        if (separatorIndex >= 0)
        {
            var longTermEndIndex = temporaryIssueIndex >= 0 ? temporaryIssueIndex : lines.Count;
            longTermLines = lines.Skip(separatorIndex + 1).Take(Math.Max(0, longTermEndIndex - separatorIndex - 1)).ToList();
        }

        var temporaryIssueLines = temporaryIssueIndex >= 0
            ? lines.Skip(temporaryIssueIndex).ToList()
            : [];

        return new ScratchNoteSections(
            string.Join(Environment.NewLine, TrimBlankLines(dailyLines)).TrimEnd(),
            string.Join(Environment.NewLine, TrimBlankLines(longTermLines)).TrimEnd(),
            string.Join(Environment.NewLine, TrimBlankLines(temporaryIssueLines)).TrimEnd());
    }

    private static string NormalizeDailyTodoText(string rawText)
    {
        var lines = rawText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .ToList();
        return string.Join(Environment.NewLine, TrimBlankLines(lines)).TrimEnd();
    }

    private static IReadOnlyList<string> TrimBlankLines(List<string> lines)
    {
        var start = 0;
        var end = lines.Count - 1;

        while (start <= end && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
        {
            end--;
        }

        return start > end ? [] : lines.GetRange(start, end - start + 1);
    }

    private static bool IsDateLine(string line)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^(?:(\d{4})/)?\d{1,2}/\d{1,2}$");
    }

    private static bool IsSeparatorLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 5 && trimmed.All(static ch => ch == '=');
    }

    private static bool ContainsDateLine(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Any(IsDateLine);
    }

    private static bool IsTemporaryIssueHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("客户/项目:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("客户/项目：", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("客户:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("客户：", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("项目:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("项目：", StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendScratchSectionText(string currentText, string appendedText)
    {
        if (string.IsNullOrWhiteSpace(currentText))
        {
            return appendedText.TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(appendedText))
        {
            return currentText.TrimEnd();
        }

        return $"{currentText.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{appendedText.TrimEnd()}";
    }

    private sealed record ScratchNoteSections(string DailyTodoText, string LongTermTodoText, string TemporaryIssueText);

    private sealed record TodoWorkspaceSaveState(
        DateOnly WorkDate,
        string ScratchNoteTitle,
        List<TodoEntry> TodoEntries,
        List<TodoEntry> DailyTodoEntries,
        List<ClientIssueEntry> ClientIssues,
        string RawScratchNoteText,
        string FormattedDailyReportText,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? LastArrangedAtUtc);

    private static DateOnly GetWeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static DateOnly GetWeekEnd(DateOnly date)
        => GetWeekStart(date).AddDays(6);

    private static DateOnly GetMonthStart(DateOnly date)
        => new(date.Year, date.Month, 1);

    private static DateOnly GetMonthEnd(DateOnly date)
        => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private static DateTimeOffset CreateDateOffset(DateOnly date)
        => new(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), TimeSpan.Zero);

    private static void ReplaceCollection<TItem>(ObservableCollection<TItem> target, IEnumerable<TItem> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
