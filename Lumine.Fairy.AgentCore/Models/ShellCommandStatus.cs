namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 Shell 命令执行后的结构化状态。
/// </summary>
public enum ShellCommandStatus
{
    /// <summary>
    /// 命令执行成功并正常退出。
    /// </summary>
    Succeeded = 0,

    /// <summary>
    /// 命令已启动，但退出码或执行过程表明失败。
    /// </summary>
    Failed = 1,

    /// <summary>
    /// 命令执行被策略阻止，未真正启动进程。
    /// </summary>
    Blocked = 2,

    /// <summary>
    /// 命令执行超时，被主动终止。
    /// </summary>
    TimedOut = 3,

    /// <summary>
    /// 命令执行被外部取消，被主动终止。
    /// </summary>
    Cancelled = 4
}
