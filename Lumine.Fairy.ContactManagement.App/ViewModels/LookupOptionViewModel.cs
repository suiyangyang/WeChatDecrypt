using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumine.Fairy.ContactManagement.App.ViewModels;

public sealed partial class LookupOptionViewModel : ObservableObject
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
