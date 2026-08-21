using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Infrastructure.Persistence;

internal static class ContactManagementSampleSnapshotFactory
{
    public static ContactManagementSnapshot Create()
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
                    GroupChats = ["微信项目群", "QQ运维群"],
                    Notes = "导入前可删除或修改。"
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
            ],
            TodoEntries =
            [
                new TodoEntry
                {
                    Id = "sample-todo-1",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "示例项目",
                    Title = "整理导入模板",
                    Content = "整理导入模板",
                    Status = TodoStatus.InProgress,
                    WorkHours = 1.5m,
                    IsDaily = true,
                    IsFromScratchNote = true,
                    SourceText = "1.示例项目: 整理导入模板 1.5",
                    SortOrder = 1,
                    Notes = "示例 Todo"
                }
            ],
            ClientIssues =
            [
                new ClientIssueEntry
                {
                    Id = "sample-issue-1",
                    WorkDate = new DateOnly(2026, 3, 21),
                    ProjectName = "示例项目",
                    ClientName = "示例客户",
                    IssueLines = ["接口返回为空"],
                    SolutionLines = ["补充日志并检查参数"],
                    Notes = "示例客户问题"
                }
            ],
            DailyReports =
            [
                new DailyReport
                {
                    Date = new DateOnly(2026, 3, 21),
                    Items =
                    [
                        new TodoEntry
                        {
                            Id = "sample-todo-1",
                            WorkDate = new DateOnly(2026, 3, 21),
                            ProjectName = "示例项目",
                            Title = "整理导入模板",
                            Content = "整理导入模板",
                            Status = TodoStatus.InProgress,
                            WorkHours = 1.5m,
                            IsDaily = true,
                            IsFromScratchNote = true,
                            SourceText = "1.示例项目: 整理导入模板 1.5",
                            SortOrder = 1,
                            Notes = "示例 Todo"
                        }
                    ],
                    ClientIssues =
                    [
                        new ClientIssueEntry
                        {
                            Id = "sample-issue-1",
                            WorkDate = new DateOnly(2026, 3, 21),
                            ProjectName = "示例项目",
                            ClientName = "示例客户",
                            IssueLines = ["接口返回为空"],
                            SolutionLines = ["补充日志并检查参数"],
                            Notes = "示例客户问题"
                        }
                    ],
                    TotalHours = 1.5m,
                    RawScratchText = "3/21\n1.示例项目: 整理导入模板 1.5",
                    FormattedReportText = "1、(示例项目)整理导入模板,1.5;"
                }
            ],
            ScratchNoteDocuments =
            [
                new ScratchNoteDocument
                {
                    Id = "scratch-note-20260321",
                    WorkDate = new DateOnly(2026, 3, 21),
                    Title = "2026-03-21 随手记",
                    RawText = "3/21\n1.示例项目: 整理导入模板 1.5",
                    CreatedAtUtc = new DateTimeOffset(2026, 3, 21, 1, 0, 0, TimeSpan.Zero),
                    UpdatedAtUtc = new DateTimeOffset(2026, 3, 21, 1, 10, 0, TimeSpan.Zero),
                    LastArrangedAtUtc = new DateTimeOffset(2026, 3, 21, 1, 10, 0, TimeSpan.Zero)
                }
            ]
        };
    }
}
