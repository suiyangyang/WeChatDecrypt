using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumine.Fairy.TodoList.App.ViewModels;

public sealed partial class UnclassifiedFragmentItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _text = string.Empty;
}
