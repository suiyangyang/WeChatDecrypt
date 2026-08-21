using CommunityToolkit.Mvvm.ComponentModel;
using Lumine.Fairy.ContactManagement.App.Helpers;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.ViewModels;

public sealed partial class ProjectItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _companyId;

    [ObservableProperty]
    private string? _ownerContactId;

    [ObservableProperty]
    private DateTimeOffset? _deliveryDate;

    [ObservableProperty]
    private string _tagsText = string.Empty;

    [ObservableProperty]
    private string _groupChatsText = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _isActive = true;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "新项目" : Name;

    public string ActiveStatusText => IsActive ? "启用" : "停用";

    public string DeliveryDateText => DeliveryDate.HasValue
        ? DateOnly.FromDateTime(DeliveryDate.Value.DateTime).ToString("yyyy-MM-dd")
        : "长期项目";

    public string SecondaryText => DelimitedTextFieldHelper.Split(GroupChatsText).FirstOrDefault()
        ?? DelimitedTextFieldHelper.Split(TagsText).FirstOrDefault()
        ?? "未配置标签 / 群聊";

    public static ProjectItemViewModel CreateNew() => new();

    public static ProjectItemViewModel FromModel(ProjectProfile model)
    {
        return new ProjectItemViewModel
        {
            Id = model.Id,
            Name = model.Name,
            CompanyId = model.CompanyId,
            OwnerContactId = model.OwnerContactId,
            DeliveryDate = model.DeliveryDate.HasValue
                ? new DateTimeOffset(model.DeliveryDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : null,
            TagsText = DelimitedTextFieldHelper.Join(model.Tags),
            GroupChatsText = DelimitedTextFieldHelper.Join(model.GroupChats),
            Notes = model.Notes,
            IsActive = model.IsActive
        };
    }

    public ProjectProfile ToModel()
    {
        return new ProjectProfile
        {
            Id = Id,
            Name = Name.Trim(),
            CompanyId = string.IsNullOrWhiteSpace(CompanyId) ? null : CompanyId,
            OwnerContactId = string.IsNullOrWhiteSpace(OwnerContactId) ? null : OwnerContactId,
            DeliveryDate = DeliveryDate.HasValue ? DateOnly.FromDateTime(DeliveryDate.Value.Date) : null,
            Tags = DelimitedTextFieldHelper.Split(TagsText),
            GroupChats = DelimitedTextFieldHelper.Split(GroupChatsText),
            Notes = Notes.Trim(),
            IsActive = IsActive
        };
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnTagsTextChanged(string value)
    {
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnGroupChatsTextChanged(string value)
    {
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnDeliveryDateChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(DeliveryDateText));
    }

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ActiveStatusText));
    }
}
