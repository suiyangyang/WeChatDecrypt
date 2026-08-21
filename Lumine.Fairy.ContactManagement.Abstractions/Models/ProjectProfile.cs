namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ProjectProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? CompanyId { get; set; }
    public string? OwnerContactId { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> GroupChats { get; set; } = [];
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
