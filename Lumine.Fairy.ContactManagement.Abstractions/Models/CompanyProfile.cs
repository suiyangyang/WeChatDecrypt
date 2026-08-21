namespace Lumine.Fairy.ContactManagement.Models;

public sealed class CompanyProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> Addresses { get; set; } = [];
    public List<string> Departments { get; set; } = [];
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
