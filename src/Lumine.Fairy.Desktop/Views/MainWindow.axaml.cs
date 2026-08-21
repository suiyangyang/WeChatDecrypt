using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Lumine.Fairy.Desktop.ViewModels;

namespace Lumine.Fairy.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void NotificationScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer
            && scrollViewer.Extent.Height - scrollViewer.Offset.Y - scrollViewer.Viewport.Height < 120
            && DataContext is MainWindowViewModel viewModel)
            viewModel.LoadMoreNotificationsCommand.Execute(null);
    }

    private void NotificationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is Avalonia.Controls.ListBox listBox && listBox.SelectedIndex >= 0)
            listBox.SelectedIndex = -1;
    }
}
