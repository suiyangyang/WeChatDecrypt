using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.TodoList.App.Services;
using Lumine.Fairy.TodoList.App.ViewModels;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class MainWindowViewModelTodoBrowseTests
{
    [Fact]
    public async Task SaveTodoWorkspaceAsync_PreservesHistoricalReportsAndScratchNotes()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = baseDirectory
        };

        var validationService = new ContactManagementValidationService();
        var store = new ContactManagementSqliteStore(options, validationService);
        var workspaceService = new ContactManagementWorkspaceService(store, validationService);

        var historicalDate = new DateOnly(2026, 3, 20);
        var currentDate = new DateOnly(2026, 3, 21);

        await workspaceService.SaveSnapshotAsync(new ContactManagementSnapshot
        {
            DailyReports =
            [
                new DailyReport
                {
                    Date = historicalDate,
                    Items =
                    [
                        new TodoEntry
                        {
                            Id = "todo-history",
                            WorkDate = historicalDate,
                            ProjectName = "历史项目",
                            Title = "历史日报",
                            Content = "历史日报",
                            WorkHours = 1m,
                            IsDaily = true,
                            SortOrder = 1
                        }
                    ],
                    TotalHours = 1m,
                    RawScratchText = "3/20\n1.历史项目: 历史日报 1",
                    FormattedReportText = "1、(历史项目)历史日报,1;"
                }
            ],
            ScratchNoteDocuments =
            [
                new ScratchNoteDocument
                {
                    Id = "scratch-note-20260320",
                    WorkDate = historicalDate,
                    Title = "2026-03-20 随手记",
                    RawText = "3/20\n1.历史项目: 历史日报 1"
                }
            ]
        });

        var initialSnapshot = await workspaceService.LoadAsync();
        var viewModel = new TodoMainWindowViewModel(
            options.DatabaseFilePath,
            workspaceService,
            initialSnapshot);

        viewModel.TodoWorkDate = new DateTimeOffset(currentDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        viewModel.ScratchNoteText = "3/21\n1.Lumine.Fairy: 周月浏览联调 2";
        viewModel.TodoEntries.Clear();
        viewModel.DailyTodoEntries.Clear();
        viewModel.TodoEntries.Add(new TodoEntryItemViewModel
        {
            Id = "todo-current",
            WorkDate = new DateTimeOffset(currentDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            ProjectName = "Lumine.Fairy",
            Title = "周月浏览联调",
            Content = "周月浏览联调",
            WorkHoursText = "2",
            IsDaily = true,
            SortOrder = 1
        });
        viewModel.DailyTodoEntries.Add(new TodoEntryItemViewModel
        {
            Id = "todo-current",
            WorkDate = new DateTimeOffset(currentDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            ProjectName = "Lumine.Fairy",
            Title = "周月浏览联调",
            Content = "周月浏览联调",
            WorkHoursText = "2",
            IsDaily = true,
            SortOrder = 1
        });
        viewModel.FormattedDailyReportText = "1、(Lumine.Fairy)周月浏览联调,2;";

        await viewModel.SaveTodoWorkspaceCommand.ExecuteAsync(null);

        var latestSnapshot = await workspaceService.LoadAsync();

        Assert.Equal(2, latestSnapshot.DailyReports.Count);
        Assert.Contains(latestSnapshot.DailyReports, report => report.Date == historicalDate);
        Assert.Contains(latestSnapshot.DailyReports, report => report.Date == currentDate);
        Assert.Equal(2, latestSnapshot.ScratchNoteDocuments.Count);
        Assert.Contains(latestSnapshot.ScratchNoteDocuments, document => document.WorkDate == historicalDate);
        Assert.Contains(latestSnapshot.ScratchNoteDocuments, document => document.WorkDate == currentDate);
    }

    [Fact]
    public async Task ClassifyFragmentAsTodoCommand_RemovesFragmentAndCreatesDailyTodo()
    {
        var (_, workspaceService, viewModel) = await CreateViewModelAsync();

        viewModel.TodoWorkDate = CreateDateOffset(new DateOnly(2026, 3, 21));
        viewModel.ScratchNoteText = "3/21\n今天先讨论方案";

        viewModel.ParseScratchNoteCommand.Execute(null);

        var fragment = Assert.Single(viewModel.UnclassifiedFragments);
        viewModel.ClassifyFragmentAsTodoCommand.Execute(fragment);

        Assert.Empty(viewModel.UnclassifiedFragments);
        var todo = Assert.Single(viewModel.DailyTodoEntries);
        Assert.Equal("今天先讨论方案", todo.Content);
        Assert.True(todo.ShouldFocusProjectNameEditor);
        Assert.Equal(TodoWorkspaceViewMode.FormattedOnly, viewModel.SelectedTodoViewMode);
        Assert.True(viewModel.HasMissingDailyWorkHours);
        Assert.Contains("待补工时", viewModel.TodoStatusBarText);
    }

    [Fact]
    public async Task ProjectNameSuggestions_IncludeProjectsAndManualClassifiedEntries()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = baseDirectory
        };

        var validationService = new ContactManagementValidationService();
        var store = new ContactManagementSqliteStore(options, validationService);
        var workspaceService = new ContactManagementWorkspaceService(store, validationService);
        await workspaceService.SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Projects =
            [
                new ProjectProfile
                {
                    Id = "project-1",
                    Name = "Lumine.Fairy"
                }
            ]
        });

        var initialSnapshot = await workspaceService.LoadAsync();
        var viewModel = new TodoMainWindowViewModel(
            options.DatabaseFilePath,
            workspaceService,
            initialSnapshot);

        viewModel.TodoWorkDate = CreateDateOffset(new DateOnly(2026, 3, 21));
        viewModel.ScratchNoteText = "3/21\n临时事项";
        viewModel.ParseScratchNoteCommand.Execute(null);

        var fragment = Assert.Single(viewModel.UnclassifiedFragments);
        viewModel.ClassifyFragmentAsTodoCommand.Execute(fragment);

        Assert.Contains("Lumine.Fairy", viewModel.ProjectNameSuggestions);
        Assert.Single(viewModel.DailyTodoEntries);
        Assert.Equal("Lumine.Fairy", viewModel.DailyTodoEntries[0].ProjectName);
    }

    [Fact]
    public async Task ProjectNameSuggestions_OrderByMostRecentUsage()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = baseDirectory
        };

        var validationService = new ContactManagementValidationService();
        var store = new ContactManagementSqliteStore(options, validationService);
        var workspaceService = new ContactManagementWorkspaceService(store, validationService);
        await workspaceService.SaveSnapshotAsync(new ContactManagementSnapshot
        {
            Projects =
            [
                new ProjectProfile { Id = "project-1", Name = "Alpha" },
                new ProjectProfile { Id = "project-2", Name = "Beta" },
                new ProjectProfile { Id = "project-3", Name = "Gamma" }
            ],
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 20),
                    Items =
                    [
                        new TodoEntry
                        {
                            Id = "todo-beta",
                            WorkDate = new DateOnly(2026, 3, 20),
                            ProjectName = "Beta",
                            Title = "较新的项目",
                            Content = "较新的项目",
                            WorkHours = 1m,
                            IsDaily = true,
                            SortOrder = 1
                        }
                    ]
                },
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 19),
                    Items =
                    [
                        new TodoEntry
                        {
                            Id = "todo-alpha",
                            WorkDate = new DateOnly(2026, 3, 19),
                            ProjectName = "Alpha",
                            Title = "更早项目",
                            Content = "更早项目",
                            WorkHours = 1m,
                            IsDaily = true,
                            SortOrder = 1
                        }
                    ]
                }
            ]
        });

        var initialSnapshot = await workspaceService.LoadAsync();
        var viewModel = new TodoMainWindowViewModel(
            options.DatabaseFilePath,
            workspaceService,
            initialSnapshot);

        Assert.Equal(new[] { "Beta", "Alpha", "Gamma" }, viewModel.ProjectNameSuggestions.Take(3).ToArray());
    }

    [Fact]
    public void TodoEntryItemViewModel_FilteredProjectNameSuggestions_FollowsKeyword()
    {
        var item = new TodoEntryItemViewModel();
        item.SetProjectNameSuggestions(["Lumine.Fairy", "FileOps", "Shell"]);

        item.ProjectName = "fi";

        Assert.Equal(new[] { "FileOps" }, item.FilteredProjectNameSuggestions.ToArray());
    }

    [Fact]
    public async Task InsertScratchNoteExampleTemplateCommand_AppendsExampleBlock()
    {
        var (_, _, viewModel) = await CreateViewModelAsync();

        viewModel.TodoWorkDate = CreateDateOffset(new DateOnly(2026, 3, 21));
        viewModel.ScratchNoteText = "已有记录";

        viewModel.InsertScratchNoteExampleTemplateCommand.Execute(null);

        Assert.Contains("已有记录", viewModel.ScratchNoteText);
        Assert.Contains("每日 Todo", viewModel.ScratchNoteText);
        Assert.Contains("客户/项目:", viewModel.ScratchNoteText);
    }

    [Fact]
    public async Task ParseScratchNoteCommand_IgnoresTemporaryIssues_WhenBuildingTodoPreview()
    {
        var (_, _, viewModel) = await CreateViewModelAsync();

        viewModel.TodoWorkDate = CreateDateOffset(new DateOnly(2026, 3, 21));
        viewModel.DailyTodoText = "1.Lumine.Fairy: 完成对照视图 1";
        viewModel.LongTermTodoText = "Lumine.Fairy\n1.支持左右拖动 ←\n2.补文案 √";
        viewModel.TemporaryIssueText = "客户/项目: 客户A\n问题: 提交按钮没反应\n方案: 补充异常提示";

        viewModel.ParseScratchNoteCommand.Execute(null);

        Assert.Equal(3, viewModel.TodoEntries.Count);
        Assert.Single(viewModel.DailyTodoEntries);
        Assert.Empty(viewModel.ClientIssueEntries);
        Assert.Contains("3 条临时问题", viewModel.TodoStatusBarText, StringComparison.Ordinal);
        Assert.DoesNotContain("客户A", viewModel.FormattedDailyReportText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScratchNoteSections_AutoSaveWithoutManualCommand()
    {
        var (_, workspaceService, viewModel) = await CreateViewModelAsync(TimeSpan.FromMilliseconds(50));
        var currentDate = new DateOnly(2026, 3, 21);

        viewModel.TodoWorkDate = CreateDateOffset(currentDate);
        viewModel.DailyTodoText = "1.Lumine.Fairy: 自动保存联调 ← 1";
        viewModel.LongTermTodoText = "Lumine.Fairy\n1.补自动保存回归 √";
        viewModel.TemporaryIssueText = "客户/项目: 客户A\n问题: 页面未刷新";
        viewModel.ParseScratchNoteCommand.Execute(null);

        ContactManagementSnapshot latestSnapshot = new();
        for (var attempt = 0; attempt < 15; attempt++)
        {
            await Task.Delay(100);
            latestSnapshot = await workspaceService.LoadAsync();
            if (latestSnapshot.ScratchNoteDocuments.Any(item => item.WorkDate == currentDate))
            {
                break;
            }
        }

        Assert.True(
            latestSnapshot.ScratchNoteDocuments.Any(item => item.WorkDate == currentDate),
            $"自动保存未落库，当前状态：{viewModel.StatusText}");

        var scratchNoteDocument = Assert.Single(latestSnapshot.ScratchNoteDocuments, item => item.WorkDate == currentDate);
        var dailyReport = Assert.Single(latestSnapshot.DailyReports, item => item.Date == currentDate);

        Assert.Contains("@@fairy-todolist-v2", scratchNoteDocument.RawText, StringComparison.Ordinal);
        Assert.Contains("自动保存联调 ← 1", scratchNoteDocument.RawText, StringComparison.Ordinal);
        Assert.Contains("补自动保存回归 √", scratchNoteDocument.RawText, StringComparison.Ordinal);
        Assert.Contains("页面未刷新", scratchNoteDocument.RawText, StringComparison.Ordinal);
        Assert.Contains("@@fairy-todolist-v2", dailyReport.RawScratchText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadRecentWeekDailyReportsCommand_UsesCurrentRangeReports()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = baseDirectory
        };

        var validationService = new ContactManagementValidationService();
        var store = new ContactManagementSqliteStore(options, validationService);
        var workspaceService = new ContactManagementWorkspaceService(store, validationService);
        await workspaceService.SaveSnapshotAsync(new ContactManagementSnapshot
        {
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 23),
                    FormattedReportText = "1、(历史项目)范围外日报,1;"
                },
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 25),
                    FormattedReportText = "1、(Lumine.Fairy)范围内历史日报,2;"
                }
            ]
        });

        var initialSnapshot = await workspaceService.LoadAsync();
        var uploadService = new FakeRecentDailyReportUploadService();
        var rangeSelectionService = new FakeDailyReportUploadRangeSelectionService(
            new DailyReportUploadRangeSelectionResult(new DateOnly(2026, 3, 24), new DateOnly(2026, 3, 30)));
        var viewModel = new TodoMainWindowViewModel(
            options.DatabaseFilePath,
            workspaceService,
            initialSnapshot,
            recentDailyReportUploadService: uploadService,
            dailyReportUploadRangeSelectionService: rangeSelectionService);

        viewModel.TodoWorkDate = CreateDateOffset(new DateOnly(2026, 3, 30));
        viewModel.FormattedDailyReportText = "1、(Lumine.Fairy)当天未保存日报,3;";

        await viewModel.UploadRecentWeekDailyReportsCommand.ExecuteAsync(null);

        Assert.Equal(new DateOnly(2026, 3, 24), uploadService.StartDate);
        Assert.Equal(new DateOnly(2026, 3, 30), uploadService.EndDate);
        Assert.Equal(new[] { new DateOnly(2026, 3, 25), new DateOnly(2026, 3, 30) }, uploadService.UploadedReports.Select(static report => report.Date).ToArray());
        Assert.Contains("已上传日报（2026-03-24 ~ 2026-03-30）", viewModel.StatusText, StringComparison.Ordinal);
    }

    private static async Task<(ContactManagementStorageOptions Options, ContactManagementWorkspaceService WorkspaceService, TodoMainWindowViewModel ViewModel)> CreateViewModelAsync(TimeSpan? scratchNoteAutoSaveDelay = null)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var options = new ContactManagementStorageOptions
        {
            BaseDirectoryPath = baseDirectory
        };

        var validationService = new ContactManagementValidationService();
        var store = new ContactManagementSqliteStore(options, validationService);
        var workspaceService = new ContactManagementWorkspaceService(store, validationService);
        var initialSnapshot = await workspaceService.LoadAsync();
        var viewModel = new TodoMainWindowViewModel(
            options.DatabaseFilePath,
            workspaceService,
            initialSnapshot,
            scratchNoteAutoSaveDelay);

        return (options, workspaceService, viewModel);
    }

    private static DateTimeOffset CreateDateOffset(DateOnly date)
        => new(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), TimeSpan.Zero);

    private sealed class FakeRecentDailyReportUploadService : IRecentDailyReportUploadService
    {
        public List<DailyReport> UploadedReports { get; } = [];

        public DateOnly StartDate { get; private set; }

        public DateOnly EndDate { get; private set; }

        public Task<RecentDailyReportUploadResult> UploadAsync(IReadOnlyList<DailyReport> reports, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
        {
            UploadedReports.Clear();
            UploadedReports.AddRange(reports.Select(static report => new DailyReport
            {
                Date = report.Date,
                FormattedReportText = report.FormattedReportText
            }));
            StartDate = startDate;
            EndDate = endDate;

            return Task.FromResult(new RecentDailyReportUploadResult(
                "tester",
                "week.txt",
                "WeeklyReports/week.txt",
                "http://localhost:10545/",
                startDate,
                endDate,
                UploadedReports.Count));
        }
    }

        private sealed class FakeDailyReportUploadRangeSelectionService(DailyReportUploadRangeSelectionResult? selectionResult) : IDailyReportUploadRangeSelectionService
        {
            public Task<DailyReportUploadRangeSelectionResult?> SelectRangeAsync(DateOnly defaultStartDate, DateOnly defaultEndDate, CancellationToken cancellationToken = default)
                => Task.FromResult(selectionResult);
        }

}