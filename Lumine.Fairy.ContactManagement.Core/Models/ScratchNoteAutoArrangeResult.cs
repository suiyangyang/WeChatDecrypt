using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Core.Models;

public sealed class ScratchNoteAutoArrangeResult
{
    public ScratchNoteDocument Document { get; set; } = new();

    public ScratchNoteParseResult ParseResult { get; set; } = new();

    public DailyReport DailyReport { get; set; } = new();
}