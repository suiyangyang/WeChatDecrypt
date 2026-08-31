using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumine.Fairy.ContactManagement.App.ViewModels;
using Lumine.Fairy.Protocol;
using System.Collections.ObjectModel;

namespace Lumine.Fairy.Desktop.ViewModels;

public sealed partial class TodoEditWindowViewModel : ObservableObject
{
    private readonly TodoRecord _original;
    private readonly Func<TodoRecord, Task<TodoRecord?>> _saveAsync;
    private readonly Action<TodoRecord?> _closeAction;
    private TodoCompanyOption? _companyForSave;
    private bool _companyOptionsRefreshScheduled;
    public bool IsNew { get; }
    public IReadOnlyList<TodoListOption> ListOptions { get; }
    public IReadOnlyList<TodoCompanyOption> CompanyOptions { get; }

    public TodoEditWindowViewModel(
        TodoRecord todo,
        Func<TodoRecord, Task<TodoRecord?>> saveAsync,
        bool isNew,
        IReadOnlyList<TodoListOption> listOptions,
        IReadOnlyList<TodoCompanyOption>? companyOptions,
        Action<TodoRecord?> closeAction)
    {
        _original = todo;
        _saveAsync = saveAsync;
        IsNew = isNew;
        ListOptions = listOptions;
        CompanyOptions = companyOptions ?? [new TodoCompanyOption(null, "未设置")];
        _closeAction = closeAction;
        Title = todo.Title;
        Summary = todo.Summary;
        Priority = ToDisplayPriority(todo.Priority);
        Assignee = todo.Assignee ?? string.Empty;
        DueDate = todo.DueAt?.ToLocalTime();
        SelectedList = ListOptions.FirstOrDefault(option => option.Id == todo.ListId)
                       ?? ListOptions.FirstOrDefault();
        SelectedCompany = CompanyOptions.FirstOrDefault(option => option.Id == todo.CompanyId)
                          ?? CompanyOptions.FirstOrDefault(option => option.Id is null);
        _companyForSave = SelectedCompany;
        CompanyFilterText = SelectedCompany?.Name ?? string.Empty;
        RefreshFilteredCompanyOptions();
        IsArchived = todo.IsArchived;
        foreach (var label in todo.Labels ?? Array.Empty<TodoLabel>())
            Labels.Add(new TodoLabelEditorViewModel(label, RemoveLabel));
        foreach (var item in todo.Checklist ?? Array.Empty<TodoChecklistItem>())
            ChecklistItems.Add(new TodoChecklistItemViewModel(item, RemoveChecklistItem));
        foreach (var comment in todo.Comments ?? Array.Empty<TodoComment>())
            Comments.Add(comment);
    }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _priority = "medium";
    [ObservableProperty] private string _assignee = string.Empty;
    [ObservableProperty] private DateTimeOffset? _dueDate;
    [ObservableProperty] private TodoListOption? _selectedList;
    [ObservableProperty] private TodoCompanyOption? _selectedCompany;
    [ObservableProperty] private string _companyFilterText = string.Empty;
    [ObservableProperty] private bool _isArchived;
    [ObservableProperty] private string _newLabelName = string.Empty;
    [ObservableProperty] private string _newChecklistText = string.Empty;
    [ObservableProperty] private string _newCommentText = string.Empty;
    [ObservableProperty] private string _statusText = "修改后点击保存。";
    [ObservableProperty] private bool _isBusy;

    public string SourceText => $"来源：{_original.Source}";
    public string DialogTitle => IsNew ? "新建 TODO" : "编辑 TODO";
    public string DialogSubtitle => IsNew ? "创建一个本地 TODO 卡片。" : "修改内容不会影响原始微信消息和通知记录。";
    public string ConversationText => string.IsNullOrWhiteSpace(_original.ConversationDisplayName)
        ? $"会话：{_original.ConversationId}"
        : $"会话：{_original.ConversationDisplayName}";
    public string CreatedAtText => $"创建于：{_original.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
    public bool IsStatusError => StatusText.Contains("失败", StringComparison.Ordinal)
        || StatusText.Contains("不能为空", StringComparison.Ordinal)
        || StatusText.Contains("不正确", StringComparison.Ordinal);
    public ObservableCollection<TodoLabelEditorViewModel> Labels { get; } = [];
    public ObservableCollection<TodoChecklistItemViewModel> ChecklistItems { get; } = [];
    public ObservableCollection<TodoComment> Comments { get; } = [];
    public ObservableCollection<TodoCompanyOption> FilteredCompanyOptions { get; } = [];
    public bool HasLabels => Labels.Count > 0;
    public bool HasChecklist => ChecklistItems.Count > 0;
    public bool HasComments => Comments.Count > 0;

    partial void OnStatusTextChanged(string value)
        => OnPropertyChanged(nameof(IsStatusError));

    partial void OnSelectedCompanyChanged(TodoCompanyOption? value)
    {
        if (value is not null)
        {
            _companyForSave = value;
            CompanyFilterText = value.Name;
        }

        RequestRefreshFilteredCompanyOptions();
    }

    partial void OnCompanyFilterTextChanged(string value)
        => RequestRefreshFilteredCompanyOptions();

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        if (!Validate()) return;

        try
        {
            IsBusy = true;
            var updated = _original with
            {
                Title = Title.Trim(),
                Summary = Summary.Trim(),
                Priority = Priority.Trim().ToLowerInvariant(),
                CompanyId = _companyForSave?.Id,
                CompanyName = _companyForSave?.Id is null ? null : _companyForSave.Name,
                Assignee = string.IsNullOrWhiteSpace(Assignee) ? null : Assignee.Trim(),
                DueAt = DueDate?.ToUniversalTime(),
                ListId = SelectedList?.Id ?? _original.ListId,
                Status = SelectedList?.Status ?? _original.Status,
                IsArchived = IsArchived,
                Labels = Labels
                    .Where(label => !string.IsNullOrWhiteSpace(label.Name))
                    .Select(label => new TodoLabel(label.Id, label.Name.Trim(), label.Color))
                    .ToList(),
                Checklist = ChecklistItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                    .Select(item => new TodoChecklistItem(item.Id, item.Text.Trim(), item.IsCompleted))
                    .ToList(),
                Comments = Comments.ToList(),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            if (!string.IsNullOrWhiteSpace(NewCommentText))
            {
                updated = updated with
                {
                    Comments = updated.Comments!.Concat(new[]
                    {
                        new TodoComment($"comment:{Guid.NewGuid():N}", "我", NewCommentText.Trim(), DateTimeOffset.UtcNow),
                    }).ToList(),
                };
            }
            var saved = await _saveAsync(updated);
            if (saved is null)
            {
                StatusText = "保存失败：TODO 可能已被删除。";
                return;
            }

            _closeAction(saved);
        }
        catch (Exception ex)
        {
            StatusText = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
        => _closeAction(null);

    [RelayCommand]
    private void AddLabel()
    {
        if (string.IsNullOrWhiteSpace(NewLabelName)) return;
        Labels.Add(new TodoLabelEditorViewModel(
            new TodoLabel($"label:{Guid.NewGuid():N}", NewLabelName.Trim(), TodoLabelColors.DefaultColor),
            RemoveLabel));
        NewLabelName = string.Empty;
        OnPropertyChanged(nameof(HasLabels));
    }

    private void RemoveLabel(TodoLabelEditorViewModel label)
    {
        Labels.Remove(label);
        OnPropertyChanged(nameof(HasLabels));
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

    private void RefreshFilteredCompanyOptions()
    {
        var selectedOption = SelectedCompany ?? _companyForSave;
        var searchText = string.Equals(
            CompanyFilterText.Trim(),
            selectedOption?.Name.Trim(),
            StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : CompanyFilterText;
        var items = CompanyOptions
            .Where(option => option == selectedOption
                || CompanyOptionSearch.Matches(option.Name, searchText))
            .ToList();

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
                if (FilteredCompanyOptions.Contains(items[index]))
                {
                    FilteredCompanyOptions.Move(FilteredCompanyOptions.IndexOf(items[index]), index);
                }
                else
                {
                    FilteredCompanyOptions.Insert(index, items[index]);
                }
            }
        }
    }

    [RelayCommand]
    private void AddChecklistItem()
    {
        if (string.IsNullOrWhiteSpace(NewChecklistText)) return;
        ChecklistItems.Add(new TodoChecklistItemViewModel(
            new TodoChecklistItem($"check:{Guid.NewGuid():N}", NewChecklistText.Trim(), false),
            RemoveChecklistItem));
        NewChecklistText = string.Empty;
        OnPropertyChanged(nameof(HasChecklist));
    }

    private void RemoveChecklistItem(TodoChecklistItemViewModel item)
    {
        ChecklistItems.Remove(item);
        OnPropertyChanged(nameof(HasChecklist));
    }

    [RelayCommand]
    private void AddComment()
    {
        if (string.IsNullOrWhiteSpace(NewCommentText)) return;
        Comments.Add(new TodoComment($"comment:{Guid.NewGuid():N}", "我", NewCommentText.Trim(), DateTimeOffset.UtcNow));
        NewCommentText = string.Empty;
        OnPropertyChanged(nameof(HasComments));
    }

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            StatusText = "标题不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Summary))
        {
            StatusText = "摘要不能为空。";
            return false;
        }

        if (!PriorityOptions.Contains(Priority, StringComparer.OrdinalIgnoreCase))
        {
            StatusText = "优先级不能为空。";
            return false;
        }

        return true;
    }

    public IReadOnlyList<string> PriorityOptions { get; } = ["High", "Medium", "Low"];

    private static string ToDisplayPriority(string priority)
        => priority.Trim().ToLowerInvariant() switch
        {
            "high" or "urgent" or "critical" or "高" => "High",
            "low" or "低" => "Low",
            _ => "Medium",
        };
}

public sealed record TodoListOption(string Id, string Name, string? Status = null);

public sealed record TodoCompanyOption(string? Id, string Name);

public sealed class TodoLabelEditorViewModel : ObservableObject
{
    private readonly Action<TodoLabelEditorViewModel> _remove;
    private readonly IReadOnlyList<TodoLabelColorOptionViewModel> _colorOptions;
    private string _name;
    private string _color;

    public TodoLabelEditorViewModel(TodoLabel label, Action<TodoLabelEditorViewModel> remove)
    {
        Id = label.Id;
        _name = label.Name;
        _color = TodoLabelColors.Normalize(label.Color);
        _remove = remove;
        RemoveCommand = new RelayCommand(() => _remove(this));
        _colorOptions = TodoLabelColors.CommonColors
            .Select(option => new TodoLabelColorOptionViewModel(
                option,
                string.Equals(option.Color, _color, StringComparison.OrdinalIgnoreCase),
                selected => SelectColor(selected.Color)))
            .ToList();
    }

    public string Id { get; }
    public string Color => _color;
    public SolidColorBrush BackgroundBrush => TodoLabelColors.CreateBrush(_color);
    public SolidColorBrush ForegroundBrush { get; } = TodoLabelColors.CreateBrush("#FFFFFF");
    public IReadOnlyList<TodoLabelColorOptionViewModel> ColorOptions => _colorOptions;
    public RelayCommand RemoveCommand { get; }
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private void SelectColor(string color)
    {
        var normalized = TodoLabelColors.Normalize(color);
        if (string.Equals(_color, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        _color = normalized;
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(BackgroundBrush));
        foreach (var option in _colorOptions)
            option.SetSelected(string.Equals(option.Color, _color, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class TodoLabelColorOptionViewModel : ObservableObject
{
    private readonly Action<TodoLabelColorOptionViewModel> _select;
    private bool _isSelected;

    public TodoLabelColorOptionViewModel(
        TodoLabelColorDefinition definition,
        bool isSelected,
        Action<TodoLabelColorOptionViewModel> select)
    {
        Name = definition.Name;
        Color = definition.Color;
        Brush = TodoLabelColors.CreateBrush(Color);
        _isSelected = isSelected;
        _select = select;
        SelectCommand = new RelayCommand(() => _select(this));
    }

    public string Name { get; }
    public string Color { get; }
    public SolidColorBrush Brush { get; }
    public RelayCommand SelectCommand { get; }
    public bool IsSelected
    {
        get => _isSelected;
        private set
        {
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(SelectionBorderBrush));
        }
    }

    public SolidColorBrush SelectionBorderBrush => IsSelected
        ? TodoLabelColors.CreateBrush("#FFFFFF")
        : TodoLabelColors.CreateBrush("#00000000");

    internal void SetSelected(bool isSelected)
        => IsSelected = isSelected;
}

public sealed class TodoChecklistItemViewModel : ObservableObject
{
    private readonly Action<TodoChecklistItemViewModel> _remove;
    private string _text;
    private bool _isCompleted;

    public TodoChecklistItemViewModel(TodoChecklistItem item, Action<TodoChecklistItemViewModel> remove)
    {
        Id = item.Id;
        _text = item.Text;
        _isCompleted = item.IsCompleted;
        _remove = remove;
        RemoveCommand = new RelayCommand(() => _remove(this));
    }

    public string Id { get; }
    public RelayCommand RemoveCommand { get; }
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }
}
