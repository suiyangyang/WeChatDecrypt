using Avalonia.Controls;
using Avalonia.Interactivity;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.TodoList.App.ViewModels;

namespace Lumine.Fairy.TodoList.App;

public partial class TodoDetailWindow : Window
{
    public TodoDetailWindow()
    {
        InitializeComponent();
    }

    private void AddChecklistItem_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TodoEntryItemViewModel item || ChecklistInput.Text is not { Length: > 0 } text)
        {
            return;
        }

        item.Checklist.Add(new TodoChecklistItem { Text = text.Trim() });
        ChecklistInput.Text = string.Empty;
        item.RefreshChecklistState();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ChecklistItemChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TodoEntryItemViewModel item)
        {
            item.RefreshChecklistState();
        }
    }
}
