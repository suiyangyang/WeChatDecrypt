using Lumine.Fairy.Application;
using Lumine.Fairy.Protocol;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public sealed class WeChatAnalysisSelectionTests
{
    [Fact]
    public void ExactWeChatIdMatchesEvenWhenDisplayNameDiffers()
    {
        var settings = new WeChatAnalysisSettings
        {
            EnableWhitelist = true,
            WhitelistContactIds = ["contact-1"]
        };
        var session = new ChatSession("wxid_zhangsan", "客户备注名", "", 0);
        var whitelist = new[]
        {
            new WeChatWhitelistEntry("contact-1", "contact", "张三", ["张三"], ["wxid_zhangsan"])
        };

        Assert.True(WeChatAnalysisSelection.IsSessionAllowed(session, settings, whitelist));
    }

    [Fact]
    public void ShortAsciiAliasDoesNotCauseBroadSubstringMatch()
    {
        var settings = new WeChatAnalysisSettings
        {
            EnableWhitelist = true,
            WhitelistCompanyIds = ["company-1"]
        };
        var session = new ChatSession("wxid_other", "AI 实验室", "", 0);
        var whitelist = new[]
        {
            new WeChatWhitelistEntry("company-1", "company", "AI", ["AI"])
        };

        Assert.False(WeChatAnalysisSelection.IsSessionAllowed(session, settings, whitelist));
    }

    [Fact]
    public void CompanyEntryCanMatchLinkedContactExactId()
    {
        var settings = new WeChatAnalysisSettings
        {
            EnableWhitelist = true,
            WhitelistCompanyIds = ["company-1"]
        };
        var session = new ChatSession("wxid_customer", "未保存的备注", "", 0);
        var whitelist = new[]
        {
            new WeChatWhitelistEntry("company-1", "company", "客户公司", ["客户公司"], ["wxid_customer"])
        };

        Assert.True(WeChatAnalysisSelection.IsSessionAllowed(session, settings, whitelist));
    }
}
