using System.Globalization;
using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.App.Helpers;

internal static class ContactManagementDisplayText
{
    public static string GetContactRelationshipTypeText(ContactRelationshipType value)
    {
        return value switch
        {
            ContactRelationshipType.Stranger => "陌生人",
            ContactRelationshipType.Colleague => "同事",
            ContactRelationshipType.Acquaintance => "熟人",
            ContactRelationshipType.Family => "家人",
            ContactRelationshipType.Customer => "客户",
            ContactRelationshipType.Partner => "合作伙伴",
            ContactRelationshipType.Other => "其他",
            ContactRelationshipType.Self => "自己",
            _ => value.ToString()
        };
    }

    public static string GetEntityKindText(EntityKind value)
    {
        return value switch
        {
            EntityKind.Contact => "联系人",
            EntityKind.Company => "公司",
            EntityKind.Project => "项目",
            _ => value.ToString()
        };
    }

    public static string GetRelationshipLinkTypeText(RelationshipLinkType value)
    {
        return value switch
        {
            RelationshipLinkType.WorksAt => "就职于",
            RelationshipLinkType.BelongsToDepartment => "属于部门",
            RelationshipLinkType.OwnsProject => "负责项目",
            RelationshipLinkType.ParticipatesInProject => "参与项目",
            RelationshipLinkType.ProjectBelongsToCompany => "项目归属公司",
            RelationshipLinkType.Knows => "认识",
            RelationshipLinkType.ReportsTo => "汇报给",
            RelationshipLinkType.CollaboratesWith => "协作",
            _ => value.ToString()
        };
    }

    public static string GetDeliveryDateText(DateOnly? value)
    {
        return value.HasValue
            ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "长期项目";
    }
}
