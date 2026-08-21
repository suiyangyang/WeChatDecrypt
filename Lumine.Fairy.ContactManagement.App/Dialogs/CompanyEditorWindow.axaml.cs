using Avalonia.Controls;
using Avalonia.Interactivity;
using Lumine.Fairy.ContactManagement.App.ViewModels;

namespace Lumine.Fairy.ContactManagement.App.Dialogs;

public partial class CompanyEditorWindow : Window
{
    public CompanyEditorWindow()
        : this(CompanyItemViewModel.CreateNew())
    {
    }

    public CompanyEditorWindow(CompanyItemViewModel item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        InitializeComponent();
        DataContext = this;
    }

    public CompanyItemViewModel Item { get; }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
