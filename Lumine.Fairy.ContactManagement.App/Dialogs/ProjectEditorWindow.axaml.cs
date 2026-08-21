using Avalonia.Controls;
using Avalonia.Interactivity;
using Lumine.Fairy.ContactManagement.App.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Lumine.Fairy.ContactManagement.App.Dialogs;

public partial class ProjectEditorWindow : Window
{
    private bool _isUpdatingCompanySelection;
    private bool _isUpdatingContactSelection;

    public ProjectEditorWindow()
        : this(ProjectItemViewModel.CreateNew(), [], [])
    {
    }

    public ProjectEditorWindow(
        ProjectItemViewModel item,
        IReadOnlyList<LookupOptionViewModel> companyOptions,
        IReadOnlyList<LookupOptionViewModel> contactOptions)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        InitializeOptions(CompanyOptions, companyOptions, Item.CompanyId, OnCompanyOptionPropertyChanged);
        InitializeOptions(ContactOptions, contactOptions, Item.OwnerContactId, OnContactOptionPropertyChanged);
        RefreshCompanyOptions();
        RefreshContactOptions();
        InitializeComponent();
        DataContext = this;
    }

    public ProjectItemViewModel Item { get; }

    public ObservableCollection<LookupOptionViewModel> CompanyOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> FilteredCompanyOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> SelectedCompanyOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> ContactOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> FilteredContactOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> SelectedContactOptions { get; } = [];

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void CompanyFilterTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshCompanyOptions((sender as TextBox)?.Text);
    }

    private void ContactFilterTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshContactOptions((sender as TextBox)?.Text);
    }

    private void SelectCompanyOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: LookupOptionViewModel option })
        {
            option.IsSelected = true;
        }
    }

    private void SelectContactOption_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: LookupOptionViewModel option })
        {
            option.IsSelected = true;
        }
    }

    private void RemoveCompanyTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: LookupOptionViewModel option })
        {
            option.IsSelected = false;
        }
    }

    private void RemoveContactTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: LookupOptionViewModel option })
        {
            option.IsSelected = false;
        }
    }

    private void OnCompanyOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LookupOptionViewModel.IsSelected)
            || sender is not LookupOptionViewModel option
            || _isUpdatingCompanySelection)
        {
            return;
        }

        _isUpdatingCompanySelection = true;
        try
        {
            ApplySingleSelection(CompanyOptions, option);
            Item.CompanyId = CompanyOptions.FirstOrDefault(static item => item.IsSelected)?.Id;
            RefreshSelectedOptions(CompanyOptions, SelectedCompanyOptions);
        }
        finally
        {
            _isUpdatingCompanySelection = false;
        }
    }

    private void OnContactOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LookupOptionViewModel.IsSelected)
            || sender is not LookupOptionViewModel option
            || _isUpdatingContactSelection)
        {
            return;
        }

        _isUpdatingContactSelection = true;
        try
        {
            ApplySingleSelection(ContactOptions, option);
            Item.OwnerContactId = ContactOptions.FirstOrDefault(static item => item.IsSelected)?.Id;
            RefreshSelectedOptions(ContactOptions, SelectedContactOptions);
        }
        finally
        {
            _isUpdatingContactSelection = false;
        }
    }

    private void RefreshCompanyOptions(string? keyword = null)
    {
        RefreshFilteredOptions(CompanyOptions, FilteredCompanyOptions, keyword);
        RefreshSelectedOptions(CompanyOptions, SelectedCompanyOptions);
    }

    private void RefreshContactOptions(string? keyword = null)
    {
        RefreshFilteredOptions(ContactOptions, FilteredContactOptions, keyword);
        RefreshSelectedOptions(ContactOptions, SelectedContactOptions);
    }

    private static void InitializeOptions(
        ObservableCollection<LookupOptionViewModel> target,
        IReadOnlyList<LookupOptionViewModel> source,
        string? selectedId,
        PropertyChangedEventHandler propertyChangedHandler)
    {
        foreach (var item in source.Select(item => new LookupOptionViewModel
                 {
                     Id = item.Id,
                     DisplayName = item.DisplayName,
                     IsSelected = string.Equals(item.Id, selectedId, StringComparison.OrdinalIgnoreCase)
                 }))
        {
            item.PropertyChanged += propertyChangedHandler;
            target.Add(item);
        }
    }

    private static void ApplySingleSelection(
        IEnumerable<LookupOptionViewModel> source,
        LookupOptionViewModel changedOption)
    {
        if (changedOption.IsSelected)
        {
            foreach (var option in source.Where(option => !ReferenceEquals(option, changedOption) && option.IsSelected))
            {
                option.IsSelected = false;
            }

            return;
        }

        foreach (var option in source.Where(option => !ReferenceEquals(option, changedOption) && option.IsSelected))
        {
            option.IsSelected = false;
        }
    }

    private static void RefreshFilteredOptions(
        IEnumerable<LookupOptionViewModel> source,
        ObservableCollection<LookupOptionViewModel> target,
        string? keyword)
    {
        IEnumerable<LookupOptionViewModel> items = source;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var trimmedKeyword = keyword.Trim();
            items = items.Where(item => item.DisplayName.Contains(trimmedKeyword, StringComparison.OrdinalIgnoreCase));
        }

        ReplaceCollection(target, items.OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase));
    }

    private static void RefreshSelectedOptions(
        IEnumerable<LookupOptionViewModel> source,
        ObservableCollection<LookupOptionViewModel> target)
    {
        ReplaceCollection(target, source.Where(static item => item.IsSelected).OrderBy(static item => item.DisplayName, StringComparer.OrdinalIgnoreCase));
    }

    private static void ReplaceCollection<TItem>(ObservableCollection<TItem> target, IEnumerable<TItem> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
