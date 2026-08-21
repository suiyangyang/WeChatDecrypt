namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一次 DesktopOps 动作执行后的结构化结果。
/// </summary>
public sealed class DesktopActionResult
{
    /// <summary>
    /// 获取或设置对应的动作请求标识。
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置本次执行对应的动作类型。
    /// </summary>
    public DesktopActionKind ActionKind { get; set; } = DesktopActionKind.CaptureScreen;

    /// <summary>
    /// 获取或设置最终判定的风险等级。
    /// </summary>
    public DesktopActionRiskLevel RiskLevel { get; set; } = DesktopActionRiskLevel.ReadOnly;

    /// <summary>
    /// 获取或设置动作执行是否成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 获取或设置动作是否已经真正下发执行。
    /// </summary>
    public bool Executed { get; set; }

    /// <summary>
    /// 获取或设置动作是否被策略阻止。
    /// </summary>
    public bool BlockedByPolicy { get; set; }

    /// <summary>
    /// 获取或设置动作是否因等待确认而未执行。
    /// </summary>
    public bool WaitingForConfirmation { get; set; }

    /// <summary>
    /// 获取或设置动作是否因超时失败。
    /// </summary>
    public bool TimedOut { get; set; }

    /// <summary>
    /// 获取或设置动作是否被取消。
    /// </summary>
    public bool Cancelled { get; set; }

    /// <summary>
    /// 获取或设置结果摘要消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置失败原因。
    /// </summary>
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置本次执行命中的目标。
    /// </summary>
    public UiTarget? ResolvedTarget { get; set; }

    /// <summary>
    /// 获取或设置执行后活跃窗口描述。
    /// </summary>
    public WindowDescriptor? ActiveWindow { get; set; }

    /// <summary>
    /// 获取或设置本次执行是否疑似受到前台锁或系统焦点限制影响。
    /// </summary>
    public bool ForegroundLockSuspected { get; set; }

    /// <summary>
    /// 获取或设置窗口激活阶段已尝试的策略列表。
    /// </summary>
    public List<string> AttemptedActivationStrategies { get; set; } = [];

    /// <summary>
    /// 获取或设置激活失败时观察到的前台窗口。
    /// </summary>
    public WindowDescriptor? ObservedForegroundWindow { get; set; }

    /// <summary>
    /// 获取或设置执行阶段或执行后的截图引用。
    /// </summary>
    public ScreenSnapshotRef? Snapshot { get; set; }

    /// <summary>
    /// 获取或设置动作校验结果。
    /// </summary>
    public ActionVerificationResult Verification { get; set; } = new();

    /// <summary>
    /// 获取当前动作结果的统一状态。
    /// </summary>
    public DesktopActionStatus Status
    {
        get
        {
            if (WaitingForConfirmation && !Executed)
            {
                return DesktopActionStatus.AwaitingConfirmation;
            }

            if (BlockedByPolicy)
            {
                return DesktopActionStatus.Blocked;
            }

            if (Cancelled)
            {
                return DesktopActionStatus.Cancelled;
            }

            if (TimedOut)
            {
                return DesktopActionStatus.TimedOut;
            }

            if (!Executed || !Success)
            {
                return DesktopActionStatus.Failed;
            }

            return Verification.Status is ActionVerificationStatus.NotRun or ActionVerificationStatus.Passed
                ? DesktopActionStatus.Succeeded
                : DesktopActionStatus.VerificationFailed;
        }
    }
}