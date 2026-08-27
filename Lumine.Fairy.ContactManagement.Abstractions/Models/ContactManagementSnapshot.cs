namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ContactManagementSnapshot
{
    public IReadOnlyList<ContactProfile> Contacts { get; init; } = [];
    public IReadOnlyList<CompanyProfile> Companies { get; init; } = [];
    public IReadOnlyList<ProjectProfile> Projects { get; init; } = [];
    public IReadOnlyList<WeChatGroupChatProfile> WeChatGroupChats { get; init; } = [];
    public IReadOnlyList<RelationshipLink> RelationshipLinks { get; init; } = [];
    public IReadOnlyList<TodoEntry> TodoEntries { get; init; } = [];
    public IReadOnlyList<ClientIssueEntry> ClientIssues { get; init; } = [];
    public IReadOnlyList<DailyReport> DailyReports { get; init; } = [];
    public IReadOnlyList<ScratchNoteDocument> ScratchNoteDocuments { get; init; } = [];
}
