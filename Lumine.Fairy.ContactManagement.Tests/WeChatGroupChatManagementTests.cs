using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;
using Lumine.Fairy.ContactManagement.App.ViewModels;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Infrastructure;
using Lumine.Fairy.Protocol;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class WeChatGroupChatManagementTests
{
    [Fact]
    public async Task RefreshAsync_PreservesCompanyMappingForStableWechatIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var options = new ContactManagementStorageOptions { BaseDirectoryPath = directory };
        var store = new ContactManagementSqliteStore(options, new ContactManagementValidationService());
        await store.SaveAsync(new ContactManagementSnapshot
        {
            Companies = [new CompanyProfile { Id = "company-1", Name = "示例公司" }],
            WeChatGroupChats = [new WeChatGroupChatProfile
            {
                Id = "wechat-group:stable",
                AccountId = "account-1",
                WeChatUsername = "@@room-1",
                DisplayName = "旧群名",
                CompanyId = "company-1"
            }]
        });

        var provider = new StubGroupChatProvider([
            new WeChatGroupChatCandidate("account-1", "工作微信", "@@room-1", "新群名", DateTimeOffset.UtcNow)
        ]);
        var service = new WeChatGroupChatDirectoryService(store, provider);

        var snapshot = await service.RefreshAsync(TimeSpan.FromDays(30));

        var groupChat = Assert.Single(snapshot.WeChatGroupChats);
        Assert.Equal("company-1", groupChat.CompanyId);
        Assert.Equal("新群名", groupChat.DisplayName);
        Assert.Equal("wechat-group:stable", groupChat.Id);
    }

    [Fact]
    public async Task TodoStore_PersistsCompanyAndConversationMetadata()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"), "todos.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var store = new SqliteTodoStore(databasePath);
        var candidate = new TodoRecord(
            "todo-1", "跟进报价", "确认报价范围", "high", null, null, .95, [],
            "pending_confirmation", DateTimeOffset.UtcNow, null, null, "wechat-analysis", "@@room-1",
            "company-1", "示例公司", "示例公司项目群", true);

        await store.AddCandidateAsync(candidate);

        var saved = await store.GetAsync(candidate.Id);
        Assert.NotNull(saved);
        Assert.Equal("company-1", saved!.CompanyId);
        Assert.Equal("示例公司", saved.CompanyName);
        Assert.Equal("示例公司项目群", saved.ConversationDisplayName);
        Assert.True(saved.IsGroupConversation);
    }

    [Fact]
    public async Task Provider_UsesContactMetadataWhenSessionNameIsWechatIdentifier()
    {
        var provider = new Lumine.Fairy.ContactManagement.Infrastructure.WeChat.WeChatGroupChatProvider(
            new StubWeChatService(
                [new ChatAccount("account-1", "工作微信", 1)],
                [new ChatSession("12345@chatroom", "12345@chatroom", "", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ChatConversationKind.Group)],
                [new ChatContact("12345@chatroom", "真实群名称", "群昵称", "group")]));

        var result = await provider.GetRecentAsync(TimeSpan.FromDays(30));

        var groupChat = Assert.Single(result);
        Assert.Equal("真实群名称", groupChat.DisplayName);
        Assert.NotNull(groupChat.LastMessageAt);
    }

    [Fact]
    public void GroupChatItem_ShowsUnassignedWhenCompanyIdIsEmptyOrUnknown()
    {
        var item = WeChatGroupChatItemViewModel.FromModel(new WeChatGroupChatProfile
        {
            Id = "group-1",
            AccountId = "account-1",
            WeChatUsername = "12345@chatroom",
            DisplayName = "示例群聊"
        }, [new LookupOptionViewModel { Id = "company-1", DisplayName = "示例公司" }]);

        Assert.Equal("未分配", item.CompanyName);
        Assert.Null(item.CompanyId);

        item.CompanyId = "company-deleted";
        item.SetCompanyOptions([new LookupOptionViewModel { Id = "company-1", DisplayName = "示例公司" }]);

        Assert.Equal("未分配", item.CompanyName);
        Assert.Null(item.CompanyId);
    }

    [Fact]
    public async Task Directory_AllowsSavingGroupChatWithoutCompany()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var store = new ContactManagementSqliteStore(
            new ContactManagementStorageOptions { BaseDirectoryPath = directory },
            new ContactManagementValidationService());
        await store.SaveAsync(new ContactManagementSnapshot
        {
            Companies = [new CompanyProfile { Id = "company-1", Name = "示例公司" }]
        });

        var service = new WeChatGroupChatDirectoryService(
            store,
            new StubGroupChatProvider([]));

        var snapshot = await service.UpsertAsync(new WeChatGroupChatProfile
        {
            Id = "group-1",
            AccountId = "account-1",
            WeChatUsername = "12345@chatroom",
            DisplayName = "未分配群聊"
        });

        var groupChat = Assert.Single(snapshot.WeChatGroupChats);
        Assert.Null(groupChat.CompanyId);
    }

    private sealed class StubGroupChatProvider(IReadOnlyList<WeChatGroupChatCandidate> candidates) : IWeChatGroupChatProvider
    {
        public Task<IReadOnlyList<WeChatGroupChatCandidate>> GetRecentAsync(TimeSpan lookback, CancellationToken cancellationToken = default)
            => Task.FromResult(candidates);
    }

    private sealed class StubWeChatService(
        IReadOnlyList<ChatAccount> accounts,
        IReadOnlyList<ChatSession> sessions,
        IReadOnlyList<ChatContact> contacts) : IWeChatService
    {
        public Task<IReadOnlyList<ChatAccount>> GetAccountsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(accounts);

        public Task<IReadOnlyList<ChatSession>> GetSessionsAsync(string accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(sessions);

        public Task<IReadOnlyList<ChatContact>> GetChatContactsAsync(string accountId, bool includeGroups = true, CancellationToken cancellationToken = default)
            => Task.FromResult(contacts);

        public Task<IReadOnlyList<ChatMessage>> GetLatestMessagesAsync(string accountId, string conversationId, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
    }
}
