using CommunityToolkit.Mvvm.ComponentModel;
using Lumine.Fairy.ContactManagement.App.Helpers;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.ViewModels;

public sealed partial class RelationshipLinkItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _sourceEntityId = string.Empty;

    [ObservableProperty]
    private EntityKind _sourceEntityKind = EntityKind.Contact;

    [ObservableProperty]
    private string _targetEntityId = string.Empty;

    [ObservableProperty]
    private EntityKind _targetEntityKind = EntityKind.Company;

    [ObservableProperty]
    private RelationshipLinkType _linkType = RelationshipLinkType.WorksAt;

    [ObservableProperty]
    private string _departmentName = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isPrimary;

    [ObservableProperty]
    private string _notes = string.Empty;

    public string LinkTypeText => ContactManagementDisplayText.GetRelationshipLinkTypeText(LinkType);

    public string SourceEntityKindText => ContactManagementDisplayText.GetEntityKindText(SourceEntityKind);

    public string TargetEntityKindText => ContactManagementDisplayText.GetEntityKindText(TargetEntityKind);

    public string DisplayName => $"{LinkTypeText}: {SourceEntityKindText} → {TargetEntityKindText}";

    public string SecondaryText => string.IsNullOrWhiteSpace(Title) ? DepartmentName : Title;

    public string PrimaryStatusText => IsPrimary ? "主关系" : "普通";

    public static RelationshipLinkItemViewModel CreateNew() => new();

    public static RelationshipLinkItemViewModel FromModel(RelationshipLink model)
    {
        return new RelationshipLinkItemViewModel
        {
            Id = model.Id,
            SourceEntityId = model.SourceEntityId,
            SourceEntityKind = model.SourceEntityKind,
            TargetEntityId = model.TargetEntityId,
            TargetEntityKind = model.TargetEntityKind,
            LinkType = model.LinkType,
            DepartmentName = model.DepartmentName,
            Title = model.Title,
            IsPrimary = model.IsPrimary,
            Notes = model.Notes
        };
    }

    public RelationshipLink ToModel()
    {
        return new RelationshipLink
        {
            Id = Id,
            SourceEntityId = SourceEntityId,
            SourceEntityKind = SourceEntityKind,
            TargetEntityId = TargetEntityId,
            TargetEntityKind = TargetEntityKind,
            LinkType = LinkType,
            DepartmentName = DepartmentName.Trim(),
            Title = Title.Trim(),
            IsPrimary = IsPrimary,
            Notes = Notes.Trim()
        };
    }

    partial void OnSourceEntityKindChanged(EntityKind value)
    {
        OnPropertyChanged(nameof(SourceEntityKindText));
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnTargetEntityKindChanged(EntityKind value)
    {
        OnPropertyChanged(nameof(TargetEntityKindText));
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnLinkTypeChanged(RelationshipLinkType value)
    {
        OnPropertyChanged(nameof(LinkTypeText));
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnDepartmentNameChanged(string value)
    {
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnTitleChanged(string value)
    {
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnIsPrimaryChanged(bool value)
    {
        OnPropertyChanged(nameof(PrimaryStatusText));
    }
}
