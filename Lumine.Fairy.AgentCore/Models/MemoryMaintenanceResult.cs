using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Models;

public sealed class MemoryMaintenanceResult
{
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public string TriggerReason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ExpiredFactsDeleted { get; set; }
    public int AgedOutEpisodesDeleted { get; set; }
    public int SchedulerExecutionEpisodesDeleted { get; set; }
    public int OverflowEpisodesDeleted { get; set; }
    public List<string> Notes { get; set; } = [];

    public int TotalDeletedCount => ExpiredFactsDeleted + AgedOutEpisodesDeleted + OverflowEpisodesDeleted;

    public string SummaryText => Status.ToLowerInvariant() switch
    {
        "disabled" => "长期记忆老化清理当前已禁用。",
        "skipped" when TotalDeletedCount == 0 => "本次未发现需要清理的长期记忆。",
        "skipped" => $"本次未执行新的老化清理，但最近一次待处理删除数为 {TotalDeletedCount}。",
        _ => $"已清理 {ExpiredFactsDeleted} 条过期事实、{AgedOutEpisodesDeleted} 条老化情节，额外裁剪 {OverflowEpisodesDeleted} 条超额低价值情节。"
    };
}