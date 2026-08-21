using CommunityToolkit.Mvvm.ComponentModel;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.ViewModels;

public sealed partial class CompanyItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _addressesText = string.Empty;

    [ObservableProperty]
    private string _departmentsText = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _isActive = true;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "新公司" : Name;

    public string ActiveStatusText => IsActive ? "启用" : "停用";

    public string SecondaryText => DelimitedTextFieldHelper.Split(DepartmentsText).FirstOrDefault() ?? DelimitedTextFieldHelper.Split(AddressesText).FirstOrDefault() ?? "未配置";

    public static CompanyItemViewModel CreateNew() => new();

    public static CompanyItemViewModel FromModel(CompanyProfile model)
    {
        return new CompanyItemViewModel
        {
            Id = model.Id,
            Name = model.Name,
            AddressesText = DelimitedTextFieldHelper.Join(model.Addresses),
            DepartmentsText = DelimitedTextFieldHelper.Join(model.Departments),
            Notes = model.Notes,
            IsActive = model.IsActive
        };
    }

    public CompanyProfile ToModel()
    {
        return new CompanyProfile
        {
            Id = Id,
            Name = Name.Trim(),
            Addresses = DelimitedTextFieldHelper.Split(AddressesText),
            Departments = DelimitedTextFieldHelper.Split(DepartmentsText),
            Notes = Notes.Trim(),
            IsActive = IsActive
        };
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnAddressesTextChanged(string value)
    {
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnDepartmentsTextChanged(string value)
    {
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ActiveStatusText));
    }
}
