using CommunityToolkit.Mvvm.ComponentModel;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.ViewModels;

public sealed partial class ImportConflictDecisionItemViewModel : ObservableObject
{
    public ImportConflictDecisionItemViewModel(ImportConflictItem conflict)
    {
        Conflict = conflict;
        SelectedAction = conflict.RecommendedAction;
    }

    public ImportConflictItem Conflict { get; }

    public string Message => Conflict.Message;

    public string Code => Conflict.Code;

    public string EntityType => Conflict.EntityType;

    public string EntityIdentifier => Conflict.EntityIdentifier;

    public ImportConflictResolutionAction RecommendedAction => Conflict.RecommendedAction;

    public IReadOnlyList<ImportConflictResolutionAction> AvailableActions { get; } =
    [
        ImportConflictResolutionAction.Skip,
        ImportConflictResolutionAction.Merge
    ];

    [ObservableProperty]
    private ImportConflictResolutionAction _selectedAction;

    public ImportConflictResolutionDecision ToDecision()
    {
        return new ImportConflictResolutionDecision
        {
            Code = Conflict.Code,
            EntityType = Conflict.EntityType,
            EntityIdentifier = Conflict.EntityIdentifier,
            Action = SelectedAction
        };
    }

    public string DecisionKey => $"{Conflict.Code}|{Conflict.EntityType}|{Conflict.EntityIdentifier}";
}
