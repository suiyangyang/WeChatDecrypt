namespace Lumine.Fairy.Models;

/// <summary>
/// 表示桌面动作后置校验的结构化状态。
/// </summary>
public enum ActionVerificationStatus
{
    /// <summary>
    /// 尚未执行或未产生有效校验信息。
    /// </summary>
    NotRun = 0,

    /// <summary>
    /// 目标命中且期望状态达成。
    /// </summary>
    Passed = 1,

    /// <summary>
    /// 未找到可验证目标。
    /// </summary>
    TargetNotFound = 2,

    /// <summary>
    /// 找到了目标，但期望状态未达成。
    /// </summary>
    ExpectedStateNotReached = 3,

    /// <summary>
    /// 校验过程失败，无法给出更细分结论。
    /// </summary>
    Failed = 4
}