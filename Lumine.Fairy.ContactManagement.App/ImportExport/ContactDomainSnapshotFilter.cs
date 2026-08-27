using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.ImportExport;

internal static class ContactDomainSnapshotFilter
{
    public static ContactManagementSnapshot ToContactDomainSnapshot(ContactManagementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ContactManagementSnapshot
        {
            Contacts = snapshot.Contacts,
            Companies = snapshot.Companies,
            Projects = snapshot.Projects,
            WeChatGroupChats = snapshot.WeChatGroupChats,
            RelationshipLinks = snapshot.RelationshipLinks,
            TodoEntries = [],
            ClientIssues = [],
            DailyReports = [],
            ScratchNoteDocuments = []
        };
    }
}
