namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ContactProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FullName { get; set; } = string.Empty;
    public List<string> Nicknames { get; set; } = [];
    /// <summary>
    /// 已确认的微信联系人标识（通常为 wxid_* 或其他稳定 username）。
    /// </summary>
    public List<string> WeChatIds { get; set; } = [];
    public List<string> Phones { get; set; } = [];
    public List<string> Emails { get; set; } = [];
    public List<string> CompanyIds { get; set; } = [];
    public List<string> ProjectIds { get; set; } = [];
    public ContactRelationshipType RelationshipType { get; set; } = ContactRelationshipType.Stranger;
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
