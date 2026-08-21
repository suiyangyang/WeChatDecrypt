using Lumine.Fairy.Models;
using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Services;

public sealed class WorkspaceIndexMatchResult
{
    public required string Path { get; init; }
    public bool IsDirectory { get; init; }
    public double Confidence { get; init; }
    public required WorkspaceIndexMatchFeatures Features { get; init; }
}

public sealed class WorkspaceIndexMatchFeatures
{
    public double TextScore { get; init; }
    public double PinyinScore { get; init; }
    public double RecencyScore { get; init; }
    public double FrequencyScore { get; init; }
}

public sealed class WorkspaceIndexMatcherStatsSnapshot
{
    public double TextWeight { get; init; }
    public double RecencyWeight { get; init; }
    public double FrequencyWeight { get; init; }
    public int RecentFeedbackCount { get; init; }
    public double RecentSuccessRate { get; init; }
    public required IReadOnlyList<WorkspaceIndexFeedbackEntry> RecentFeedbacks { get; init; }
}

public sealed class WorkspaceIndexFeedbackEntry
{
    public required string Path { get; init; }
    public bool Success { get; init; }
    public DateTime CreatedUtc { get; init; }
    public double Confidence { get; init; }
}

public sealed class LlmDeepMatchResult
{
    public required List<LlmDeepMatchItem> Matches { get; init; }
    public required string RawReply { get; init; }
    public bool HasMatches => Matches.Count > 0;
}

public sealed class LlmDeepMatchItem
{
    public required string Path { get; init; }
    public double Confidence { get; init; }
}

public sealed class LlmDeepMatchCandidate
{
    public required string Path { get; init; }
    public bool IsDirectory { get; init; }
    public DirectoryPriority Priority { get; init; } = DirectoryPriority.Medium;
}
