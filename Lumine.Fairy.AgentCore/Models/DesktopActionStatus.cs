namespace Lumine.Fairy.Models;

/// <summary>
/// 表示桌面动作执行后的统一结果状态。
/// </summary>
public enum DesktopActionStatus
{
    /// <summary>
    /// 动作等待用户确认，尚未真正执行。
    /// </summary>
    AwaitingConfirmation = 0,

    /// <summary>
    /// 动作执行完成，且后置校验通过。
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// 动作已执行，但后置校验未通过。
    /// </summary>
    VerificationFailed = 2,

    /// <summary>
    /// 动作执行失败。
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 动作被策略阻止，未真正执行。
    /// </summary>
    Blocked = 4,

    /// <summary>
    /// 动作执行超时。
    /// </summary>
    TimedOut = 5,

    /// <summary>
    /// 动作执行被取消。
    /// </summary>
    Cancelled = 6
}