namespace Lumine.Fairy.ContactManagement.Models;

public sealed class DailyReportBrowseResult
{
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public IReadOnlyList<DailyReportBrowseItem> Reports { get; init; } = [];
}