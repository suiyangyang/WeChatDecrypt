using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Models;

public sealed class MemoryPromptDebugSnapshot
{
    public string SessionId { get; set; } = string.Empty;
    public string InputPreview { get; set; } = string.Empty;
    public List<string> InjectedSectionNames { get; set; } = [];
    public List<string> InjectedSources { get; set; } = [];
    public List<MemoryRetrievedItem> RetrievalCandidates { get; set; } = [];
    public List<MemoryRetrievedItem> RetrievedItems { get; set; } = [];
    public List<MemoryRetrievedItem> OmittedByQuotaItems { get; set; } = [];
    public List<MemoryRetrievedItem> OmittedByBudgetItems { get; set; } = [];
    public int RetrievedTopK { get; set; }
    public int RetrievedCharsBudget { get; set; }
    public int RetrievedMaxItemsPerType { get; set; }
    public int RetrievedSummaryCharsBudget { get; set; }
    public int RetrievedEpisodeCharsBudget { get; set; }
    public int RetrievedToolResultCharsBudget { get; set; }
    public int RetrievedDomainCharsBudget { get; set; }
    public string TruncationReason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
