using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Lumine.Fairy.TodoList.App.ViewModels;

namespace Lumine.Fairy.TodoList.App.Services;

public interface IDailyReportUploadRangeSelectionService
{
    Task<DailyReportUploadRangeSelectionResult?> SelectRangeAsync(
        DateOnly defaultStartDate,
        DateOnly defaultEndDate,
        CancellationToken cancellationToken = default);
}

public sealed record DailyReportUploadRangeSelectionResult(DateOnly StartDate, DateOnly EndDate);

public sealed class DailyReportUploadRangeSelectionService(Func<Window?>? ownerProvider = null) : IDailyReportUploadRangeSelectionService
{
    private readonly Func<Window?> _ownerProvider = ownerProvider ?? ResolveOwnerWindow;

    public async Task<DailyReportUploadRangeSelectionResult?> SelectRangeAsync(
        DateOnly defaultStartDate,
        DateOnly defaultEndDate,
        CancellationToken cancellationToken = default)
    {
        var window = new DailyReportUploadRangeWindow();
        DailyReportUploadRangeWindowViewModel? viewModel = null;
        viewModel = new DailyReportUploadRangeWindowViewModel(defaultStartDate, defaultEndDate, confirmed =>
        {
            if (confirmed)
            {
                window.Close(viewModel!.SelectedRange);
                return;
            }

            window.Close(null);
        });

        window.DataContext = viewModel;

        var owner = _ownerProvider();
        if (owner is not null)
        {
            return await window.ShowDialog<DailyReportUploadRangeSelectionResult?>(owner);
        }

        var completionSource = new TaskCompletionSource<DailyReportUploadRangeSelectionResult?>();
        void ClosedHandler(object? _, EventArgs __)
        {
            window.Closed -= ClosedHandler;
            completionSource.TrySetResult(viewModel.SelectedRange);
        }

        window.Closed += ClosedHandler;
        window.Show();

        using var registration = cancellationToken.Register(() => completionSource.TrySetCanceled(cancellationToken));
        return await completionSource.Task;
    }

    private static Window? ResolveOwnerWindow()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}