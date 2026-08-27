using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.ImportExport;

internal sealed class ContactDomainJsonExchangeDocument
{
    public int SchemaVersion { get; set; }

    public List<ContactProfile> Contacts { get; set; } = [];

    public List<CompanyProfile> Companies { get; set; } = [];

    public List<ProjectProfile> Projects { get; set; } = [];

    public List<WeChatGroupChatProfile> WeChatGroupChats { get; set; } = [];

    public List<RelationshipLink> RelationshipLinks { get; set; } = [];

    public ContactManagementSnapshot ToSnapshot() => new()
    {
        Contacts = Contacts,
        Companies = Companies,
        Projects = Projects,
        WeChatGroupChats = WeChatGroupChats,
        RelationshipLinks = RelationshipLinks
    };

    public static ContactDomainJsonExchangeDocument FromSnapshot(ContactManagementSnapshot snapshot, int schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ContactDomainJsonExchangeDocument
        {
            SchemaVersion = schemaVersion,
            Contacts = [.. snapshot.Contacts],
            Companies = [.. snapshot.Companies],
            Projects = [.. snapshot.Projects],
            WeChatGroupChats = [.. snapshot.WeChatGroupChats],
            RelationshipLinks = [.. snapshot.RelationshipLinks]
        };
    }
}
