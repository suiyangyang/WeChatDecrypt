using System;
using System.Collections.Generic;
using System.Linq;

namespace Lumine.Fairy.Models;

public sealed class MemoryRetrievedItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string MemoryType { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string SourceSessionId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public List<string> RankingReasons { get; init; } = [];
    public double BaseScore { get; init; }
    public double Score { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? $"[{MemoryType}]"
        : $"[{MemoryType}] {Title}";

    public bool HasVisualizationBadge => MemoryType switch
    {
        "work-context" => true,
        "scheduler-job" => true,
        "relation" => true,
        _ => false
    };

    public string VisualizationBadgeText => MemoryType switch
    {
        "work-context" => "工作上下文",
        "scheduler-job" => "定时任务",
        "relation" => "关系记忆",
        _ => string.Empty
    };

    public string VisualizationCardBackground => MemoryType switch
    {
        "work-context" => "#102235",
        "scheduler-job" => "#10291E",
        "relation" => "#2A1240",
        _ => "#0F172A"
    };

    public string VisualizationBadgeBackground => MemoryType switch
    {
        "work-context" => "#1D4ED8",
        "scheduler-job" => "#166534",
        "relation" => "#7C3AED",
        _ => "#1E293B"
    };

    public string VisualizationBadgeForeground => MemoryType switch
    {
        "work-context" => "#DBEAFE",
        "scheduler-job" => "#DCFCE7",
        "relation" => "#F3E8FF",
        _ => "#E2E8F0"
    };

    public string VisualizationTitleForeground => MemoryType switch
    {
        "work-context" => "#BFDBFE",
        "scheduler-job" => "#BBF7D0",
        "relation" => "#E9D5FF",
        _ => "#E2E8F0"
    };

    public string DisplayMeta
    {
        get
        {
            var parts = new List<string>
            {
                $"score={Score:F1}"
            };

            if (BaseScore > 0)
            {
                parts.Add($"base={BaseScore:F1}");
            }

            if (Tags.Count > 0)
            {
                parts.Add($"tags={string.Join('、', Tags.Take(4))}");
            }

            parts.Add(Timestamp.ToString("yyyy-MM-dd HH:mm"));
            return string.Join(" | ", parts);
        }
    }

    public string DisplayReasons => RankingReasons.Count == 0
        ? "暂无命中解释"
        : string.Join("；", RankingReasons);
}