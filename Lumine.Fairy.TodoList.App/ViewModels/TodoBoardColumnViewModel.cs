using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.TodoList.App.ViewModels;

public sealed partial class TodoBoardColumnViewModel : ObservableObject
{
    public TodoBoardColumnViewModel(TodoStatus status, string title)
    {
        Status = status;
        Title = title;
    }

    public TodoStatus Status { get; }

    public string Title { get; }

    public ObservableCollection<TodoEntryItemViewModel> Items { get; } = [];

    public int Count => Items.Count;

    public string CountText => Count.ToString();

    public void RefreshCount()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountText));
    }
}
