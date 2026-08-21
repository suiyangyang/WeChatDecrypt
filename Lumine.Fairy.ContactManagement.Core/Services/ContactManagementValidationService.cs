using Lumine.Fairy.ContactManagement.Core.Models;
using Lumine.Fairy.ContactManagement.Models;
using System.Text.RegularExpressions;

namespace Lumine.Fairy.ContactManagement.Core.Services;

public sealed partial class ContactManagementValidationService
{
    public void Validate(ContactManagementDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);

        foreach (var contact in dataSet.Contacts)
        {
            EnsureHasValue(contact.FullName, "联系人姓名不能为空。");
            ValidateCollection(contact.Emails, IsValidEmail, $"联系人 {contact.FullName} 包含无效邮箱。");
            ValidateCollection(contact.Phones, IsValidPhone, $"联系人 {contact.FullName} 包含无效电话。");
        }

        foreach (var company in dataSet.Companies)
        {
            EnsureHasValue(company.Name, "公司名称不能为空。");
        }

        foreach (var project in dataSet.Projects)
        {
            EnsureHasValue(project.Name, "项目名称不能为空。");
        }

        foreach (var link in dataSet.RelationshipLinks)
        {
            EnsureHasValue(link.SourceEntityId, "关系源实体不能为空。");
            EnsureHasValue(link.TargetEntityId, "关系目标实体不能为空。");
        }

        foreach (var todoEntry in dataSet.TodoEntries)
        {
            EnsureHasValue(todoEntry.ProjectName, "Todo 项目名不能为空。");
            if (string.IsNullOrWhiteSpace(todoEntry.Title) && string.IsNullOrWhiteSpace(todoEntry.Content))
            {
                throw new InvalidOperationException("Todo 标题和内容不能同时为空。");
            }

            if (todoEntry.WorkHours is < 0)
            {
                throw new InvalidOperationException($"Todo {todoEntry.Id} 的工时不能为负数。");
            }
        }

        foreach (var clientIssue in dataSet.ClientIssues)
        {
            EnsureHasValue(clientIssue.ProjectName, "客户问题项目名不能为空。");
            EnsureHasValue(clientIssue.ClientName, "客户问题客户名不能为空。");
        }

        foreach (var dailyReport in dataSet.DailyReports)
        {
            if (dailyReport.TotalHours < 0)
            {
                throw new InvalidOperationException($"日报 {dailyReport.Date:yyyy-MM-dd} 的总工时不能为负数。");
            }
        }

        foreach (var scratchNoteDocument in dataSet.ScratchNoteDocuments)
        {
            EnsureHasValue(scratchNoteDocument.Id, "随手记文档 Id 不能为空。");
        }
    }

    public ContactManagementStatistics BuildStatistics(ContactManagementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new ContactManagementStatistics
        {
            ContactCount = snapshot.Contacts.Count,
            CompanyCount = snapshot.Companies.Count,
            ProjectCount = snapshot.Projects.Count,
            RelationshipCount = snapshot.RelationshipLinks.Count,
            TodoCount = snapshot.TodoEntries.Count,
            ClientIssueCount = snapshot.ClientIssues.Count,
            DailyReportCount = snapshot.DailyReports.Count,
            ScratchNoteCount = snapshot.ScratchNoteDocuments.Count
        };
    }

    private static void EnsureHasValue(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void ValidateCollection(IEnumerable<string> values, Func<string, bool> validator, string message)
    {
        foreach (var value in values)
        {
            if (!validator(value))
            {
                throw new InvalidOperationException(message);
            }
        }
    }

    private static bool IsValidEmail(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains('@', StringComparison.Ordinal);

    private static bool IsValidPhone(string value) =>
        string.IsNullOrWhiteSpace(value) || PhoneRegex().IsMatch(value);

    [GeneratedRegex(@"^[0-9+\-\s]+$")]
    private static partial Regex PhoneRegex();
}
