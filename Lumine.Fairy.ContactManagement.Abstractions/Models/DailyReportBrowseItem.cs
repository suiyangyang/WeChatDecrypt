namespace Lumine.Fairy.ContactManagement.Models;

public sealed class DailyReportBrowseItem
{
    public DateOnly Date { get; set; }

    public decimal TotalHours { get; set; }

    public string PreviewText { get; set; } = string.Empty;

    public List<string> ProjectNames { get; set; } = [];
}