namespace Lumine.Fairy.Models;

/// <summary>
/// 表示桌面动作的风险等级。
/// </summary>
public enum DesktopActionRiskLevel
{
    /// <summary>
    /// 只读或仅观察类动作，可直接执行。
    /// </summary>
    ReadOnly = 0,

    /// <summary>
    /// 低风险写入动作，通常会带来有限副作用。
    /// </summary>
    LowRiskWrite = 1,

    /// <summary>
    /// 危险写入动作，可能造成不可逆或高影响变更，必须确认。
    /// </summary>
    DangerousWrite = 2
}