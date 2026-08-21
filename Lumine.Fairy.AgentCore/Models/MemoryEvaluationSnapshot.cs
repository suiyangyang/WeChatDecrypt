namespace Lumine.Fairy.Models;

public sealed class MemoryEvaluationSnapshot
{
    public int TotalPromptBuilds { get; set; }
    public int PromptBuildsWithRetrievedMemories { get; set; }
    public int TotalRetrievedItemsInjected { get; set; }
    public int LongTermWriteAttempts { get; set; }
    public int LongTermWritesBlocked { get; set; }
    public int ActiveFactCount { get; set; }
    public int RecentEpisodeCount { get; set; }
    public int MaintenanceRuns { get; set; }
    public int ExpiredFactsCleaned { get; set; }
    public int EpisodesCleaned { get; set; }

    public double RetrievalHitRate => TotalPromptBuilds == 0
        ? 0
        : (double)PromptBuildsWithRetrievedMemories / TotalPromptBuilds;
}
