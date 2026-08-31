using Avalonia.Controls;
using Avalonia.Interactivity;
using Lumine.Fairy.ContactManagement.App.ViewModels;
using Lumine.Fairy.ContactManagement.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Lumine.Fairy.ContactManagement.App.Dialogs;

public partial class ContactEditorWindow : Window
{
    public ContactEditorWindow()
        : this(ContactItemViewModel.CreateNew(), Enum.GetValues<ContactRelationshipType>(), [], [])
    {
    }

    public ContactEditorWindow(
        ContactItemViewModel item,
        IReadOnlyList<ContactRelationshipType> relationshipTypeOptions,
        IReadOnlyList<LookupOptionViewModel> companyOptions,
        IReadOnlyList<LookupOptionViewModel> projectOptions)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        RelationshipTypeOptions = relationshipTypeOptions ?? throw new ArgumentNullException(nameof(relationshipTypeOptions));
        InitializeOptions(CompanyOptions, companyOptions, Item.GetCompanyIds(), OnCompanyOptionPropertyChanged);
        InitializeOptions(ProjectOptions, projectOptions, Item.GetProjectIds(), OnProjectOptionPropertyChanged);
        RefreshCompanyOptions();
        RefreshProjectOptions();
        InitializeComponent();
        DataContext = this;
    }

    public ContactItemViewModel Item { get; }

    public IReadOnlyList<ContactRelationshipType> RelationshipTypeOptions { get; }

    public ObservableCollection<LookupOptionViewModel> CompanyOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> FilteredCompanyOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> SelectedCompanyOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> ProjectOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> FilteredProjectOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> SelectedProjectOptions { get; } = [];

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        Item.SetAffiliations(
            CompanyOptions.Where(static option => option.IsSelected).Select(static option => option.Id),
            ProjectOptions.Where(static option => option.IsSelected).Select(static option => option.Id));
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

    private void ProjectFilterTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshProjectOptions((sender as TextBox)?.Text);
    }

    private void RemoveCompanyTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: LookupOptionViewModel option })
        {
            option.IsSelected = false;
        }
    }

    private void RemoveProjectTag_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: LookupOptionViewModel option })
        {
            option.IsSelected = false;
        }
    }

    private void OnCompanyOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LookupOptionViewModel.IsSelected))
        {
            RefreshSelectedOptions(CompanyOptions, SelectedCompanyOptions);
        }
    }

    private void OnProjectOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LookupOptionViewModel.IsSelected))
        {
            RefreshSelectedOptions(ProjectOptions, SelectedProjectOptions);
        }
    }

    private void RefreshCompanyOptions(string? keyword = null)
    {
        RefreshFilteredOptions(CompanyOptions, FilteredCompanyOptions, keyword);
        RefreshSelectedOptions(CompanyOptions, SelectedCompanyOptions);
    }

    private void RefreshProjectOptions(string? keyword = null)
    {
        RefreshFilteredOptions(ProjectOptions, FilteredProjectOptions, keyword);
        RefreshSelectedOptions(ProjectOptions, SelectedProjectOptions);
    }

    private static void InitializeOptions(
        ObservableCollection<LookupOptionViewModel> target,
        IReadOnlyList<LookupOptionViewModel> source,
        IReadOnlyList<string> selectedIds,
        PropertyChangedEventHandler propertyChangedHandler)
    {
        var selectedIdSet = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in source.Select(item => new LookupOptionViewModel
                 {
                     Id = item.Id,
                     DisplayName = item.DisplayName,
                     IsSelected = selectedIdSet.Contains(item.Id)
                 }))
        {
            item.PropertyChanged += propertyChangedHandler;
            target.Add(item);
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
            items = items.Where(item => CompanyOptionSearch.Matches(item.DisplayName, keyword));
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
