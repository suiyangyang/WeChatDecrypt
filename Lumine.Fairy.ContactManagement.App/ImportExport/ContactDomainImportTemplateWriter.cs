using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.ImportExport;

public static class ContactDomainImportTemplateWriter
{
    public static Task WriteExampleTemplateAsync(string filePath, int schemaVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var snapshot = CreateContactDomainSampleSnapshot();
        var extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return Task.Run(() => ContactDomainExcelWorkbookMapper.WriteSnapshot(filePath, snapshot), cancellationToken);
        }

        return Task.Run(async () =>
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = ContactDomainJsonExchangeDocument.FromSnapshot(snapshot, schemaVersion);
            await using var stream = File.Create(filePath);
            await System.Text.Json.JsonSerializer.SerializeAsync(stream, document, cancellationToken: cancellationToken);
        }, cancellationToken);
    }

    private static ContactManagementSnapshot CreateContactDomainSampleSnapshot()
    {
        const string contactId = "sample-contact-1";
        const string companyId = "sample-company-1";
        const string projectId = "sample-project-1";

        return new ContactManagementSnapshot
        {
            Contacts =
            [
                new ContactProfile
                {
                    Id = contactId,
                    FullName = "示例联系人",
                    Nicknames = ["微信昵称", "昵称别名"],
                    Phones = ["13800000000"],
                    Emails = ["sample@example.com"],
                    CompanyIds = [companyId],
                    ProjectIds = [projectId],
                    RelationshipType = ContactRelationshipType.Acquaintance,
                    Notes = "可按需替换为真实数据。"
                }
            ],
            Companies =
            [
                new CompanyProfile
                {
                    Id = companyId,
                    Name = "示例公司",
                    Addresses = ["上海市浦东新区示例路 100 号"],
                    Departments = ["市场部", "产品部"],
                    Notes = "用于演示导入格式。"
                }
            ],
            Projects =
            [
                new ProjectProfile
                {
                    Id = projectId,
                    Name = "示例项目",
                    CompanyId = companyId,
                    OwnerContactId = contactId,
                    Tags = ["crm", "template"],
                    GroupChats = ["微信项目群", "QQ项目群"],
                    Notes = "导入前可删除或修改。"
                }
            ],
            WeChatGroupChats =
            [
                new WeChatGroupChatProfile
                {
                    Id = "sample-wechat-group-1",
                    AccountId = "sample-wechat-account-1",
                    AccountDisplayName = "示例微信账号",
                    WeChatUsername = "@@sample-group-room",
                    DisplayName = "示例公司项目群",
                    CompanyId = companyId,
                    LastMessageAt = DateTimeOffset.UtcNow,
                    LastSyncedAt = DateTimeOffset.UtcNow
                }
            ],
            RelationshipLinks =
            [
                new RelationshipLink
                {
                    Id = "sample-link-1",
                    SourceEntityId = contactId,
                    SourceEntityKind = EntityKind.Contact,
                    TargetEntityId = companyId,
                    TargetEntityKind = EntityKind.Company,
                    LinkType = RelationshipLinkType.WorksAt,
                    DepartmentName = "市场部",
                    Title = "客户经理",
                    IsPrimary = true,
                    Notes = "示例关系。"
                }
            ]
        };
    }
}
