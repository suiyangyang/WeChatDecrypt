namespace Lumine.Fairy.Models;

/// <summary>
/// 表示技能运行所需的一项权限声明。
/// </summary>
public sealed class SkillPermission
{
    /// <summary>
    /// 获取或设置权限代码，用于程序化判定授权能力。
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// 获取或设置权限的人类可读说明。
    /// </summary>
    public string Description { get; set; } = string.Empty;
}