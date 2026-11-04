using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Lumine.Fairy.Desktop.ViewModels;
using Lumine.Fairy.Protocol;
using AvaloniaComboBox = Avalonia.Controls.ComboBox;
using AvaloniaTextBox = Avalonia.Controls.TextBox;

namespace Lumine.Fairy.Desktop.Views;

public partial class TodoEditWindow : Window
{
    public TodoEditWindow()
    {
        InitializeComponent();
        AddHandler(AvaloniaTextBox.TextChangedEvent, CompanyFilterTextChanged);
    }

    private static void CompanyFilterTextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (e.Source is AvaloniaTextBox { IsFocused: true } textBox
            && textBox.FindAncestorOfType<AvaloniaComboBox>() is { DataContext: TodoEditWindowViewModel viewModel } comboBox
            && comboBox.SelectedItem is null
            && viewModel.FilteredCompanyOptions.Count > 0)
        {
            comboBox.SetCurrentValue(AvaloniaComboBox.IsDropDownOpenProperty, true);
        }
    }

    private async void AddFileReference_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TodoEditWindowViewModel viewModel)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要引用的文件",
            AllowMultiple = true,
        });
        viewModel.AddFileReferences(files
            .Select(file => new TodoFileReference(GetLocalPath(file), false))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Path)));
    }

    private async void AddFolderReference_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TodoEditWindowViewModel viewModel)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择要引用的文件夹",
            AllowMultiple = true,
        });
        viewModel.AddFileReferences(folders
            .Select(folder => new TodoFileReference(GetLocalPath(folder), true))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Path)));
    }

    private static string GetLocalPath(IStorageItem item)
        => item.TryGetLocalPath() ?? item.Path?.LocalPath ?? string.Empty;
}
