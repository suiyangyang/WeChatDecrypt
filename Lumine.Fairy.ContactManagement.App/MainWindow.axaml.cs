using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lumine.Fairy.ContactManagement.App.Dialogs;
using Lumine.Fairy.ContactManagement.App.ViewModels;

namespace Lumine.Fairy.ContactManagement.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void PickImportFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ContactMainWindowViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择导入 JSON / Excel 文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("数据交换文件") { Patterns = ["*.json", "*.xlsx"] },
                new FilePickerFileType("JSON 文件") { Patterns = ["*.json"] },
                new FilePickerFileType("Excel 文件") { Patterns = ["*.xlsx"] },
                new FilePickerFileType("所有文件") { Patterns = ["*.*"] }
            ]
        });

        var filePath = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            viewModel.SetImportFilePath(filePath);
        }
    }

    private async void PickExportFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ContactMainWindowViewModel viewModel)
        {
            return;
        }

        var pickerResult = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择导出 JSON / Excel 文件",
            SuggestedFileName = Path.GetFileName(viewModel.ExportFilePath),
            DefaultExtension = Path.GetExtension(viewModel.ExportFilePath).TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("数据交换文件") { Patterns = ["*.json", "*.xlsx"] },
                new FilePickerFileType("JSON 文件") { Patterns = ["*.json"] },
                new FilePickerFileType("Excel 文件") { Patterns = ["*.xlsx"] },
                new FilePickerFileType("所有文件") { Patterns = ["*.*"] }
            ]
        });

        var filePath = pickerResult?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            viewModel.SetExportFilePath(filePath);
        }
    }

    private async void NewContact_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ContactMainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedContact = null;
        var draft = ContactItemViewModel.CreateNew();
        var dialog = new ContactEditorWindow(draft, viewModel.RelationshipTypeOptions, viewModel.CompanyOptions.ToList(), viewModel.ProjectOptions.ToList());
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        await viewModel.SaveContactDraftAsync(draft);
    }

    private async void EditContact_Click(object? sender, RoutedEventArgs e)
    {
        await EditSelectedContactAsync();
    }

    private async void ContactRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ContactItemViewModel item } && DataContext is ContactMainWindowViewModel viewModel)
        {
            viewModel.SelectedContact = item;
        }

        await EditSelectedContactAsync();
    }

    private async void NewCompany_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ContactMainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedCompany = null;
        var draft = CompanyItemViewModel.CreateNew();
        var dialog = new CompanyEditorWindow(draft);
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        await viewModel.SaveCompanyDraftAsync(draft);
    }

    private async void EditCompany_Click(object? sender, RoutedEventArgs e)
    {
        await EditSelectedCompanyAsync();
    }

    private async void CompanyRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: CompanyItemViewModel item } && DataContext is ContactMainWindowViewModel viewModel)
        {
            viewModel.SelectedCompany = item;
        }

        await EditSelectedCompanyAsync();
    }

    private async void NewProject_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ContactMainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedProject = null;
        var draft = ProjectItemViewModel.CreateNew();
        var dialog = new ProjectEditorWindow(draft, viewModel.CompanyOptions.ToList(), viewModel.ContactOptions.ToList());
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        await viewModel.SaveProjectDraftAsync(draft);
    }

    private async void EditProject_Click(object? sender, RoutedEventArgs e)
    {
        await EditSelectedProjectAsync();
    }

    private async void ProjectRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ProjectItemViewModel item } && DataContext is ContactMainWindowViewModel viewModel)
        {
            viewModel.SelectedProject = item;
        }

        await EditSelectedProjectAsync();
    }

    private async Task EditSelectedContactAsync()
    {
        if (DataContext is not ContactMainWindowViewModel viewModel || viewModel.SelectedContact is null)
        {
            return;
        }

        var draft = ContactItemViewModel.FromModel(viewModel.SelectedContact.ToModel());
        var dialog = new ContactEditorWindow(draft, viewModel.RelationshipTypeOptions, viewModel.CompanyOptions.ToList(), viewModel.ProjectOptions.ToList());
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        await viewModel.SaveContactDraftAsync(draft);
    }

    private async Task EditSelectedCompanyAsync()
    {
        if (DataContext is not ContactMainWindowViewModel viewModel || viewModel.SelectedCompany is null)
        {
            return;
        }

        var draft = CompanyItemViewModel.FromModel(viewModel.SelectedCompany.ToModel());
        var dialog = new CompanyEditorWindow(draft);
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        await viewModel.SaveCompanyDraftAsync(draft);
    }

    private async Task EditSelectedProjectAsync()
    {
        if (DataContext is not ContactMainWindowViewModel viewModel || viewModel.SelectedProject is null)
        {
            return;
        }

        var draft = ProjectItemViewModel.FromModel(viewModel.SelectedProject.ToModel());
        var dialog = new ProjectEditorWindow(draft, viewModel.CompanyOptions.ToList(), viewModel.ContactOptions.ToList());
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        await viewModel.SaveProjectDraftAsync(draft);
    }

}
