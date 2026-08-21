using System;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示桌面动作执行后的结构化展示信息。
/// </summary>
public sealed class DesktopExecutionCard
{
    public string StatusKey { get; init; } = string.Empty;

    public string StatusTitle { get; init; } = string.Empty;

    public string SummaryTitle { get; init; } = string.Empty;

    public string BadgeText { get; init; } = string.Empty;

    public bool IsSuccess { get; init; }

    public string ActionKindText { get; init; } = string.Empty;

    public string ExecutionStatusText { get; init; } = string.Empty;

    public string VerificationStatusText { get; init; } = string.Empty;

    public string StatusDetail { get; init; } = string.Empty;

    public string TargetWindow { get; init; } = string.Empty;

    public string TargetSummary { get; init; } = string.Empty;

    public string RiskLevelText { get; init; } = string.Empty;

    public string VerificationSummary { get; init; } = string.Empty;

    public string FailureReason { get; init; } = string.Empty;

    public string ActivationDiagnostic { get; init; } = string.Empty;

    public string ActivationStrategiesText { get; init; } = string.Empty;

    public string SnapshotPath { get; init; } = string.Empty;

    public string ConfidenceText { get; init; } = string.Empty;

    public bool IsSucceededStatus => string.Equals(StatusKey, "Succeeded", StringComparison.OrdinalIgnoreCase);

    public bool IsVerificationFailedStatus => string.Equals(StatusKey, "VerificationFailed", StringComparison.OrdinalIgnoreCase);

    public bool IsBlockedStatus => string.Equals(StatusKey, "Blocked", StringComparison.OrdinalIgnoreCase);

    public bool IsTimedOutStatus => string.Equals(StatusKey, "TimedOut", StringComparison.OrdinalIgnoreCase);

    public bool IsCancelledStatus => string.Equals(StatusKey, "Cancelled", StringComparison.OrdinalIgnoreCase);

    public bool IsAwaitingConfirmationStatus => string.Equals(StatusKey, "AwaitingConfirmation", StringComparison.OrdinalIgnoreCase);

    public bool IsFailedStatus => string.Equals(StatusKey, "Failed", StringComparison.OrdinalIgnoreCase);

    public bool IsSuccessBadge => IsSucceededStatus;

    public bool IsWarningBadge => IsVerificationFailedStatus || IsTimedOutStatus;

    public bool IsDangerBadge => IsBlockedStatus || IsFailedStatus;

    public bool IsNeutralBadge => IsCancelledStatus;

    public bool IsInfoBadge => IsAwaitingConfirmationStatus;

    public bool HasVerificationStatus => !string.IsNullOrWhiteSpace(VerificationStatusText);

    public bool HasStatusDetail => !string.IsNullOrWhiteSpace(StatusDetail);

    public bool HasTargetWindow => !string.IsNullOrWhiteSpace(TargetWindow);

    public bool HasTargetSummary => !string.IsNullOrWhiteSpace(TargetSummary);

    public bool HasVerificationSummary => !string.IsNullOrWhiteSpace(VerificationSummary);

    public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);

    public bool HasActivationDiagnostic => !string.IsNullOrWhiteSpace(ActivationDiagnostic);

    public bool HasActivationStrategies => !string.IsNullOrWhiteSpace(ActivationStrategiesText);

    public bool HasSnapshotPath => !string.IsNullOrWhiteSpace(SnapshotPath);

    public bool HasConfidence => !string.IsNullOrWhiteSpace(ConfidenceText);
}