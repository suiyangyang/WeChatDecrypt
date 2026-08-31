using Avalonia;
using Avalonia.Threading;
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeText))]
    private bool _isGroupChat = true;
    [ObservableProperty] private DateTimeOffset? _lastMessageAt;
    [ObservableProperty] private string? _companyId;
    [ObservableProperty] private string? _projectId;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private LookupOptionViewModel? _selectedCompanyOption;
    [ObservableProperty] private string _companyFilterText = string.Empty;
    private bool _companyOptionsRefreshScheduled;
    private string? _savedCompanyId;

    public ObservableCollection<LookupOptionViewModel> CompanyOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> FilteredCompanyOptions { get; } = [];

    public string LastMessageAtText => LastMessageAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "未知";
    public string CompanyName => SelectedCompanyOption?.DisplayName
        ?? CompanyOptions.FirstOrDefault(option => string.Equals(option.Id, CompanyId, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? "未分配";
    public string TypeText => IsGroupChat ? "群聊" : "联系人";
    public bool HasUnsavedChanges => !string.Equals(CompanyId, _savedCompanyId, StringComparison.OrdinalIgnoreCase);

    public static WeChatGroupChatItemViewModel FromModel(WeChatGroupChatProfile model, IEnumerable<LookupOptionViewModel> companyOptions)
    {
        var item = new WeChatGroupChatItemViewModel
        {
            Id = model.Id,
            AccountId = model.AccountId,
            AccountDisplayName = model.AccountDisplayName,
            WeChatUsername = model.WeChatUsername,
            DisplayName = model.DisplayName,
            IsGroupChat = model.IsGroupChat,
            LastMessageAt = model.LastMessageAt,
            CompanyId = model.CompanyId,
            ProjectId = model.ProjectId,
            IsActive = model.IsActive
        };
        item.SetCompanyOptions(companyOptions);
        item.MarkAsSaved();
        return item;
    }

    public void MarkAsSaved()
    {
        _savedCompanyId = CompanyId;
        OnPropertyChanged(nameof(HasUnsavedChanges));
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

        var selectedOption = CompanyOptions.FirstOrDefault(option => string.Equals(option.Id, currentId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                             ?? CompanyOptions[0];
        CompanyFilterText = selectedOption.DisplayName;
        RefreshFilteredCompanyOptions(selectedOption);
        SelectedCompanyOption = selectedOption;
    }

    public WeChatGroupChatProfile ToModel() => new()
    {
        Id = Id,
        AccountId = AccountId,
        AccountDisplayName = AccountDisplayName.Trim(),
        WeChatUsername = WeChatUsername.Trim(),
        DisplayName = DisplayName.Trim(),
        IsGroupChat = IsGroupChat,
        LastMessageAt = LastMessageAt,
        // SelectedCompanyOption is the value edited by the row ComboBox. Keep the
        // persisted value in sync even if the binding has not pushed CompanyId yet.
        CompanyId = SelectedCompanyOption is null
            ? (string.IsNullOrWhiteSpace(CompanyId) ? null : CompanyId)
            : string.IsNullOrWhiteSpace(SelectedCompanyOption.Id) ? null : SelectedCompanyOption.Id,
        ProjectId = string.IsNullOrWhiteSpace(ProjectId) ? null : ProjectId,
        IsActive = IsActive,
        LastSyncedAt = DateTimeOffset.UtcNow
    };

    partial void OnSelectedCompanyOptionChanged(LookupOptionViewModel? value)
    {
        // 编辑搜索文字时 ComboBox 会暂时取消选择，不应因此清除公司归属。
        if (value is not null)
        {
            CompanyId = string.IsNullOrWhiteSpace(value.Id) ? null : value.Id;
        }
        OnPropertyChanged(nameof(CompanyName));
    }

    partial void OnCompanyIdChanged(string? value)
    {
        OnPropertyChanged(nameof(CompanyName));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    partial void OnLastMessageAtChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(LastMessageAtText));
    }

    partial void OnCompanyFilterTextChanged(string value)
    {
        RequestRefreshFilteredCompanyOptions();
    }

    private void RequestRefreshFilteredCompanyOptions()
    {
        if (global::Avalonia.Application.Current is null)
        {
            RefreshFilteredCompanyOptions();
            return;
        }

        if (_companyOptionsRefreshScheduled)
        {
            return;
        }

        _companyOptionsRefreshScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _companyOptionsRefreshScheduled = false;
            RefreshFilteredCompanyOptions();
        }, DispatcherPriority.Background);
    }

    private void RefreshFilteredCompanyOptions(LookupOptionViewModel? selectedOption = null)
    {
        selectedOption ??= SelectedCompanyOption;
        var searchText = string.Equals(
            CompanyFilterText.Trim(),
            selectedOption?.DisplayName.Trim(),
            StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : CompanyFilterText;
        var items = CompanyOptions.Where(option => option == selectedOption
            || CompanyOptionSearch.Matches(option.DisplayName, searchText)).ToList();

        // 保留仍然有效的候选对象，避免 Reset 让 ComboBox 清空选中项和搜索文字。
        for (var index = FilteredCompanyOptions.Count - 1; index >= 0; index--)
        {
            if (!items.Contains(FilteredCompanyOptions[index]))
            {
                FilteredCompanyOptions.RemoveAt(index);
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (index >= FilteredCompanyOptions.Count || FilteredCompanyOptions[index] != items[index])
            {
                FilteredCompanyOptions.Insert(index, items[index]);
            }
        }
    }
}
