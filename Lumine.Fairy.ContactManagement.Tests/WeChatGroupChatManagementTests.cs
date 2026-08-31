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

        var snapshot = await service.RefreshAsync();

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
    public async Task Provider_ReturnsAllContactsAndUsesContactMetadata()
    {
        var provider = new Lumine.Fairy.ContactManagement.Infrastructure.WeChat.WeChatGroupChatProvider(
            new StubWeChatService(
                [new ChatAccount("account-1", "工作微信", 1)],
                [
                    new ChatSession("12345@chatroom", "12345@chatroom", "", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ChatConversationKind.Group),
                    new ChatSession("session-only-friend", "会话中的联系人", "", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ChatConversationKind.Direct)
                ],
                [
                    new ChatContact("12345@chatroom", "真实群名称", "群昵称", "group"),
                    new ChatContact("wxid-friend", "真实联系人", "联系人昵称", "friend")
                ]));

        var result = await provider.GetAllAsync();

        Assert.Equal(3, result.Count);
        var groupChat = Assert.Single(result, item => item.IsGroupChat);
        Assert.Equal("真实群名称", groupChat.DisplayName);
        Assert.NotNull(groupChat.LastMessageAt);
        var contact = Assert.Single(result, item => item.WeChatUsername == "wxid-friend");
        Assert.Equal("真实联系人", contact.DisplayName);
        Assert.Contains(result, item => item.WeChatUsername == "session-only-friend");
    }

    [Fact]
    public async Task RefreshAsync_MarksEntriesMissingFromWechatCatalogInactive()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var store = new ContactManagementSqliteStore(
            new ContactManagementStorageOptions { BaseDirectoryPath = directory },
            new ContactManagementValidationService());
        await store.SaveAsync(new ContactManagementSnapshot
        {
            WeChatGroupChats =
            [
                new WeChatGroupChatProfile
                {
                    Id = "old-entry",
                    AccountId = "account-1",
                    WeChatUsername = "old-entry",
                    DisplayName = "已移除联系人",
                    CompanyId = "company-1"
                }
            ]
        });

        var service = new WeChatGroupChatDirectoryService(
            store,
            new StubGroupChatProvider(
            [
                new WeChatGroupChatCandidate("account-1", "工作微信", "new-entry", "新联系人", null, false)
            ]));

        var snapshot = await service.RefreshAsync();

        var oldEntry = Assert.Single(snapshot.WeChatGroupChats, item => item.WeChatUsername == "old-entry");
        var newEntry = Assert.Single(snapshot.WeChatGroupChats, item => item.WeChatUsername == "new-entry");
        Assert.False(oldEntry.IsActive);
        Assert.Equal("company-1", oldEntry.CompanyId);
        Assert.True(newEntry.IsActive);
        Assert.False(newEntry.IsGroupChat);
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
    public void GroupChatItem_ShowsTheWechatEntryType()
    {
        var group = WeChatGroupChatItemViewModel.FromModel(new WeChatGroupChatProfile
        {
            Id = "group-1",
            IsGroupChat = true,
            DisplayName = "示例群聊"
        }, []);
        var contact = WeChatGroupChatItemViewModel.FromModel(new WeChatGroupChatProfile
        {
            Id = "contact-1",
            IsGroupChat = false,
            DisplayName = "示例联系人"
        }, []);

        Assert.Equal("群聊", group.TypeText);
        Assert.Equal("联系人", contact.TypeText);
    }

    [Fact]
    public void GroupChatItem_ChangingSelectedCompanyUpdatesCompanyWithoutRefreshingItemsSource()
    {
        var item = WeChatGroupChatItemViewModel.FromModel(new WeChatGroupChatProfile
        {
            Id = "group-1",
            AccountId = "account-1",
            WeChatUsername = "12345@chatroom",
            DisplayName = "示例群聊"
        },
        [
            new LookupOptionViewModel { Id = "company-1", DisplayName = "示例公司" },
            new LookupOptionViewModel { Id = "company-2", DisplayName = "另一家公司" }
        ]);

        item.SelectedCompanyOption = item.CompanyOptions.Single(option => option.Id == "company-2");

        Assert.Equal("company-2", item.CompanyId);
        Assert.True(item.HasUnsavedChanges);
        Assert.Contains(item.SelectedCompanyOption, item.FilteredCompanyOptions);
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

    [Theory]
    [InlineData("北辰", "company-2")]
    [InlineData("beichensheji", "company-2")]
    [InlineData("不存在", null)]
    public void GroupChatItem_SearchingDoesNotClearCompanyAssignment(string keyword, string? expectedCompanyId)
    {
        var item = WeChatGroupChatItemViewModel.FromModel(new WeChatGroupChatProfile
        {
            Id = "group-1",
            CompanyId = "company-1"
        },
        [
            new LookupOptionViewModel { Id = "company-1", DisplayName = "星云科技" },
            new LookupOptionViewModel { Id = "company-2", DisplayName = "北辰设计" }
        ]);

        // 可编辑 ComboBox 在输入不等于完整候选名称时会暂时取消选择。
        item.SelectedCompanyOption = null;
        item.CompanyFilterText = keyword;

        if (expectedCompanyId is null)
        {
            Assert.Empty(item.FilteredCompanyOptions);
        }
        else
        {
            Assert.Equal(expectedCompanyId, Assert.Single(item.FilteredCompanyOptions).Id);
        }
        Assert.Equal(keyword, item.CompanyFilterText);
        Assert.Equal("星云科技", item.CompanyName);
        Assert.Equal("company-1", item.ToModel().CompanyId);

        item.CompanyFilterText = string.Empty;
        Assert.Equal(3, item.FilteredCompanyOptions.Count);

        item.SelectedCompanyOption = item.CompanyOptions.Single(option => option.Id == "company-2");
        Assert.Equal("company-2", item.ToModel().CompanyId);

        item.SelectedCompanyOption = item.CompanyOptions.Single(option => option.Id == string.Empty);
        Assert.Null(item.ToModel().CompanyId);
        Assert.Equal("未分配", item.CompanyName);
    }

    [Fact]
    public void GroupChatItem_FilterRefreshNeverRemovesTheSelectedCompany()
    {
        var item = WeChatGroupChatItemViewModel.FromModel(new WeChatGroupChatProfile
        {
            Id = "group-1",
            CompanyId = "company-1"
        },
        [
            new LookupOptionViewModel { Id = "company-1", DisplayName = "星云科技" },
            new LookupOptionViewModel { Id = "company-2", DisplayName = "北辰设计" }
        ]);
        var selected = item.SelectedCompanyOption;
        item.FilteredCompanyOptions.CollectionChanged += (_, args) =>
        {
            Assert.NotEqual(System.Collections.Specialized.NotifyCollectionChangedAction.Reset, args.Action);
            Assert.Contains(selected, item.FilteredCompanyOptions);
        };

        item.CompanyFilterText = "星云";
        Assert.Same(selected, Assert.Single(item.FilteredCompanyOptions));

        item.CompanyFilterText = "星云科技";
        Assert.Equal(item.CompanyOptions, item.FilteredCompanyOptions);
        Assert.Equal("company-1", item.ToModel().CompanyId);
    }

    [Fact]
    public async Task Directory_PersistsSelectedCompanyAfterSavingAndReloading()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Lumine.Fairy.ContactManagement.Tests", Guid.NewGuid().ToString("N"));
        var store = new ContactManagementSqliteStore(
            new ContactManagementStorageOptions { BaseDirectoryPath = directory },
            new ContactManagementValidationService());
        await store.SaveAsync(new ContactManagementSnapshot
        {
            Companies = [new CompanyProfile { Id = "company-1", Name = "示例公司" }],
            WeChatGroupChats = [new WeChatGroupChatProfile
            {
                Id = "group-1",
                AccountId = "account-1",
                WeChatUsername = "12345@chatroom",
                DisplayName = "示例群聊"
            }]
        });

        var service = new WeChatGroupChatDirectoryService(store, new StubGroupChatProvider([]));
        var item = WeChatGroupChatItemViewModel.FromModel(
            (await store.LoadAsync()).WeChatGroupChats.Single(),
            [new LookupOptionViewModel { Id = "company-1", DisplayName = "示例公司" }]);
        item.SelectedCompanyOption = item.CompanyOptions.Single(option => option.Id == "company-1");
        item.CompanyFilterText = "示例公司";
        Assert.Equal(2, item.FilteredCompanyOptions.Count);

        item.CompanyFilterText = "示例";
        Assert.Equal("company-1", Assert.Single(item.FilteredCompanyOptions).Id);

        item.CompanyFilterText = "sjgs";
        Assert.Equal("company-1", Assert.Single(item.FilteredCompanyOptions).Id);

        await service.UpsertAsync(item.ToModel());

        var reloaded = await store.LoadAsync();
        Assert.Equal("company-1", reloaded.WeChatGroupChats.Single().CompanyId);
    }

    private sealed class StubGroupChatProvider(IReadOnlyList<WeChatGroupChatCandidate> candidates) : IWeChatGroupChatProvider
    {
        public Task<IReadOnlyList<WeChatGroupChatCandidate>> GetAllAsync(CancellationToken cancellationToken = default)
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
