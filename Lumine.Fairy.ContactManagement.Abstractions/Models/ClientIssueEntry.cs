namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ClientIssueEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateOnly? WorkDate { get; set; }

    public string ProjectName { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public List<string> IssueLines { get; set; } = [];

    public List<string> SolutionLines { get; set; } = [];

    public string Notes { get; set; } = string.Empty;
}