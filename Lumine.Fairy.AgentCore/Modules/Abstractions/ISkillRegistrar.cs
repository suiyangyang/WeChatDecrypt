using Lumine.Fairy.Skills.Abstractions;

namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 提供模块向宿主注册技能的窄接口。
/// </summary>
public interface ISkillRegistrar
{
    /// <summary>
    /// 注册一个技能实例。
    /// </summary>
    /// <param name="skill">要注册的技能。</param>
    void Register(ISubAgentSkill skill);
}