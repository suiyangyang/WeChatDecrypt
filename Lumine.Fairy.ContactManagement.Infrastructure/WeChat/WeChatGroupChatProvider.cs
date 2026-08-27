using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.ContactManagement.Infrastructure.WeChat;

public sealed class WeChatGroupChatProvider(IWeChatService weChat) : IWeChatGroupChatProvider
{
    public async Task<IReadOnlyList<WeChatGroupChatCandidate>> GetRecentAsync(TimeSpan lookback, CancellationToken cancellationToken = default)
    {
        var threshold = DateTimeOffset.UtcNow.Subtract(lookback);
        var result = new List<WeChatGroupChatCandidate>();

        foreach (var account in await weChat.GetAccountsAsync(cancellationToken))
        {
            var sessions = await weChat.GetSessionsAsync(account.Id, cancellationToken);
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

            var contactsByUsername = contacts
                .Where(static contact => contact.IsGroup)
                .GroupBy(static contact => contact.Username, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

            result.AddRange(sessions
                .Where(static session => session.IsGroupConversation)
                .Select(session =>
                {
                    contactsByUsername.TryGetValue(session.Username, out var contact);
                    return new WeChatGroupChatCandidate(
                        account.Id,
                        account.DisplayName,
                        session.Username,
                        ResolveDisplayName(session, contact),
                        ToDateTimeOffset(session.LastMessageTime));
                })
                .Where(candidate => candidate.LastMessageAt is null || candidate.LastMessageAt >= threshold));
        }

        return result
            .GroupBy(candidate => $"{candidate.AccountId}:{candidate.WeChatUsername}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.LastMessageAt).First())
            .OrderByDescending(candidate => candidate.LastMessageAt)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static DateTimeOffset? ToDateTimeOffset(long timestamp)
    {
        if (timestamp <= 0) return null;
        return timestamp > 10_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
            : DateTimeOffset.FromUnixTimeSeconds(timestamp);
    }

    private static string ResolveDisplayName(ChatSession session, ChatContact? contact)
    {
        foreach (var candidate in new[] { contact?.DisplayName, contact?.Name, contact?.Nickname, contact?.Remark, session.DisplayName })
        {
            var value = candidate?.Trim();
            if (!string.IsNullOrWhiteSpace(value) && !IsIdentifier(value, session.Username))
                return value;
        }

        return "未知群聊";
    }

    private static bool IsIdentifier(string value, string username)
        => value.Equals(username, StringComparison.OrdinalIgnoreCase)
           || value.StartsWith("@@", StringComparison.Ordinal)
           || value.EndsWith("@chatroom", StringComparison.OrdinalIgnoreCase);
}
