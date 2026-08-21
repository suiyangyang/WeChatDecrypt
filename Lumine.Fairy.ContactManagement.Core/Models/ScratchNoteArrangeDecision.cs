namespace Lumine.Fairy.ContactManagement.Core.Models;

public sealed class ScratchNoteArrangeDecision
{
    public bool ShouldArrange { get; set; }

    public ScratchNoteArrangeDepth Depth { get; set; } = ScratchNoteArrangeDepth.None;

    public TimeSpan? RecommendedDelay { get; set; }

    public string Reason { get; set; } = string.Empty;
}