using System.Security.Cryptography;
using System.Text;
using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.Domain;

namespace Lumine.Fairy.ContactManagement.Core.Services;

public sealed class WeChatGroupChatDirectoryService
{
    private readonly IContactManagementStore _store;
    private readonly IWeChatGroupChatProvider _provider;

    public WeChatGroupChatDirectoryService(IContactManagementStore store, IWeChatGroupChatProvider provider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<ContactManagementSnapshot> RefreshAsync(TimeSpan lookback, CancellationToken cancellationToken = default)
    {
        if (lookback <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lookback));

        var snapshot = await _store.LoadAsync(cancellationToken);
        var groupChats = snapshot.WeChatGroupChats.ToDictionary(GetIdentity, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in await _provider.GetRecentAsync(lookback, cancellationToken))
        {
            var identity = GetIdentity(candidate.AccountId, candidate.WeChatUsername);
            if (!groupChats.TryGetValue(identity, out var item))
            {
                item = new WeChatGroupChatProfile
                {
                    Id = BuildId(candidate.AccountId, candidate.WeChatUsername),
                    AccountId = candidate.AccountId,
                    AccountDisplayName = candidate.AccountDisplayName,
                    WeChatUsername = candidate.WeChatUsername,
                    IsActive = true
                };
                groupChats.Add(identity, item);
            }

            var candidateDisplayName = candidate.DisplayName?.Trim();
            if (!string.IsNullOrWhiteSpace(candidateDisplayName)
                && (!candidateDisplayName.Equals("未知群聊", StringComparison.Ordinal)
                    || IsUnknownDisplayName(item.DisplayName, candidate.WeChatUsername)))
                item.DisplayName = candidateDisplayName;
            else if (IsUnknownDisplayName(item.DisplayName, candidate.WeChatUsername))
                item.DisplayName = "未知群聊";
            item.AccountDisplayName = candidate.AccountDisplayName.Trim();
            item.LastMessageAt = candidate.LastMessageAt;
            item.LastSyncedAt = DateTimeOffset.UtcNow;
            item.IsActive = true;
        }

        var result = new ContactManagementSnapshot
        {
            Contacts = snapshot.Contacts,
            Companies = snapshot.Companies,
            Projects = snapshot.Projects,
            WeChatGroupChats = groupChats.Values
                .OrderByDescending(item => item.LastMessageAt)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            RelationshipLinks = snapshot.RelationshipLinks,
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        };

        await _store.SaveAsync(result, cancellationToken);
        return await _store.LoadAsync(cancellationToken);
    }

    public async Task<ContactManagementSnapshot> UpsertAsync(WeChatGroupChatProfile groupChat, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupChat);
        var snapshot = await _store.LoadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(groupChat.CompanyId)
            && !snapshot.Companies.Any(company => string.Equals(company.Id, groupChat.CompanyId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("选定的公司不存在或已被删除。");
        if (!string.IsNullOrWhiteSpace(groupChat.ProjectId)
            && !snapshot.Projects.Any(project => string.Equals(project.Id, groupChat.ProjectId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("选定的项目不存在或已被删除。");

        var groupChats = snapshot.WeChatGroupChats.ToList();
        var index = groupChats.FindIndex(item => string.Equals(item.Id, groupChat.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) groupChats[index] = groupChat;
        else groupChats.Add(groupChat);

        var result = new ContactManagementSnapshot
        {
            Contacts = snapshot.Contacts,
            Companies = snapshot.Companies,
            Projects = snapshot.Projects,
            WeChatGroupChats = groupChats,
            RelationshipLinks = snapshot.RelationshipLinks,
            TodoEntries = snapshot.TodoEntries,
            ClientIssues = snapshot.ClientIssues,
            DailyReports = snapshot.DailyReports,
            ScratchNoteDocuments = snapshot.ScratchNoteDocuments
        };
        await _store.SaveAsync(result, cancellationToken);
        return await _store.LoadAsync(cancellationToken);
    }

    private static string GetIdentity(WeChatGroupChatProfile item)
        => GetIdentity(item.AccountId, item.WeChatUsername);

    private static string GetIdentity(string accountId, string username)
        => $"{accountId.Trim()}:{username.Trim()}";

    private static string BuildId(string accountId, string username)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(GetIdentity(accountId, username)));
        return $"wechat-group:{Convert.ToHexString(bytes)[..24].ToLowerInvariant()}";
    }

    private static bool IsUnknownDisplayName(string? displayName, string username)
        => string.IsNullOrWhiteSpace(displayName)
           || displayName.Equals("未知群聊", StringComparison.Ordinal)
           || displayName.Equals(username, StringComparison.OrdinalIgnoreCase)
           || displayName.StartsWith("@@", StringComparison.Ordinal)
           || displayName.EndsWith("@chatroom", StringComparison.OrdinalIgnoreCase);
}
