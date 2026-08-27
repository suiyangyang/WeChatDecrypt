using CommunityToolkit.Mvvm.ComponentModel;
using Lumine.Fairy.ContactManagement.Models;
using System.Collections.ObjectModel;

namespace Lumine.Fairy.ContactManagement.App.ViewModels;

public sealed partial class WeChatGroupChatItemViewModel : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _accountId = string.Empty;
    [ObservableProperty] private string _accountDisplayName = string.Empty;
    [ObservableProperty] private string _weChatUsername = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private DateTimeOffset? _lastMessageAt;
    [ObservableProperty] private string? _companyId;
    [ObservableProperty] private string? _projectId;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private LookupOptionViewModel? _selectedCompanyOption;

    public ObservableCollection<LookupOptionViewModel> CompanyOptions { get; } = [];

    public string LastMessageAtText => LastMessageAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "未知";
    public string CompanyName => SelectedCompanyOption?.DisplayName ?? "未分配";
    public string IdentityText => string.IsNullOrWhiteSpace(AccountDisplayName)
        ? WeChatUsername
        : $"{AccountDisplayName} · {WeChatUsername}";

    public static WeChatGroupChatItemViewModel FromModel(WeChatGroupChatProfile model, IEnumerable<LookupOptionViewModel> companyOptions)
    {
        var item = new WeChatGroupChatItemViewModel
        {
            Id = model.Id,
            AccountId = model.AccountId,
            AccountDisplayName = model.AccountDisplayName,
            WeChatUsername = model.WeChatUsername,
            DisplayName = model.DisplayName,
            LastMessageAt = model.LastMessageAt,
            CompanyId = model.CompanyId,
            ProjectId = model.ProjectId,
            IsActive = model.IsActive
        };
        item.SetCompanyOptions(companyOptions);
        return item;
    }

    public void SetCompanyOptions(IEnumerable<LookupOptionViewModel> options)
    {
        var currentId = CompanyId;
        CompanyOptions.Clear();
        CompanyOptions.Add(new LookupOptionViewModel { Id = string.Empty, DisplayName = "未分配" });
        foreach (var option in options)
        {
            CompanyOptions.Add(new LookupOptionViewModel { Id = option.Id, DisplayName = option.DisplayName });
        }

        SelectedCompanyOption = CompanyOptions.FirstOrDefault(option => string.Equals(option.Id, currentId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                                ?? CompanyOptions[0];
    }

    public WeChatGroupChatProfile ToModel() => new()
    {
        Id = Id,
        AccountId = AccountId,
        AccountDisplayName = AccountDisplayName.Trim(),
        WeChatUsername = WeChatUsername.Trim(),
        DisplayName = DisplayName.Trim(),
        LastMessageAt = LastMessageAt,
        CompanyId = string.IsNullOrWhiteSpace(CompanyId) ? null : CompanyId,
        ProjectId = string.IsNullOrWhiteSpace(ProjectId) ? null : ProjectId,
        IsActive = IsActive,
        LastSyncedAt = DateTimeOffset.UtcNow
    };

    partial void OnSelectedCompanyOptionChanged(LookupOptionViewModel? value)
    {
        CompanyId = string.IsNullOrWhiteSpace(value?.Id) ? null : value.Id;
        OnPropertyChanged(nameof(CompanyName));
    }

    partial void OnCompanyIdChanged(string? value)
    {
        OnPropertyChanged(nameof(CompanyName));
    }

    partial void OnLastMessageAtChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(LastMessageAtText));
    }
}
