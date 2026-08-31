namespace Lumine.Fairy.ContactManagement.Models;

public sealed class TodoChecklistItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }
}
