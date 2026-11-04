using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumine.Fairy.ContactManagement.App.ViewModels;
using Lumine.Fairy.Desktop.Services;
using Lumine.Fairy.Protocol;
using System.Collections.ObjectModel;
using System.Text;

namespace Lumine.Fairy.Desktop.ViewModels;

public sealed partial class TodoEditWindowViewModel : ObservableObject
{
    public const int MaxLabelCount = 10;
    public const int MaxLabelNameLength = 30;

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
        ColorOptions = TodoLabelColors.CommonColors
            .Select(option => new TodoLabelColorOptionViewModel(option, false, selected => SelectLabelColor(selected.Color)))
            .ToList();
        SelectedLabel = Labels.FirstOrDefault();
        foreach (var item in todo.Checklist ?? Array.Empty<TodoChecklistItem>())
            ChecklistItems.Add(new TodoChecklistItemViewModel(item, RemoveChecklistItem));
        foreach (var comment in todo.Comments ?? Array.Empty<TodoComment>())
            Comments.Add(comment);
        foreach (var reference in todo.FileReferences ?? Array.Empty<TodoFileReference>())
            FileReferences.Add(new TodoFileReferenceViewModel(reference, RemoveFileReference));
        ChecklistItems.CollectionChanged += (_, _) => CopyChecklistAsTextCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _priority = "medium";
    [ObservableProperty] private string _assignee = string.Empty;
    [ObservableProperty] private DateTimeOffset? _dueDate;
    [ObservableProperty] private TodoListOption? _selectedList;
    [ObservableProperty] private TodoCompanyOption? _selectedCompany;
    [ObservableProperty] private TodoLabelEditorViewModel? _selectedLabel;
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
    public ObservableCollection<TodoFileReferenceViewModel> FileReferences { get; } = [];
    public ObservableCollection<TodoCompanyOption> FilteredCompanyOptions { get; } = [];
    public IReadOnlyList<TodoLabelColorOptionViewModel> ColorOptions { get; }
    public bool HasLabels => Labels.Count > 0;
    public bool CanAddLabel => Labels.Count < MaxLabelCount;
    public string LabelLimitText => $"{Labels.Count}/{MaxLabelCount} 个标签，每个最多 {MaxLabelNameLength} 个字符";
    public bool HasChecklist => ChecklistItems.Count > 0;
    public bool CanCopyChecklist => ChecklistItems.Count > 0;
    public bool HasComments => Comments.Count > 0;
    public bool HasFileReferences => FileReferences.Count > 0;

    partial void OnStatusTextChanged(string value)
        => OnPropertyChanged(nameof(IsStatusError));

    partial void OnSelectedLabelChanged(TodoLabelEditorViewModel? value)
    {
        foreach (var option in ColorOptions)
            option.SetSelected(value is not null && string.Equals(option.Color, value.Color, StringComparison.OrdinalIgnoreCase));
    }

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
                    .Take(MaxLabelCount)
                    .Select(label => new TodoLabel(label.Id, label.Name.Trim(), label.Color))
                    .ToList(),
                Checklist = ChecklistItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                    .Select(item => new TodoChecklistItem(item.Id, item.Text.Trim(), item.IsCompleted))
                    .ToList(),
                Comments = Comments.ToList(),
                FileReferences = FileReferences
                    .Select(reference => new TodoFileReference(reference.Path, reference.IsDirectory))
                    .ToList(),
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
        var name = NewLabelName.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Labels.Count >= MaxLabelCount)
        {
            StatusText = $"标签最多添加 {MaxLabelCount} 个。";
            return;
        }

        if (name.Length > MaxLabelNameLength)
        {
            StatusText = $"标签名称最多 {MaxLabelNameLength} 个字符。";
            return;
        }

        var label = new TodoLabelEditorViewModel(
            new TodoLabel($"label:{Guid.NewGuid():N}", NewLabelName.Trim(), TodoLabelColors.DefaultColor),
            RemoveLabel);
        Labels.Add(label);
        SelectedLabel = label;
        NewLabelName = string.Empty;
        OnPropertyChanged(nameof(HasLabels));
        OnPropertyChanged(nameof(CanAddLabel));
        OnPropertyChanged(nameof(LabelLimitText));
    }

    private void RemoveLabel(TodoLabelEditorViewModel label)
    {
        if (SelectedLabel == label)
        {
            var index = Labels.IndexOf(label);
            SelectedLabel = Labels.ElementAtOrDefault(index + 1)
                            ?? Labels.ElementAtOrDefault(index - 1);
        }

        Labels.Remove(label);
        OnPropertyChanged(nameof(HasLabels));
        OnPropertyChanged(nameof(CanAddLabel));
        OnPropertyChanged(nameof(LabelLimitText));
    }

    private void SelectLabelColor(string color)
        => SelectedLabel?.SetColor(color);

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

    // 将子任务清单以"子任务清单：\n1.xxx\n2.xxx"格式复制到剪贴板，
    // 供聊天/工单等场景直接粘贴使用。空白行会被跳过。
    [RelayCommand(CanExecute = nameof(CanCopyChecklist))]
    private async Task CopyChecklistAsTextAsync()
    {
        var text = BuildChecklistAsText();
        var ok = await AppClipboard.TrySetTextAsync(text);
        StatusText = ok
            ? $"已复制 {ChecklistItems.Count} 个子任务到剪贴板。"
            : "复制失败：剪贴板不可用。";
    }

    private string BuildChecklistAsText()
    {
        var sb = new StringBuilder();
        sb.Append("子任务清单：");
        var index = 0;
        foreach (var item in ChecklistItems)
        {
            var text = item.Text.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            sb.Append('\n').Append(++index).Append('.').Append(text);
        }
        return sb.ToString();
    }

    public void AddFileReferences(IEnumerable<TodoFileReference> references)
    {
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Path)
                || FileReferences.Any(existing => string.Equals(existing.Path, reference.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            FileReferences.Add(new TodoFileReferenceViewModel(reference, RemoveFileReference));
        }

        OnPropertyChanged(nameof(HasFileReferences));
    }

    private void RemoveFileReference(TodoFileReferenceViewModel reference)
    {
        FileReferences.Remove(reference);
        OnPropertyChanged(nameof(HasFileReferences));
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

public sealed record TodoCompanyOption(string? Id, string Name)
{
    // 可编辑 ComboBox 选中项显示依赖 ToString()（SelectionBoxItemTemplate 对可编辑模式无效），
    // 不重写会直接显示记录的调试文本，如 "TodoCompanyOption { Id = ..., Name = ... }"。
    public override string ToString() => Name;
}

public sealed class TodoLabelEditorViewModel : ObservableObject
{
    private readonly Action<TodoLabelEditorViewModel> _remove;
    private string _name;
    private string _color;

    public TodoLabelEditorViewModel(TodoLabel label, Action<TodoLabelEditorViewModel> remove)
    {
        Id = label.Id;
        _name = LimitName(label.Name);
        _color = TodoLabelColors.Normalize(label.Color);
        _remove = remove;
        RemoveCommand = new RelayCommand(() => _remove(this));
    }

    public string Id { get; }
    public string Color => _color;
    public SolidColorBrush BackgroundBrush => TodoLabelColors.CreateBrush(_color);
    public SolidColorBrush ForegroundBrush { get; } = TodoLabelColors.CreateBrush("#FFFFFF");
    public RelayCommand RemoveCommand { get; }
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, LimitName(value));
    }

    internal void SetColor(string color)
    {
        var normalized = TodoLabelColors.Normalize(color);
        if (string.Equals(_color, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        _color = normalized;
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(BackgroundBrush));
    }

    private static string LimitName(string? value)
    {
        var name = value ?? string.Empty;
        return name.Length <= TodoEditWindowViewModel.MaxLabelNameLength
            ? name
            : name[..TodoEditWindowViewModel.MaxLabelNameLength];
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
        ? TodoLabelColors.CreateBrush("#000000")
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
