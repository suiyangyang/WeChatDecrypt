using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Core.Models;

public sealed class ContactManagementDataSet
{
    public List<ContactProfile> Contacts { get; init; } = [];
    public List<CompanyProfile> Companies { get; init; } = [];
    public List<ProjectProfile> Projects { get; init; } = [];
    public List<WeChatGroupChatProfile> WeChatGroupChats { get; init; } = [];
    public List<RelationshipLink> RelationshipLinks { get; init; } = [];
    public List<TodoEntry> TodoEntries { get; init; } = [];
    public List<ClientIssueEntry> ClientIssues { get; init; } = [];
    public List<DailyReport> DailyReports { get; init; } = [];
    public List<ScratchNoteDocument> ScratchNoteDocuments { get; init; } = [];

    public ContactManagementSnapshot ToSnapshot() => new()
    {
        Contacts = Contacts,
        Companies = Companies,
        Projects = Projects,
        WeChatGroupChats = WeChatGroupChats,
        RelationshipLinks = RelationshipLinks,
        TodoEntries = TodoEntries,
        ClientIssues = ClientIssues,
        DailyReports = DailyReports,
        ScratchNoteDocuments = ScratchNoteDocuments
    };

    public static ContactManagementDataSet FromSnapshot(ContactManagementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ContactManagementDataSet
        {
            Contacts = [.. snapshot.Contacts],
            Companies = [.. snapshot.Companies],
            Projects = [.. snapshot.Projects],
            WeChatGroupChats = [.. snapshot.WeChatGroupChats],
            RelationshipLinks = [.. snapshot.RelationshipLinks],
            TodoEntries = [.. snapshot.TodoEntries],
            ClientIssues = [.. snapshot.ClientIssues],
            DailyReports = [.. snapshot.DailyReports],
            ScratchNoteDocuments = [.. snapshot.ScratchNoteDocuments]
        };
    }
}
