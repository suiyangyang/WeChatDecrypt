using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Lumine.Fairy.ContactManagement.Models;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.Domain;
using Lumine.Fairy.Protocol;
using Lumine.Fairy.Infrastructure;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class WeChatAnalysisContextResolverTests
{
    private readonly WeChatAnalysisContextResolver _resolver = new();

    [Fact]
    public void ExactWechatDirectoryMapping_ReturnsCompanyAndProjectContext()
    {
        var snapshot = new ContactManagementSnapshot
        {
            Companies = [new CompanyProfile { Id = "company-1", Name = "示例公司" }],
            Projects = [new ProjectProfile { Id = "project-1", Name = "项目A", CompanyId = "company-1", Tags = ["续约"] }],
            WeChatGroupChats =
            [
                new WeChatGroupChatProfile
                {
                    AccountId = "account-1",
                    WeChatUsername = "wx_group",
                    DisplayName = "示例公司项目群",
                    CompanyId = "company-1",
                    ProjectId = "project-1",
                    IsActive = true
                }
            ]
        };

        var result = _resolver.Resolve(
            snapshot,
            new ChatAccount("account-1", "我的微信", 1),
            new ChatSession("wx_group", "示例公司项目群", "", 0, ChatConversationKind.Group));

        Assert.NotNull(result);
        Assert.Equal("company-1", result.CompanyId);
        Assert.Equal("示例公司", result.CompanyName);
        Assert.Equal("project-1", result.ProjectId);
        Assert.Equal("项目A", result.ProjectName);
        Assert.Equal(["续约"], result.ProjectTags);
        Assert.Equal("微信会话目录精确匹配", result.MatchMethod);
    }

    [Fact]
    public void ExactContactWechatId_ReturnsLinkedBusinessContext()
    {
        var snapshot = new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile
                {
                    Id = "contact-1",
                    FullName = "张三",
                    WeChatIds = ["wx_zhang"],
                    CompanyIds = ["company-1"],
                    ProjectIds = ["project-1"],
                    IsActive = true
                }
            ],
            Companies = [new CompanyProfile { Id = "company-1", Name = "示例公司" }],
            Projects = [new ProjectProfile { Id = "project-1", Name = "项目A", CompanyId = "company-1", IsActive = true }]
        };

        var result = _resolver.Resolve(
            snapshot,
            new ChatAccount("account-1", "我的微信", 1),
            new ChatSession("wx_zhang", "张三", "", 0));

        Assert.NotNull(result);
        Assert.Equal("contact-1", result.ContactId);
        Assert.Equal("张三", result.ContactName);
        Assert.Equal("示例公司", result.CompanyName);
        Assert.Equal("项目A", result.ProjectName);
        Assert.Equal("微信号精确匹配", result.MatchMethod);
    }

    [Fact]
    public void SharedFallbackAlias_DoesNotGuessContact()
    {
        var snapshot = new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile { Id = "contact-1", FullName = "客服", IsActive = true },
                new ContactProfile { Id = "contact-2", FullName = "客服", IsActive = true }
            ]
        };

        var result = _resolver.Resolve(
            snapshot,
            new ChatAccount("account-1", "我的微信", 1),
            new ChatSession("wx_unknown", "客服", "", 0));

        Assert.Null(result);
    }

    [Fact]
    public void GroupNameMatchingContact_DoesNotReturnContactContext()
    {
        var snapshot = new ContactManagementSnapshot
        {
            Contacts = [new ContactProfile { Id = "contact-1", FullName = "项目群", IsActive = true }],
            Companies = [new CompanyProfile { Id = "company-1", Name = "示例公司", IsActive = true }]
        };

        var result = _resolver.Resolve(
            snapshot,
            new ChatAccount("account-1", "我的微信", 1),
            new ChatSession("wx_group", "项目群", "", 0, ChatConversationKind.Group));

        Assert.Null(result);
    }

    [Fact]
    public void DuplicateExactContactWechatId_DoesNotGuessContact()
    {
        var snapshot = new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile { Id = "contact-1", FullName = "张三", WeChatIds = ["wx_same"], IsActive = true },
                new ContactProfile { Id = "contact-2", FullName = "李四", WeChatIds = ["wx_same"], IsActive = true }
            ]
        };

        var result = _resolver.Resolve(
            snapshot,
            new ChatAccount("account-1", "我的微信", 1),
            new ChatSession("wx_same", "某联系人", "", 0));

        Assert.Null(result);
    }

    [Fact]
    public void UnmappedSession_ReturnsNull()
    {
        var result = _resolver.Resolve(
            new ContactManagementSnapshot(),
            new ChatAccount("account-1", "我的微信", 1),
            new ChatSession("wx_unknown", "未知联系人", "", 0));

        Assert.Null(result);
    }

    [Fact]
    public async Task ContextHttpServer_RequiresTokenAndReturnsOnlyPublicContext()
    {
        var port = GetFreePort();
        await using var server = new WeChatContextHttpServer(
            "127.0.0.1",
            port,
            "test-context-token",
            (_, _) => Task.FromResult<ConversationCompanyContext?>(new ConversationCompanyContext(
                "company-internal-id",
                "示例公司",
                "contact-internal-id",
                "张三",
                "project-internal-id",
                "项目A",
                ["续约"],
                "微信号精确匹配")));
        await server.StartAsync();

        using var client = new HttpClient();
        var unauthorized = await client.GetAsync($"http://127.0.0.1:{port}/health");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-context-token");
        var body = new
        {
            accountId = "account-1",
            weChatUsername = "wx_zhang",
            displayName = "张三",
            isGroupChat = false
        };
        var response = await client.PostAsJsonAsync(server.Endpoint, body);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);

        Assert.Equal("张三", document.RootElement.GetProperty("contactName").GetString());
        Assert.Equal("示例公司", document.RootElement.GetProperty("companyName").GetString());
        Assert.DoesNotContain("internal-id", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("chat", raw, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
