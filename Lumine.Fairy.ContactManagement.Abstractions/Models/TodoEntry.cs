namespace Lumine.Fairy.ContactManagement.Models;

public sealed class TodoEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateOnly? WorkDate { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public TodoStatus Status { get; set; } = TodoStatus.NotStarted;

    public decimal? WorkHours { get; set; }

    public bool IsDaily { get; set; }

    public bool IsFromScratchNote { get; set; }

    public string SourceText { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string Notes { get; set; } = string.Empty;
}