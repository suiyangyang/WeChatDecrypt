using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumine.Fairy.ContactManagement.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Lumine.Fairy.TodoList.App.ViewModels;

public sealed partial class HistoryWindowViewModel : ObservableObject
{
    private readonly ObservableCollection<TodoEntryViewModel> _allTodoEntries;
    private readonly Action _closeAction;
    private readonly Func<IReadOnlyList<TodoEntry>, Task>? _saveChangesAsync;
    private bool _hasPendingChanges;

    public HistoryWindowViewModel(IEnumerable<TodoEntry> allTodoEntries, Action closeAction, Func<IReadOnlyList<TodoEntry>, Task>? saveChangesAsync = null)
    {
        _allTodoEntries = new ObservableCollection<TodoEntryViewModel>(
            allTodoEntries.Select(entry => new TodoEntryViewModel(entry)));
        _closeAction = closeAction ?? throw new ArgumentNullException(nameof(closeAction));
        _saveChangesAsync = saveChangesAsync;
        FilteredTodoEntries = new ObservableCollection<TodoEntryViewModel>(_allTodoEntries);
        TodoStatusOptions = Enum.GetValues<TodoStatus>();
        foreach (var entry in _allTodoEntries)
        {
            entry.PropertyChanged += OnTodoEntryPropertyChanged;
        }

        ApplySort();
        UpdateTotalCountText();
        UpdateSelectedCountText();
        UpdateStateFlags();
    }

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _startDate;

    [ObservableProperty]
    private DateTimeOffset? _endDate;

    [ObservableProperty]
    private string _totalCountText = string.Empty;

    [ObservableProperty]
    private string _sortColumn = "WorkDate";

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private string _selectedCountText = string.Empty;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _hasEntries;

    [ObservableProperty]
    private bool _isSaving;

    private bool _isSearchExpanded;

    public bool IsSearchExpanded
    {
        get => _isSearchExpanded;
        set => SetProperty(ref _isSearchExpanded, value);
    }

    public bool HasSearchKeyword => !string.IsNullOrWhiteSpace(SearchKeyword);

    public ObservableCollection<TodoEntryViewModel> FilteredTodoEntries { get; }

    public ObservableCollection<TodoEntryViewModel> SelectedTodoEntries { get; } = [];

    public TodoStatus[] TodoStatusOptions { get; }

    [RelayCommand]
    private void Search()
    {
        var filtered = _allTodoEntries.AsEnumerable();

        // 关键字搜索
        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            var keyword = SearchKeyword.ToLowerInvariant();
            filtered = filtered.Where(entry =>
                (entry.ProjectName?.ToLowerInvariant().Contains(keyword) == true) ||
                (entry.Title?.ToLowerInvariant().Contains(keyword) == true) ||
                (entry.Content?.ToLowerInvariant().Contains(keyword) == true) ||
                (entry.Notes?.ToLowerInvariant().Contains(keyword) == true));
        }

        // 日期范围过滤
        if (StartDate.HasValue)
        {
            var startDate = DateOnly.FromDateTime(StartDate.Value.Date);
            filtered = filtered.Where(entry => entry.WorkDate >= startDate);
        }

        if (EndDate.HasValue)
        {
            var endDate = DateOnly.FromDateTime(EndDate.Value.Date);
            filtered = filtered.Where(entry => entry.WorkDate <= endDate);
        }

        ReplaceFilteredEntries(filtered);
        UpdateTotalCountText();
        UpdateStateFlags();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchKeyword = string.Empty;
        StartDate = null;
        EndDate = null;
        Search();
    }

    public void SetSearchExpanded(bool isExpanded)
    {
        IsSearchExpanded = isExpanded;
    }

    [RelayCommand]
    private void SortByColumn(string column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = false;
        }
        ApplySort();
    }

    private void ApplySort()
    {
        var currentFiltered = BuildFilteredEntries();
        var sorted = SortDescending
            ? currentFiltered.OrderByDescending(GetSortKey)
            : currentFiltered.OrderBy(GetSortKey);

        ReplaceFilteredEntries(sorted);
        UpdateTotalCountText();
        UpdateStateFlags();
    }

    private object GetSortKey(TodoEntryViewModel entry)
    {
        return SortColumn switch
        {
            "WorkDate" => entry.WorkDate ?? DateOnly.MinValue,
            "ProjectName" => entry.ProjectName ?? string.Empty,
            "Title" => entry.Title ?? string.Empty,
            "Content" => entry.Content ?? string.Empty,
            "Status" => entry.Status,
            "WorkHours" => entry.WorkHours ?? 0,
            "IsDaily" => entry.IsDaily,
            "Notes" => entry.Notes ?? string.Empty,
            _ => entry.WorkDate ?? DateOnly.MinValue
        };
    }

    [RelayCommand]
    private void AddEntry()
    {
        var defaultDate = StartDate.HasValue
            ? DateOnly.FromDateTime(StartDate.Value.DateTime)
            : EndDate.HasValue
                ? DateOnly.FromDateTime(EndDate.Value.DateTime)
                : DateOnly.FromDateTime(DateTime.Today);

        var entry = new TodoEntryViewModel(new TodoEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            WorkDate = defaultDate,
            ProjectName = string.Empty,
            Title = string.Empty,
            Content = string.Empty,
            Status = TodoStatus.NotStarted,
            WorkHours = null,
            IsDaily = true,
            SortOrder = _allTodoEntries.Count == 0 ? 0 : _allTodoEntries.Max(static item => item.SortOrder) + 1,
            Notes = string.Empty
        });

        entry.PropertyChanged += OnTodoEntryPropertyChanged;
        _allTodoEntries.Add(entry);
        ApplySort();
        MarkPendingChanges();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private void DeleteSelected()
    {
        var removedItems = SelectedTodoEntries.ToList();
        if (removedItems.Count == 0)
        {
            return;
        }

        foreach (var entry in removedItems)
        {
            entry.PropertyChanged -= OnTodoEntryPropertyChanged;
            _allTodoEntries.Remove(entry);
            FilteredTodoEntries.Remove(entry);
        }

        SelectedTodoEntries.Clear();
        UpdateSelectedCountText();
        UpdateTotalCountText();
        UpdateStateFlags();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        MarkPendingChanges();
    }

    private bool CanDeleteSelected() => SelectedTodoEntries.Count > 0;

    [RelayCommand(CanExecute = nameof(CanMarkAllCompleted))]
    private void MarkAllCompleted()
    {
        var targetEntries = SelectedTodoEntries.Count > 0
            ? SelectedTodoEntries.ToList()
            : FilteredTodoEntries.ToList();

        if (targetEntries.Count == 0)
        {
            return;
        }

        foreach (var entry in targetEntries)
        {
            entry.Status = TodoStatus.Completed;
        }

        ApplySort();
        MarkPendingChanges();
    }

    private bool CanMarkAllCompleted() => _allTodoEntries.Count > 0;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!_hasPendingChanges || _saveChangesAsync is null || IsSaving)
        {
            return;
        }

        try
        {
            IsSaving = true;
            var snapshot = _allTodoEntries
                .Select(static item => item.GetTodoEntry())
                .Select(static item => new TodoEntry
                {
                    Id = item.Id,
                    WorkDate = item.WorkDate,
                    ProjectName = item.ProjectName,
                    Title = item.Title,
                    Content = item.Content,
                    Status = item.Status,
                    Priority = item.Priority,
                    DueDate = item.DueDate,
                    WorkHours = item.WorkHours,
                    IsDaily = item.IsDaily,
                    IsFromScratchNote = item.IsFromScratchNote,
                    SourceText = item.SourceText,
                    SortOrder = item.SortOrder,
                    Notes = item.Notes,
                    Labels = [.. item.Labels],
                    Checklist = item.Checklist.Select(static checklistItem => new TodoChecklistItem
                    {
                        Id = checklistItem.Id,
                        Text = checklistItem.Text,
                        IsCompleted = checklistItem.IsCompleted
                    }).ToList(),
                    IsArchived = item.IsArchived,
                    RepeatRule = item.RepeatRule,
                    ReminderMinutesBeforeDue = item.ReminderMinutesBeforeDue
                })
                .ToList();

            await _saveChangesAsync(snapshot);
            _hasPendingChanges = false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task CloseAsync()
    {
        await SaveAsync();
        _closeAction();
    }

    public void UpdateSelection(IEnumerable<TodoEntryViewModel> selectedEntries)
    {
        ReplaceCollection(SelectedTodoEntries, selectedEntries.Distinct());
        UpdateSelectedCountText();
        UpdateStateFlags();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        MarkAllCompletedCommand.NotifyCanExecuteChanged();
    }

    private void UpdateTotalCountText()
    {
        TotalCountText = $"共 {FilteredTodoEntries.Count} 条记录";
    }

    private void UpdateSelectedCountText()
    {
        SelectedCountText = $"当前选中 {SelectedTodoEntries.Count} 条";
    }

    private IEnumerable<TodoEntryViewModel> BuildFilteredEntries()
    {
        var filtered = _allTodoEntries.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchKeyword))
        {
            var keyword = SearchKeyword.ToLowerInvariant();
            filtered = filtered.Where(entry =>
                (entry.ProjectName?.ToLowerInvariant().Contains(keyword) == true) ||
                (entry.Title?.ToLowerInvariant().Contains(keyword) == true) ||
                (entry.Content?.ToLowerInvariant().Contains(keyword) == true) ||
                (entry.Notes?.ToLowerInvariant().Contains(keyword) == true));
        }

        if (StartDate.HasValue)
        {
            var startDate = DateOnly.FromDateTime(StartDate.Value.Date);
            filtered = filtered.Where(entry => entry.WorkDate >= startDate);
        }

        if (EndDate.HasValue)
        {
            var endDate = DateOnly.FromDateTime(EndDate.Value.Date);
            filtered = filtered.Where(entry => entry.WorkDate <= endDate);
        }

        return filtered;
    }

    private void ReplaceFilteredEntries(IEnumerable<TodoEntryViewModel> entries)
    {
        ReplaceCollection(FilteredTodoEntries, entries);
        var selectedStillExists = SelectedTodoEntries.Where(FilteredTodoEntries.Contains).ToList();
        ReplaceCollection(SelectedTodoEntries, selectedStillExists);
        UpdateSelectedCountText();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private void UpdateStateFlags()
    {
        HasSelection = SelectedTodoEntries.Count > 0;
        HasEntries = _allTodoEntries.Count > 0;
    }

    private void MarkPendingChanges()
    {
        _hasPendingChanges = true;
        UpdateStateFlags();
    }

    private void OnTodoEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TodoEntryViewModel.WorkDate)
            or nameof(TodoEntryViewModel.ProjectName)
            or nameof(TodoEntryViewModel.Title)
            or nameof(TodoEntryViewModel.Content)
            or nameof(TodoEntryViewModel.Status)
            or nameof(TodoEntryViewModel.WorkHoursText)
            or nameof(TodoEntryViewModel.IsDaily)
            or nameof(TodoEntryViewModel.Notes))
        {
            MarkPendingChanges();
            if (e.PropertyName is nameof(TodoEntryViewModel.WorkDate)
                or nameof(TodoEntryViewModel.ProjectName)
                or nameof(TodoEntryViewModel.Title)
                or nameof(TodoEntryViewModel.Content)
                or nameof(TodoEntryViewModel.Notes))
            {
                ApplySort();
            }
        }
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    partial void OnSearchKeywordChanged(string value)
    {
        OnPropertyChanged(nameof(HasSearchKeyword));
        Search();
    }

    partial void OnStartDateChanged(DateTimeOffset? value)
    {
        Search();
    }

    partial void OnEndDateChanged(DateTimeOffset? value)
    {
        Search();
    }
}
