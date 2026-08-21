using System.Collections.Generic;

namespace Lumine.Fairy.Models;

public sealed class MemoryDashboardSnapshot
{
    public List<MemoryConversationSummary> RecentSummaries { get; set; } = [];
    public List<MemoryTaskState> RecentTaskStates { get; set; } = [];
    public List<MemoryFactRecord> ActiveFacts { get; set; } = [];
    public List<MemoryEpisodeRecord> RecentEpisodes { get; set; } = [];
    public List<MemoryRelationRecord> RecentRelations { get; set; } = [];
    public List<MemoryWriteAuditRecord> RecentAudits { get; set; } = [];
    public MemoryPromptDebugSnapshot? LatestPromptDebug { get; set; }
    public MemoryMaintenanceResult? LatestMaintenanceResult { get; set; }
}
