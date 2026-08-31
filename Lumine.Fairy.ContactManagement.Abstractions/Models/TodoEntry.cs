namespace Lumine.Fairy.ContactManagement.Models;

public sealed class TodoEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateOnly? WorkDate { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public TodoStatus Status { get; set; } = TodoStatus.NotStarted;

    public TodoPriority Priority { get; set; } = TodoPriority.Normal;

    public DateOnly? DueDate { get; set; }

    public decimal? WorkHours { get; set; }

    public bool IsDaily { get; set; }

    public bool IsFromScratchNote { get; set; }

    public string SourceText { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string Notes { get; set; } = string.Empty;

    public List<string> Labels { get; set; } = [];

    public List<TodoChecklistItem> Checklist { get; set; } = [];

    public bool IsArchived { get; set; }

    public string RepeatRule { get; set; } = "不重复";

    public int? ReminderMinutesBeforeDue { get; set; }
}
