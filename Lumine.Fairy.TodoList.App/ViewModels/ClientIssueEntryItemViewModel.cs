using CommunityToolkit.Mvvm.ComponentModel;
using Lumine.Fairy.ContactManagement.Models;
using System.Collections.ObjectModel;

namespace Lumine.Fairy.TodoList.App.ViewModels;

public sealed partial class ClientIssueEntryItemViewModel : ObservableObject
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
    private string _clientName = string.Empty;

    [ObservableProperty]
    private string _issueLinesText = string.Empty;

    [ObservableProperty]
    private string _solutionLinesText = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _shouldFocusProjectNameEditor;

    public ObservableCollection<string> FilteredProjectNameSuggestions { get; } = [];

    public string DisplayTitle => string.IsNullOrWhiteSpace(ClientName) ? ProjectName : $"{ProjectName} / {ClientName}";

    public static ClientIssueEntryItemViewModel FromModel(ClientIssueEntry model)
    {
        return new ClientIssueEntryItemViewModel
        {
            Id = model.Id,
            WorkDate = model.WorkDate.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(model.WorkDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), TimeSpan.Zero)
                : null,
            ProjectName = model.ProjectName,
            ClientName = model.ClientName,
            IssueLinesText = MultilineTextFieldHelper.Join(model.IssueLines),
            SolutionLinesText = MultilineTextFieldHelper.Join(model.SolutionLines),
            Notes = model.Notes
        };
    }

    public ClientIssueEntry ToModel()
    {
        return new ClientIssueEntry
        {
            Id = Id,
            WorkDate = WorkDate.HasValue ? DateOnly.FromDateTime(WorkDate.Value.DateTime) : null,
            ProjectName = ProjectName.Trim(),
            ClientName = ClientName.Trim(),
            IssueLines = MultilineTextFieldHelper.Split(IssueLinesText),
            SolutionLines = MultilineTextFieldHelper.Split(SolutionLinesText),
            Notes = Notes.Trim()
        };
    }

    partial void OnProjectNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        if (!_suppressFilterRefresh)
        {
            RefreshFilteredProjectNameSuggestions();
        }
    }

    partial void OnClientNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
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
}
