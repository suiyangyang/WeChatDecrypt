using System;

namespace Lumine.Fairy.Models;

public sealed class MemoryWriteAuditRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MemoryType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ValuePreview { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string TriggerReason { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string DecisionText => string.IsNullOrWhiteSpace(Decision)
        ? "unknown"
        : Decision.ToUpperInvariant();

    public string DecisionColor => Decision.ToLowerInvariant() switch
    {
        "allowed" => "#22C55E",
        "cleaned" => "#38BDF8",
        "blocked" => "#F97316",
        "deleted" => "#A78BFA",
        "skipped" => "#FBBF24",
        _ => "#94A3B8"
    };

    public string DisplayMeta => $"{MemoryType}/{Action} | {Source} | {CreatedAt:yyyy-MM-dd HH:mm:ss}";
}
