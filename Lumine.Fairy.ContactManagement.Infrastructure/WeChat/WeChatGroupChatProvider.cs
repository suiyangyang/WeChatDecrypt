using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.ContactManagement.Infrastructure.WeChat;

public sealed class WeChatGroupChatProvider(IWeChatService weChat) : IWeChatGroupChatProvider
{
    public async Task<IReadOnlyList<WeChatGroupChatCandidate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<WeChatGroupChatCandidate>();

        foreach (var account in await weChat.GetAccountsAsync(cancellationToken))
        {
            var sessions = await weChat.GetSessionsAsync(account.Id, cancellationToken);
            var sessionsByUsername = sessions
                .GroupBy(static session => session.Username, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<ChatContact> contacts = [];
            try
            {
                contacts = await weChat.GetChatContactsAsync(account.Id, includeGroups: true, cancellationToken: cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Older parser backends may not expose the contacts endpoint. The
                // session data remains a valid fallback in that case.
            }

            if (contacts.Count > 0)
            {
                var contactUsernames = contacts
                    .Select(static contact => contact.Username)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                result.AddRange(contacts.Select(contact =>
                {
                    sessionsByUsername.TryGetValue(contact.Username, out var session);
                    var isGroupChat = contact.IsGroup || session?.IsGroupConversation == true;
                    return new WeChatGroupChatCandidate(
                        account.Id,
                        account.DisplayName,
                        contact.Username,
                        ResolveDisplayName(contact, session),
                        session is null ? null : ToDateTimeOffset(session.LastMessageTime),
                        isGroupChat);
                }));

                // The contacts endpoint is the primary catalog, but some parser
                // versions only return part of the visible list. Keep session-only
                // entries as well so a non-empty contacts response cannot hide them.
                result.AddRange(sessions
                    .Where(session => !contactUsernames.Contains(session.Username))
                    .Select(session => new WeChatGroupChatCandidate(
                        account.Id,
                        account.DisplayName,
                        session.Username,
                        ResolveDisplayName(session),
                        ToDateTimeOffset(session.LastMessageTime),
                        session.IsGroupConversation)));
            }
            else
            {
                result.AddRange(sessions.Select(session => new WeChatGroupChatCandidate(
                    account.Id,
                    account.DisplayName,
                    session.Username,
                    ResolveDisplayName(session),
                    ToDateTimeOffset(session.LastMessageTime),
                    session.IsGroupConversation)));
            }
        }

        return result
            .GroupBy(candidate => $"{candidate.AccountId}:{candidate.WeChatUsername}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.LastMessageAt).First())
            .OrderBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static DateTimeOffset? ToDateTimeOffset(long timestamp)
    {
        if (timestamp <= 0) return null;
        return timestamp > 10_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
            : DateTimeOffset.FromUnixTimeSeconds(timestamp);
    }

    private static string ResolveDisplayName(ChatContact contact, ChatSession? session)
    {
        foreach (var candidate in new[] { contact.DisplayName, contact.Name, contact.Nickname, contact.Remark, session?.DisplayName })
        {
            var value = candidate?.Trim();
            if (!string.IsNullOrWhiteSpace(value) && !IsIdentifier(value, contact.Username))
                return value;
        }

        return contact.IsGroup || session?.IsGroupConversation == true ? "未知群聊" : "未知联系人";
    }

    private static string ResolveDisplayName(ChatSession session)
    {
        var value = session.DisplayName?.Trim();
        return !string.IsNullOrWhiteSpace(value) && !IsIdentifier(value, session.Username)
            ? value
            : session.IsGroupConversation ? "未知群聊" : "未知联系人";
    }

    private static bool IsIdentifier(string value, string username)
        => value.Equals(username, StringComparison.OrdinalIgnoreCase)
           || value.StartsWith("@@", StringComparison.Ordinal)
           || value.EndsWith("@chatroom", StringComparison.OrdinalIgnoreCase);
}
