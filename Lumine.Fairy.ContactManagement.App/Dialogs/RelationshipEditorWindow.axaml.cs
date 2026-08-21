using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Lumine.Fairy.ContactManagement.App.ViewModels;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.Dialogs;

public partial class RelationshipEditorWindow : Window
{
    private readonly IReadOnlyList<LookupOptionViewModel> _contactOptions;
    private readonly IReadOnlyList<LookupOptionViewModel> _companyOptions;
    private readonly IReadOnlyList<LookupOptionViewModel> _projectOptions;

    public RelationshipEditorWindow()
        : this(RelationshipLinkItemViewModel.CreateNew(), Enum.GetValues<EntityKind>(), Enum.GetValues<RelationshipLinkType>(), [], [], [])
    {
    }

    public RelationshipEditorWindow(
        RelationshipLinkItemViewModel item,
        IReadOnlyList<EntityKind> entityKindOptions,
        IReadOnlyList<RelationshipLinkType> relationshipLinkTypeOptions,
        IReadOnlyList<LookupOptionViewModel> contactOptions,
        IReadOnlyList<LookupOptionViewModel> companyOptions,
        IReadOnlyList<LookupOptionViewModel> projectOptions)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        EntityKindOptions = entityKindOptions ?? throw new ArgumentNullException(nameof(entityKindOptions));
        RelationshipLinkTypeOptions = relationshipLinkTypeOptions ?? throw new ArgumentNullException(nameof(relationshipLinkTypeOptions));
        _contactOptions = CloneOptions(contactOptions);
        _companyOptions = CloneOptions(companyOptions);
        _projectOptions = CloneOptions(projectOptions);

        InitializeComponent();
        DataContext = this;
        RefreshEntityOptions();
        Item.PropertyChanged += OnItemPropertyChanged;
    }

    public RelationshipLinkItemViewModel Item { get; }

    public IReadOnlyList<EntityKind> EntityKindOptions { get; }

    public IReadOnlyList<RelationshipLinkType> RelationshipLinkTypeOptions { get; }

    public ObservableCollection<LookupOptionViewModel> SourceEntityOptions { get; } = [];

    public ObservableCollection<LookupOptionViewModel> TargetEntityOptions { get; } = [];

    protected override void OnClosed(EventArgs e)
    {
        Item.PropertyChanged -= OnItemPropertyChanged;
        base.OnClosed(e);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RelationshipLinkItemViewModel.SourceEntityKind)
            or nameof(RelationshipLinkItemViewModel.TargetEntityKind))
        {
            RefreshEntityOptions();
        }
    }

    private void RefreshEntityOptions()
    {
        ReplaceCollection(SourceEntityOptions, ResolveOptions(Item.SourceEntityKind));
        ReplaceCollection(TargetEntityOptions, ResolveOptions(Item.TargetEntityKind));

        if (!SourceEntityOptions.Any(static item => !string.IsNullOrWhiteSpace(item.Id)))
        {
            Item.SourceEntityId = string.Empty;
        }
        else if (!SourceEntityOptions.Any(item => string.Equals(item.Id, Item.SourceEntityId, StringComparison.OrdinalIgnoreCase)))
        {
            Item.SourceEntityId = SourceEntityOptions.First(static item => !string.IsNullOrWhiteSpace(item.Id)).Id;
        }

        if (!TargetEntityOptions.Any(static item => !string.IsNullOrWhiteSpace(item.Id)))
        {
            Item.TargetEntityId = string.Empty;
        }
        else if (!TargetEntityOptions.Any(item => string.Equals(item.Id, Item.TargetEntityId, StringComparison.OrdinalIgnoreCase)))
        {
            Item.TargetEntityId = TargetEntityOptions.First(static item => !string.IsNullOrWhiteSpace(item.Id)).Id;
        }
    }

    private IReadOnlyList<LookupOptionViewModel> ResolveOptions(EntityKind entityKind)
    {
        return entityKind switch
        {
            EntityKind.Contact => _contactOptions,
            EntityKind.Company => _companyOptions,
            EntityKind.Project => _projectOptions,
            _ => []
        };
    }

    private static IReadOnlyList<LookupOptionViewModel> CloneOptions(IReadOnlyList<LookupOptionViewModel> source)
    {
        return source.Select(static item => new LookupOptionViewModel
        {
            Id = item.Id,
            DisplayName = item.DisplayName
        }).ToList();
    }

    private static void ReplaceCollection(ObservableCollection<LookupOptionViewModel> target, IReadOnlyList<LookupOptionViewModel> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
