using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumine.Fairy.TodoList.App.Services;

namespace Lumine.Fairy.TodoList.App.ViewModels;

public sealed partial class DailyReportUploadRangeWindowViewModel : ObservableObject
{
    private readonly Action<bool> _closeAction;

    public DailyReportUploadRangeWindowViewModel(
        DateOnly defaultStartDate,
        DateOnly defaultEndDate,
        Action<bool> closeAction)
    {
        _closeAction = closeAction ?? throw new ArgumentNullException(nameof(closeAction));
        StartDate = CreateDateOffset(defaultStartDate);
        EndDate = CreateDateOffset(defaultEndDate);
    }

    [ObservableProperty]
    private DateTimeOffset? _startDate;

    [ObservableProperty]
    private DateTimeOffset? _endDate;

    [ObservableProperty]
    private string _statusText = "支持跨节假日补传，按所选开始/结束日期上传日报。";

    public string SelectedRangePreviewText
    {
        get
        {
            if (!StartDate.HasValue || !EndDate.HasValue)
            {
                return "当前选择：请先选择开始日期和结束日期。";
            }

            var startDate = DateOnly.FromDateTime(StartDate.Value.DateTime);
            var endDate = DateOnly.FromDateTime(EndDate.Value.DateTime);
            return $"当前选择：{startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd}";
        }
    }

    partial void OnStartDateChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(SelectedRangePreviewText));
    }

    partial void OnEndDateChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(SelectedRangePreviewText));
    }

    public DailyReportUploadRangeSelectionResult? SelectedRange { get; private set; }

    [RelayCommand]
    private void Confirm()
    {
        if (!StartDate.HasValue || !EndDate.HasValue)
        {
            StatusText = "请先选择开始日期和结束日期。";
            return;
        }

        var startDate = DateOnly.FromDateTime(StartDate.Value.DateTime);
        var endDate = DateOnly.FromDateTime(EndDate.Value.DateTime);
        if (startDate > endDate)
        {
            StatusText = "开始日期不能晚于结束日期。";
            return;
        }

        SelectedRange = new DailyReportUploadRangeSelectionResult(startDate, endDate);
        _closeAction(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        SelectedRange = null;
        _closeAction(false);
    }

    private static DateTimeOffset CreateDateOffset(DateOnly date)
        => new(DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), TimeSpan.Zero);
}