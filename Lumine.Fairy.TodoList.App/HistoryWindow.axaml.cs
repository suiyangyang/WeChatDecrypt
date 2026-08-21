using Avalonia.Controls;
using Avalonia.Input;
using System.Linq;
using Lumine.Fairy.TodoList.App.ViewModels;

namespace Lumine.Fairy.TodoList.App;

public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
    }

    public HistoryWindow(HistoryWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void HistoryDataGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not HistoryWindowViewModel viewModel || sender is not DataGrid dataGrid)
        {
            return;
        }

        var selectedItems = dataGrid.SelectedItems?
            .OfType<TodoEntryViewModel>()
            .ToList() ?? [];

        viewModel.UpdateSelection(selectedItems);
    }

    private void SearchInput_OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is HistoryWindowViewModel viewModel)
        {
            viewModel.SetSearchExpanded(true);
        }
    }
}