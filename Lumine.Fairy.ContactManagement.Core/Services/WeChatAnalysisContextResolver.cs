using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.ContactManagement.Core.Services;

/// <summary>
/// Resolves the smallest useful business context for a WeChat conversation.
/// This resolver reads the local contact-management snapshot only; it never
/// reads or stores chat message content.
/// </summary>
public sealed class WeChatAnalysisContextResolver
{
    public ConversationCompanyContext? Resolve(
        ContactManagementSnapshot snapshot,
        ChatAccount account,
        ChatSession session)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(session);

        var chatDirectoryMatches = snapshot.WeChatGroupChats.Where(item =>
            item.IsActive
            && string.Equals(item.AccountId, account.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.WeChatUsername, session.Username, StringComparison.OrdinalIgnoreCase))
            .ToList();
        // Duplicate directory rows are ambiguous and must not silently select one.
        var chatDirectoryEntry = chatDirectoryMatches.Count == 1 ? chatDirectoryMatches[0] : null;

        // A group name can resemble a person's name; group chats are resolved
        // through the group/project/company directory instead of contact aliases.
        var contactMatch = session.IsGroupConversation ? null : FindContact(snapshot.Contacts, session);
        var companyId = chatDirectoryEntry?.CompanyId
                        ?? contactMatch?.CompanyIds.FirstOrDefault();
        var projectId = chatDirectoryEntry?.ProjectId
                        ?? contactMatch?.ProjectIds.FirstOrDefault();

        var matchedCompany = FindCompany(snapshot.Companies, session, companyId);
        var matchedProject = FindProject(snapshot.Projects, session, projectId, companyId);

        if (chatDirectoryEntry is null && contactMatch is null && matchedCompany is null && matchedProject is null)
            return null;

        var matchMethod = chatDirectoryEntry is not null
            ? "微信会话目录精确匹配"
            : contactMatch is not null
                ? contactMatch.WeChatIds.Any(id => string.Equals(id?.Trim(), session.Username.Trim(), StringComparison.OrdinalIgnoreCase))
                    ? "微信号精确匹配"
                    : "联系人名称或昵称匹配"
                : matchedProject is not null
                    ? "项目名称或群聊名称匹配"
                    : "公司名称匹配";

        return new ConversationCompanyContext(
            matchedCompany?.Id ?? companyId,
            matchedCompany?.Name,
            contactMatch?.Id,
            contactMatch?.FullName,
            matchedProject?.Id ?? projectId,
            matchedProject?.Name,
            matchedProject?.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            matchMethod);
    }

    private static ContactProfile? FindContact(
        IReadOnlyList<ContactProfile> contacts,
        ChatSession session)
    {
        var activeContacts = contacts.Where(contact => contact.IsActive).ToList();
        var exactMatches = activeContacts.Where(contact =>
            contact.WeChatIds.Any(id => string.Equals(id?.Trim(), session.Username.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (exactMatches.Count == 1) return exactMatches[0];
        if (exactMatches.Count > 1) return null;

        var sessionText = $"{session.Username}\n{session.DisplayName}";
        var matches = activeContacts
            .Select(contact => new
            {
                Contact = contact,
                AliasLength = new[] { contact.FullName }
                    .Concat(contact.Nicknames)
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Select(alias => alias.Trim())
                    .Where(alias => alias.Length >= 2 && sessionText.Contains(alias, StringComparison.OrdinalIgnoreCase))
                    .Select(alias => alias.Length)
                    .DefaultIfEmpty(0)
                    .Max()
            })
            .Where(item => item.AliasLength > 0)
            .OrderByDescending(item => item.AliasLength)
            .ToList();

        // Do not guess when the best alias is shared by multiple contacts.
        var best = matches.FirstOrDefault();
        return best is not null && matches.Count(item => item.AliasLength == best.AliasLength) == 1
            ? best.Contact
            : null;
    }

    private static CompanyProfile? FindCompany(
        IReadOnlyList<CompanyProfile> companies,
        ChatSession session,
        string? preferredId)
    {
        var preferred = companies.FirstOrDefault(company =>
            company.IsActive && string.Equals(company.Id, preferredId, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null) return preferred;

        return FindLongestNameMatch(
            companies.Where(company => company.IsActive),
            session,
            company => new[] { company.Name });
    }

    private static ProjectProfile? FindProject(
        IReadOnlyList<ProjectProfile> projects,
        ChatSession session,
        string? preferredId,
        string? companyId)
    {
        var preferred = projects.FirstOrDefault(project =>
            project.IsActive
            && string.Equals(project.Id, preferredId, StringComparison.OrdinalIgnoreCase));
        if (preferred is not null) return preferred;

        var candidates = projects.Where(project =>
            project.IsActive
            && (string.IsNullOrWhiteSpace(companyId)
                || string.Equals(project.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)));
        return FindLongestNameMatch(candidates, session, project =>
            new[] { project.Name }.Concat(project.GroupChats));
    }

    private static T? FindLongestNameMatch<T>(
        IEnumerable<T> candidates,
        ChatSession session,
        Func<T, IEnumerable<string>> names)
    {
        var sessionText = $"{session.Username}\n{session.DisplayName}";
        var matches = candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                NameLength = names(candidate)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Where(name => name.Length >= 2 && sessionText.Contains(name, StringComparison.OrdinalIgnoreCase))
                    .Select(name => name.Length)
                    .DefaultIfEmpty(0)
                    .Max()
            })
            .Where(item => item.NameLength > 0)
            .OrderByDescending(item => item.NameLength)
            .ToList();

        var best = matches.FirstOrDefault();
        return best is not null && matches.Count(item => item.NameLength == best.NameLength) == 1
            ? best.Candidate
            : default;
    }
}
