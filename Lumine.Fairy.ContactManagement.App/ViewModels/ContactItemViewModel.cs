using CommunityToolkit.Mvvm.ComponentModel;
using Lumine.Fairy.ContactManagement.App.Helpers;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.ViewModels;

public sealed partial class ContactItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _nicknamesText = string.Empty;

    [ObservableProperty]
    private string _weChatIdsText = string.Empty;

    [ObservableProperty]
    private string _phonesText = string.Empty;

    [ObservableProperty]
    private string _emailsText = string.Empty;

    [ObservableProperty]
    private string _companyIdsText = string.Empty;

    [ObservableProperty]
    private string _projectIdsText = string.Empty;

    [ObservableProperty]
    private ContactRelationshipType _relationshipType = ContactRelationshipType.Stranger;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _isActive = true;

    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? "新联系人" : FullName;

    public string ActiveStatusText => IsActive ? "启用" : "停用";

    public string RelationshipTypeText => ContactManagementDisplayText.GetContactRelationshipTypeText(RelationshipType);

    public string AffiliationSummaryText
    {
        get
        {
            var companyCount = DelimitedTextFieldHelper.Split(CompanyIdsText).Count;
            var projectCount = DelimitedTextFieldHelper.Split(ProjectIdsText).Count;
            return companyCount == 0 && projectCount == 0
                ? "未关联公司 / 项目"
                : $"公司 {companyCount} / 项目 {projectCount}";
        }
    }

    public string SecondaryText
    {
        get
        {
            var phones = DelimitedTextFieldHelper.Split(PhonesText);
            var emails = DelimitedTextFieldHelper.Split(EmailsText);
            return phones.FirstOrDefault() ?? emails.FirstOrDefault() ?? RelationshipTypeText;
        }
    }

    public static ContactItemViewModel CreateNew() => new();

    public static ContactItemViewModel FromModel(ContactProfile model)
    {
        return new ContactItemViewModel
        {
            Id = model.Id,
            FullName = model.FullName,
            NicknamesText = DelimitedTextFieldHelper.Join(model.Nicknames),
            WeChatIdsText = DelimitedTextFieldHelper.Join(model.WeChatIds),
            PhonesText = DelimitedTextFieldHelper.Join(model.Phones),
            EmailsText = DelimitedTextFieldHelper.Join(model.Emails),
            CompanyIdsText = DelimitedTextFieldHelper.Join(model.CompanyIds),
            ProjectIdsText = DelimitedTextFieldHelper.Join(model.ProjectIds),
            RelationshipType = model.RelationshipType,
            Notes = model.Notes,
            IsActive = model.IsActive
        };
    }

    public ContactProfile ToModel()
    {
        return new ContactProfile
        {
            Id = Id,
            FullName = FullName.Trim(),
            Nicknames = DelimitedTextFieldHelper.Split(NicknamesText),
            WeChatIds = DelimitedTextFieldHelper.Split(WeChatIdsText),
            Phones = DelimitedTextFieldHelper.Split(PhonesText),
            Emails = DelimitedTextFieldHelper.Split(EmailsText),
            CompanyIds = DelimitedTextFieldHelper.Split(CompanyIdsText),
            ProjectIds = DelimitedTextFieldHelper.Split(ProjectIdsText),
            RelationshipType = RelationshipType,
            Notes = Notes.Trim(),
            IsActive = IsActive
        };
    }

    public IReadOnlyList<string> GetCompanyIds() => DelimitedTextFieldHelper.Split(CompanyIdsText);

    public IReadOnlyList<string> GetProjectIds() => DelimitedTextFieldHelper.Split(ProjectIdsText);

    public void SetAffiliations(IEnumerable<string> companyIds, IEnumerable<string> projectIds)
    {
        CompanyIdsText = DelimitedTextFieldHelper.Join(companyIds);
        ProjectIdsText = DelimitedTextFieldHelper.Join(projectIds);
    }

    partial void OnFullNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
    }

    partial void OnPhonesTextChanged(string value)
    {
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnEmailsTextChanged(string value)
    {
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnCompanyIdsTextChanged(string value)
    {
        OnPropertyChanged(nameof(AffiliationSummaryText));
    }

    partial void OnProjectIdsTextChanged(string value)
    {
        OnPropertyChanged(nameof(AffiliationSummaryText));
    }

    partial void OnRelationshipTypeChanged(ContactRelationshipType value)
    {
        OnPropertyChanged(nameof(RelationshipTypeText));
        OnPropertyChanged(nameof(SecondaryText));
    }

    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ActiveStatusText));
    }
}
