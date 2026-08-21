namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 Shell 命令的风险等级。
/// </summary>
public enum ShellCommandRiskLevel
{
    /// <summary>
    /// 低风险命令，通常为只读查询，可直接执行。
    /// </summary>
    Safe = 0,

    /// <summary>
    /// 中风险命令，通常会产生有限副作用，建议进入确认流。
    /// </summary>
    Caution = 1,

    /// <summary>
    /// 高风险命令，可能造成删除、覆盖、安装或系统级变更，必须确认。
    /// </summary>
    Destructive = 2
}