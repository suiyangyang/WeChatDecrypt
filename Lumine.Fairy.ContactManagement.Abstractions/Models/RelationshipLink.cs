namespace Lumine.Fairy.ContactManagement.Models;

public sealed class RelationshipLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceEntityId { get; set; } = string.Empty;
    public EntityKind SourceEntityKind { get; set; }
    public string TargetEntityId { get; set; } = string.Empty;
    public EntityKind TargetEntityKind { get; set; }
    public RelationshipLinkType LinkType { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public string Notes { get; set; } = string.Empty;
}
