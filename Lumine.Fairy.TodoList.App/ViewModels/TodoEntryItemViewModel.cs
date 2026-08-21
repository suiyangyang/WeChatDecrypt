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
    private bool _shouldFocusProjectNameEditor;

    public ObservableCollection<string> FilteredProjectNameSuggestions { get; } = [];

    public string DisplayContent => string.IsNullOrWhiteSpace(Content) ? Title : Content;

    public bool HasSecondaryTitle => !string.IsNullOrWhiteSpace(Title)
        && !string.Equals(Title.Trim(), DisplayContent.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool HasSourceReference => !string.IsNullOrWhiteSpace(SourceText)
        && !string.Equals(SourceText.Trim(), DisplayContent.Trim(), StringComparison.OrdinalIgnoreCase)
        && !string.Equals(SourceText.Trim(), Title.Trim(), StringComparison.OrdinalIgnoreCase);

    public string SourceReferenceText => SourceText.Trim();

    public static TodoEntryItemViewModel FromModel(TodoEntry model)
    {
        return new TodoEntryItemViewModel
        {
            Id = model.Id,
            WorkDate = model.WorkDate.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(model.WorkDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), TimeSpan.Zero)
                : null,
            ProjectName = model.ProjectName,
            Title = model.Title,
            Content = model.Content,
            Status = model.Status,
            WorkHoursText = model.WorkHours?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
            IsDaily = model.IsDaily,
            IsFromScratchNote = model.IsFromScratchNote,
            SourceText = model.SourceText,
            SortOrder = model.SortOrder,
            Notes = model.Notes
        };
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
            WorkHours = TryParseWorkHours(WorkHoursText),
            IsDaily = IsDaily,
            IsFromScratchNote = IsFromScratchNote,
            SourceText = SourceText,
            SortOrder = SortOrder,
            Notes = Notes.Trim()
        };
    }

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayContent));
        OnPropertyChanged(nameof(HasSecondaryTitle));
        OnPropertyChanged(nameof(HasSourceReference));
    }

    partial void OnContentChanged(string value)
    {
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
