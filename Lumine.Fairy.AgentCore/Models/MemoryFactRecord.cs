using System;

namespace Lumine.Fairy.Models;

public sealed class MemoryFactRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Scope { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsUserConfirmed { get; set; }
    public bool IsPinned { get; set; }
    public bool IsFrozen { get; set; }
    public bool IsMuted { get; set; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Key)
        ? "未命名事实"
        : $"[{Category}] {Key}";

    public bool HasGovernanceStatus => IsPinned || IsFrozen || IsMuted;

    public string GovernanceStatusText
    {
        get
        {
            var statuses = new System.Collections.Generic.List<string>();
            if (IsPinned)
            {
                statuses.Add("Pin");
            }

            if (IsFrozen)
            {
                statuses.Add("Freeze");
            }

            if (IsMuted)
            {
                statuses.Add("Mute");
            }

            return statuses.Count == 0
                ? "普通"
                : string.Join(" / ", statuses);
        }
    }

    public string PinToggleText => IsPinned ? "取消 Pin" : "Pin";
    public string FreezeToggleText => IsFrozen ? "取消 Freeze" : "Freeze";
    public string MuteToggleText => IsMuted ? "取消 Mute" : "Mute";

    public string DisplayMeta
    {
        get
        {
            var confirmedText = IsUserConfirmed ? "已确认" : "未确认";
            var expiryText = ExpiresAt is null
                ? "长期"
                : $"到期 {ExpiresAt:MM-dd HH:mm}";
            return $"置信度={Confidence:F2} | 来源={Source} | {confirmedText} | 状态={GovernanceStatusText} | {expiryText} | 更新于 {UpdatedAt:yyyy-MM-dd HH:mm}";
        }
    }
}