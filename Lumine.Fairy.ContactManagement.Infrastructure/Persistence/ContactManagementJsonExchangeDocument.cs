using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

internal sealed class ContactManagementJsonExchangeDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<ContactProfile> Contacts { get; set; } = [];

    public List<CompanyProfile> Companies { get; set; } = [];

    public List<ProjectProfile> Projects { get; set; } = [];

    public List<RelationshipLink> RelationshipLinks { get; set; } = [];

    public List<TodoEntry> TodoEntries { get; set; } = [];

    public List<ClientIssueEntry> ClientIssues { get; set; } = [];

    public List<DailyReport> DailyReports { get; set; } = [];

    public List<ScratchNoteDocument> ScratchNoteDocuments { get; set; } = [];

    public ContactManagementSnapshot ToSnapshot() => new()
    {
        Contacts = Contacts,
        Companies = Companies,
        Projects = Projects,
        RelationshipLinks = RelationshipLinks,
        TodoEntries = TodoEntries,
        ClientIssues = ClientIssues,
        DailyReports = DailyReports,
        ScratchNoteDocuments = ScratchNoteDocuments
    };

    public static ContactManagementJsonExchangeDocument FromSnapshot(ContactManagementSnapshot snapshot, int schemaVersion)
    {
        return new ContactManagementJsonExchangeDocument
        {
            SchemaVersion = schemaVersion,
            Contacts = [.. snapshot.Contacts],
            Companies = [.. snapshot.Companies],
            Projects = [.. snapshot.Projects],
            RelationshipLinks = [.. snapshot.RelationshipLinks],
            TodoEntries = [.. snapshot.TodoEntries],
            ClientIssues = [.. snapshot.ClientIssues],
            DailyReports = [.. snapshot.DailyReports],
            ScratchNoteDocuments = [.. snapshot.ScratchNoteDocuments]
        };
    }
}
