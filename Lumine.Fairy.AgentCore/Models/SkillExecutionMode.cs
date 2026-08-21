namespace Lumine.Fairy.Models;

/// <summary>
/// 表示技能的执行方式。
/// </summary>
public enum SkillExecutionMode
{
    /// <summary>
    /// 在宿主进程内直接执行。
    /// </summary>
    InProcess = 0,

    /// <summary>
    /// 在独立进程中执行，以便隔离崩溃或依赖环境。
    /// </summary>
    OutOfProcess = 1,

    /// <summary>
    /// 通过远程服务执行技能。
    /// </summary>
    Remote = 2
}