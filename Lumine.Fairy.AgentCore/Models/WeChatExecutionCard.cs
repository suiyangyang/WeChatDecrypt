namespace Lumine.Fairy.Models;

/// <summary>
/// 表示微信批量抓取执行结果的结构化展示卡片。
/// </summary>
public sealed class WeChatExecutionCard
{
    public string StatusKey { get; init; } = string.Empty;

    public string StatusTitle { get; init; } = string.Empty;

    public string SummaryTitle { get; init; } = string.Empty;

    public string BadgeText { get; init; } = string.Empty;

    public string ProcessedSummary { get; init; } = string.Empty;

    public string OrderedConversationNamesText { get; init; } = string.Empty;

    public string CommonTopicsText { get; init; } = string.Empty;

    public string NeedsReviewText { get; init; } = string.Empty;

    public string EvidenceSummaryText { get; init; } = string.Empty;

    public string ExportJsonPath { get; init; } = string.Empty;

    public string EndedAtText { get; init; } = string.Empty;

    public string SummaryText { get; init; } = string.Empty;

    public string ConversationDigestText { get; init; } = string.Empty;

    public string FailureReason { get; init; } = string.Empty;

    public bool IsSucceededStatus => string.Equals(StatusKey, "Succeeded", System.StringComparison.OrdinalIgnoreCase);

    public bool IsPartialStatus => string.Equals(StatusKey, "Partial", System.StringComparison.OrdinalIgnoreCase);

    public bool IsCancelledStatus => string.Equals(StatusKey, "Cancelled", System.StringComparison.OrdinalIgnoreCase);

    public bool IsFailedStatus => string.Equals(StatusKey, "Failed", System.StringComparison.OrdinalIgnoreCase);

    public bool IsSuccessBadge => IsSucceededStatus;

    public bool IsWarningBadge => IsPartialStatus;

    public bool IsDangerBadge => IsFailedStatus;

    public bool IsNeutralBadge => IsCancelledStatus;

    public string CardBackground => IsCancelledStatus ? "#241A08" : "#0B1C17";

    public string CardBorderBrush => IsCancelledStatus ? "#F59E0B" : "#10B981";

    public string StatusTitleForeground => IsCancelledStatus ? "#FEF3C7" : "#ECFDF5";

    public string NeutralBadgeBackground => IsCancelledStatus ? "#3A2A05" : "#1F2937";

    public string NeutralBadgeBorderBrush => IsCancelledStatus ? "#F59E0B" : "#94A3B8";

    public string NeutralBadgeForeground => IsCancelledStatus ? "#FDE68A" : "#E5E7EB";

    public string MetricsPanelBackground => IsCancelledStatus ? "#2B200A" : "#052E16";

    public string MetricsPanelBorderBrush => IsCancelledStatus ? "#D97706" : "#059669";

    public string MetricsPrimaryForeground => IsCancelledStatus ? "#FEF3C7" : "#D1FAE5";

    public string MetricsSecondaryForeground => IsCancelledStatus ? "#FCD34D" : "#A7F3D0";

    public string MetricsMutedForeground => IsCancelledStatus ? "#FBBF24" : "#93C5FD";

    public string SummaryPanelBorderBrush => IsCancelledStatus ? "#92400E" : "#14532D";

    public string SummaryTitleForeground => IsCancelledStatus ? "#FCD34D" : "#A7F3D0";

    public string SummaryTextForeground => IsCancelledStatus ? "#FEF3C7" : "#E5E7EB";

    public string ExportJsonLabelForeground => IsCancelledStatus ? "#FCD34D" : "#86EFAC";

    public string ExportJsonPathForeground => IsCancelledStatus ? "#FDE68A" : "#93C5FD";

    public string ExportButtonBackground => IsCancelledStatus ? "#3A2A05" : "#0F2A24";

    public string ExportButtonForeground => IsCancelledStatus ? "#FEF3C7" : "#93C5FD";

    public string ExportButtonBorderBrush => IsCancelledStatus ? "#F59E0B" : "#60A5FA";

    public string FailureReasonForeground => IsCancelledStatus ? "#FDE68A" : "#FCA5A5";

    public bool HasOrderedConversationNames => !string.IsNullOrWhiteSpace(OrderedConversationNamesText);

    public bool HasCommonTopics => !string.IsNullOrWhiteSpace(CommonTopicsText);

    public bool HasNeedsReview => !string.IsNullOrWhiteSpace(NeedsReviewText);

    public bool HasEvidenceSummary => !string.IsNullOrWhiteSpace(EvidenceSummaryText);

    public bool HasExportJsonPath => !string.IsNullOrWhiteSpace(ExportJsonPath);

    public bool HasEndedAt => !string.IsNullOrWhiteSpace(EndedAtText);

    public bool HasSummaryText => !string.IsNullOrWhiteSpace(SummaryText);

    public bool HasConversationDigest => !string.IsNullOrWhiteSpace(ConversationDigestText);

    public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);
}