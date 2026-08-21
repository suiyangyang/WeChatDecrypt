namespace Lumine.Fairy.ContactManagement.Models;

public sealed class ScratchNoteDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateOnly? WorkDate { get; set; }

    public string Title { get; set; } = string.Empty;

    public string RawText { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastArrangedAtUtc { get; set; }
}