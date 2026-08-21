namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 ShellSkill 返回给宿主、等待用户确认后再执行的命令动作。
/// </summary>
public sealed class PendingShellAction
{
    /// <summary>
    /// 获取或设置产生该待确认动作的技能名称。
    /// </summary>
    public required string SourceSkillName { get; init; }

    /// <summary>
    /// 获取或设置人类可读的动作说明。
    /// </summary>
    public required string ActionLabel { get; init; }

    /// <summary>
    /// 获取或设置待确认的命令请求快照。
    /// </summary>
    public required ShellCommandRequest Request { get; init; }

    /// <summary>
    /// 获取或设置触发确认的原因说明。
    /// </summary>
    public string ConfirmationMessage { get; init; } = string.Empty;
}