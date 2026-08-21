using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumine.Fairy.TodoList.App.ViewModels;

public sealed partial class DailyReportBrowseItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandButtonText))]
    [NotifyPropertyChangedFor(nameof(ExpandIconPathData))]
    private bool _isExpanded;

    public DateOnly Date { get; init; }

    public decimal TotalHours { get; init; }

    public string PreviewText { get; init; } = string.Empty;

    public string FormattedReportText { get; init; } = string.Empty;

    public string ProjectNamesText { get; init; } = string.Empty;

    public string SearchText { get; init; } = string.Empty;

    public bool HasScratchNote { get; init; }

    public string DisplayDateText => Date.ToString("yyyy-MM-dd");

    public string SummaryText => $"{DisplayDateText} · {TotalHours:0.##}h · {ProjectNamesText}";

    public string ExpandButtonText => IsExpanded ? "收起" : "展开";

    public string ExpandIconPathData => IsExpanded
        ? "M4.5,9.75 L8,6.25 L11.5,9.75"
        : "M4.5,6.25 L8,9.75 L11.5,6.25";
}
