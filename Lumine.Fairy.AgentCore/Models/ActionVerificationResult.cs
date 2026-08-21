namespace Lumine.Fairy.Models;

/// <summary>
/// 表示桌面动作执行后的结果校验信息。
/// </summary>
public sealed class ActionVerificationResult
{
    /// <summary>
    /// 获取或设置执行前后是否成功找到目标。
    /// </summary>
    public bool TargetFound { get; set; }

    /// <summary>
    /// 获取或设置执行后的状态是否符合预期。
    /// </summary>
    public bool ExpectedStateReached { get; set; }

    /// <summary>
    /// 获取或设置校验失败原因。
    /// </summary>
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置对观察到状态的文字摘要。
    /// </summary>
    public string ObservedStateSummary { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置校验阶段命中的目标。
    /// </summary>
    public UiTarget? MatchedTarget { get; set; }

    /// <summary>
    /// 获取或设置校验阶段使用的截图引用。
    /// </summary>
    public ScreenSnapshotRef? Snapshot { get; set; }

    /// <summary>
    /// 获取或设置校验阶段的匹配置信度。
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 获取当前校验的结构化状态。
    /// </summary>
    public ActionVerificationStatus Status
    {
        get
        {
            if (IsEmpty())
            {
                return ActionVerificationStatus.NotRun;
            }

            if (Passed)
            {
                return ActionVerificationStatus.Passed;
            }

            if (!TargetFound)
            {
                return ActionVerificationStatus.TargetNotFound;
            }

            if (!ExpectedStateReached)
            {
                return ActionVerificationStatus.ExpectedStateNotReached;
            }

            return ActionVerificationStatus.Failed;
        }
    }

    /// <summary>
    /// 获取当前校验是否整体通过。
    /// </summary>
    public bool Passed => TargetFound && ExpectedStateReached && string.IsNullOrWhiteSpace(FailureReason);

    private bool IsEmpty()
    {
        return !TargetFound
            && !ExpectedStateReached
            && string.IsNullOrWhiteSpace(FailureReason)
            && string.IsNullOrWhiteSpace(ObservedStateSummary)
            && MatchedTarget is null
            && Snapshot is null
            && Confidence <= 0;
    }
}