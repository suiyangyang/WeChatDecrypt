namespace Lumine.Fairy.Models;

/// <summary>
/// 表示微信批量抓取待确认卡片的展示模型。
/// </summary>
public sealed class WeChatPendingActionCard
{
    public string Title { get; init; } = string.Empty;

    public string CompactNoticeText { get; init; } = string.Empty;

    public string PlanSummary { get; init; } = string.Empty;

    public string EstimatedConversationCountText { get; init; } = string.Empty;

    public string ConfiguredScanLimitText { get; init; } = string.Empty;

    public string ReadsChatHistoryText { get; init; } = string.Empty;

    public string UsesExternalVisionText { get; init; } = string.Empty;

    public string CaptureEvidenceText { get; init; } = string.Empty;

    public string VisibleContactsScopeText { get; init; } = string.Empty;

    public string SummaryModeText { get; init; } = string.Empty;

    public string MaxScrollRoundsText { get; init; } = string.Empty;

    public string TargetSummary { get; init; } = string.Empty;

    public string ExpectedImpact { get; init; } = string.Empty;

    public string ReversibilityNote { get; init; } = string.Empty;

    public bool HasTargetSummary => !string.IsNullOrWhiteSpace(TargetSummary);

    public bool HasExpectedImpact => !string.IsNullOrWhiteSpace(ExpectedImpact);

    public bool HasReversibilityNote => !string.IsNullOrWhiteSpace(ReversibilityNote);
}