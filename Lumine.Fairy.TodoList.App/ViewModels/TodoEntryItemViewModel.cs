using CommunityToolkit.Mvvm.ComponentModel;
using Lumine.Fairy.ContactManagement.Models;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Lumine.Fairy.TodoList.App.ViewModels;

public sealed partial class TodoEntryItemViewModel : ObservableObject
{
    private List<string> _allProjectNameSuggestions = [];
    private bool _suppressFilterRefresh;

    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private DateTimeOffset? _workDate;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private TodoStatus _status = TodoStatus.NotStarted;

    [ObservableProperty]
    private TodoPriority _priority = TodoPriority.Normal;

    [ObservableProperty]
    private DateTimeOffset? _dueDate;

    [ObservableProperty]
    private string _workHoursText = string.Empty;

    [ObservableProperty]
    private bool _isDaily;

    [ObservableProperty]
    private bool _isFromScratchNote;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private int _sortOrder;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private string _labelsText = string.Empty;

    [ObservableProperty]
    private bool _isArchived;

    [ObservableProperty]
    private string _repeatRule = "不重复";

    [ObservableProperty]
    private string _reminderMinutesText = string.Empty;

    public ObservableCollection<TodoChecklistItem> Checklist { get; } = [];

    public int CompletedChecklistCount => Checklist.Count(item => item.IsCompleted);

    public string ChecklistProgressText => Checklist.Count == 0
        ? string.Empty
        : $"{CompletedChecklistCount}/{Checklist.Count}";

    public bool HasChecklist => Checklist.Count > 0;

    public bool CanComplete => Status != TodoStatus.Completed;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? string.IsNullOrWhiteSpace(Content) ? "未命名 Todo" : Content.Trim()
        : Title.Trim();

    public string PriorityText => Priority switch
    {
        TodoPriority.Urgent => "紧急",
        TodoPriority.High => "高",
        TodoPriority.Low => "低",
        _ => "普通"
    };

    public string DueDateText => DueDate.HasValue ? $"截止 {DueDate.Value:yyyy-MM-dd}" : "未设置截止日期";

    public bool HasDueDate => DueDate.HasValue;

    public bool IsOverdue => DueDate.HasValue
        && DateOnly.FromDateTime(DueDate.Value.DateTime) < DateOnly.FromDateTime(DateTime.Today)
        && Status != TodoStatus.Completed
        && !IsArchived;

    [ObservableProperty]
    private bool _shouldFocusProjectNameEditor;

    public ObservableCollection<string> FilteredProjectNameSuggestions { get; } = [];

    public IReadOnlyList<TodoStatus> TodoStatusOptions { get; } = Enum.GetValues<TodoStatus>();

    public IReadOnlyList<TodoPriority> TodoPriorityOptions { get; } = Enum.GetValues<TodoPriority>();

    public IReadOnlyList<string> TodoRepeatOptions { get; } = ["不重复", "每天", "每周", "每月"];

    public string DisplayContent => string.IsNullOrWhiteSpace(Content) ? Title : Content;

    public bool HasSecondaryTitle => !string.IsNullOrWhiteSpace(Title)
        && !string.Equals(Title.Trim(), DisplayContent.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool HasSourceReference => !string.IsNullOrWhiteSpace(SourceText)
        && !string.Equals(SourceText.Trim(), DisplayContent.Trim(), StringComparison.OrdinalIgnoreCase)
        && !string.Equals(SourceText.Trim(), Title.Trim(), StringComparison.OrdinalIgnoreCase);

    public string SourceReferenceText => SourceText.Trim();

    public static TodoEntryItemViewModel FromModel(TodoEntry model)
    {
        var viewModel = new TodoEntryItemViewModel
        {
            Id = model.Id,
            WorkDate = model.WorkDate.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(model.WorkDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), TimeSpan.Zero)
                : null,
            ProjectName = model.ProjectName,
            Title = model.Title,
            Content = model.Content,
            Status = model.Status,
            Priority = model.Priority,
            DueDate = model.DueDate.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(model.DueDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), TimeSpan.Zero)
                : null,
            WorkHoursText = model.WorkHours?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
            IsDaily = model.IsDaily,
            IsFromScratchNote = model.IsFromScratchNote,
            SourceText = model.SourceText,
            SortOrder = model.SortOrder,
            Notes = model.Notes,
            LabelsText = string.Join(", ", model.Labels),
            IsArchived = model.IsArchived,
            RepeatRule = string.IsNullOrWhiteSpace(model.RepeatRule) ? "不重复" : model.RepeatRule,
            ReminderMinutesText = model.ReminderMinutesBeforeDue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
        };

        foreach (var checklistItem in model.Checklist)
        {
            viewModel.Checklist.Add(new TodoChecklistItem
            {
                Id = checklistItem.Id,
                Text = checklistItem.Text,
                IsCompleted = checklistItem.IsCompleted
            });
        }

        viewModel.RefreshChecklistState();
        return viewModel;
    }

    public TodoEntry ToModel()
    {
        return new TodoEntry
        {
            Id = Id,
            WorkDate = WorkDate.HasValue ? DateOnly.FromDateTime(WorkDate.Value.DateTime) : null,
            ProjectName = ProjectName.Trim(),
            Title = Title.Trim(),
            Content = Content.Trim(),
            Status = Status,
            Priority = Priority,
            DueDate = DueDate.HasValue ? DateOnly.FromDateTime(DueDate.Value.DateTime) : null,
            WorkHours = TryParseWorkHours(WorkHoursText),
            IsDaily = IsDaily,
            IsFromScratchNote = IsFromScratchNote,
            SourceText = SourceText,
            SortOrder = SortOrder,
            Notes = Notes.Trim(),
            Labels = LabelsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Checklist = Checklist.ToList(),
            IsArchived = IsArchived,
            RepeatRule = string.IsNullOrWhiteSpace(RepeatRule) ? "不重复" : RepeatRule,
            ReminderMinutesBeforeDue = int.TryParse(ReminderMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var reminderMinutes)
                && reminderMinutes >= 0
                ? reminderMinutes
                : null
        };
    }

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(HasSecondaryTitle));
        OnPropertyChanged(nameof(HasSourceReference));
    }

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(HasSecondaryTitle));
        OnPropertyChanged(nameof(HasSourceReference));
    }

    partial void OnSourceTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSourceReference));
        OnPropertyChanged(nameof(SourceReferenceText));
    }

    partial void OnProjectNameChanged(string value)
    {
        if (!_suppressFilterRefresh)
        {
            RefreshFilteredProjectNameSuggestions();
        }
    }

    partial void OnStatusChanged(TodoStatus value)
    {
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(IsOverdue));
    }

    partial void OnDueDateChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(HasDueDate));
        OnPropertyChanged(nameof(DueDateText));
        OnPropertyChanged(nameof(IsOverdue));
    }

    partial void OnPriorityChanged(TodoPriority value)
    {
        OnPropertyChanged(nameof(PriorityText));
    }

    partial void OnIsArchivedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsOverdue));
    }

    public void RefreshChecklistState()
    {
        OnPropertyChanged(nameof(CompletedChecklistCount));
        OnPropertyChanged(nameof(ChecklistProgressText));
        OnPropertyChanged(nameof(HasChecklist));
    }

    /// <summary>
    /// Temporarily suppress filter refresh (e.g. when user selects an item from dropdown).
    /// </summary>
    public void SuppressFilterRefresh(Action action)
    {
        _suppressFilterRefresh = true;
        try
        {
            action();
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    public void SetProjectNameSuggestions(IEnumerable<string> suggestions)
    {
        _allProjectNameSuggestions = suggestions
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        RefreshFilteredProjectNameSuggestions();
    }

    private void RefreshFilteredProjectNameSuggestions()
    {
        var keyword = ProjectName.Trim();
        var filtered = _allProjectNameSuggestions
            .Where(item => string.IsNullOrWhiteSpace(keyword) || item.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Incremental update to avoid replacing ItemsSource which closes the dropdown
        for (var i = FilteredProjectNameSuggestions.Count - 1; i >= 0; i--)
        {
            if (!filtered.Contains(FilteredProjectNameSuggestions[i], StringComparer.OrdinalIgnoreCase))
            {
                FilteredProjectNameSuggestions.RemoveAt(i);
            }
        }

        for (var i = 0; i < filtered.Count; i++)
        {
            var item = filtered[i];
            var existingIndex = -1;
            for (var j = 0; j < FilteredProjectNameSuggestions.Count; j++)
            {
                if (string.Equals(FilteredProjectNameSuggestions[j], item, StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                FilteredProjectNameSuggestions.Insert(i, item);
            }
            else if (existingIndex != i)
            {
                FilteredProjectNameSuggestions.Move(existingIndex, i);
            }
        }
    }

    private static decimal? TryParseWorkHours(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var invariantValue))
        {
            return invariantValue;
        }

        return decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out var currentValue)
            ? currentValue
            : null;
    }
}
