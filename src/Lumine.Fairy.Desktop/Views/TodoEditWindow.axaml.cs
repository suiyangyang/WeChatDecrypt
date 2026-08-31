using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lumine.Fairy.Desktop.ViewModels;
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
}
