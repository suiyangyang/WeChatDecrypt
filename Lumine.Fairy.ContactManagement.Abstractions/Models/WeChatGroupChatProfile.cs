namespace Lumine.Fairy.ContactManagement.Models;

/// <summary>
/// A locally maintained mapping between a stable WeChat group identifier and a company.
/// Message content is intentionally not stored here.
/// </summary>
public sealed class WeChatGroupChatProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountId { get; set; } = string.Empty;
    public string AccountDisplayName { get; set; } = string.Empty;
    public string WeChatUsername { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsGroupChat { get; set; } = true;
    public DateTimeOffset? LastMessageAt { get; set; }
    public string? CompanyId { get; set; }
    public string? ProjectId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LastSyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
