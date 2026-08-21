namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 标记一个可供其他模块或技能使用的能力契约。
/// </summary>
public interface ISkillCapability
{
    /// <summary>
    /// 获取能力名称。
    /// </summary>
    string Name { get; }
}