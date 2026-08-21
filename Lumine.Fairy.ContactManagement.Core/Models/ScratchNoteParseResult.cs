using Lumine.Fairy.ContactManagement.Models;

namespace Lumine.Fairy.ContactManagement.Core.Models;

public sealed class ScratchNoteParseResult
{
    public DateOnly? WorkDate { get; set; }

    public List<TodoEntry> TodoEntries { get; set; } = [];

    public List<ClientIssueEntry> ClientIssues { get; set; } = [];

    public List<ScratchNoteUnclassifiedFragment> UnclassifiedFragments { get; set; } = [];
}