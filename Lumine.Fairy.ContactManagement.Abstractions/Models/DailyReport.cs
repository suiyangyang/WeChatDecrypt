namespace Lumine.Fairy.ContactManagement.Models;

public sealed class DailyReport
{
    public DateOnly Date { get; set; }

    public List<TodoEntry> Items { get; set; } = [];

    public List<ClientIssueEntry> ClientIssues { get; set; } = [];

    public decimal TotalHours { get; set; }

    public string RawScratchText { get; set; } = string.Empty;

    public string FormattedReportText { get; set; } = string.Empty;
}