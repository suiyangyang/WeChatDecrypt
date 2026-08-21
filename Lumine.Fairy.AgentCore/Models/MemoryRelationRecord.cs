using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Models;

public sealed class MemoryRelationRecord
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string SourceMemoryType { get; init; } = string.Empty;
    public string SourceRecordId { get; init; } = string.Empty;
    public string SourceSessionId { get; init; } = string.Empty;
    public string HeadEntity { get; init; } = string.Empty;
    public string RelationType { get; init; } = string.Empty;
    public string TailEntity { get; init; } = string.Empty;
    public string EvidenceText { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public double Confidence { get; init; } = 0.75;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string DisplayTitle => string.IsNullOrWhiteSpace(TailEntity)
        ? $"{HeadEntity} {RelationType}".Trim()
        : $"{HeadEntity} {RelationType} {TailEntity}".Trim();
}
