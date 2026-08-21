using Lumine.Fairy.Models;

namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 为模块提供技能目录相关的默认值补回能力。
/// </summary>
public interface IModuleSkillDefaultsProvider
{
    /// <summary>
    /// 将模块自己的技能默认值补回到当前配置中。
    /// </summary>
    /// <param name="personaProfiles">当前角色配置集合。</param>
    /// <param name="manifests">当前已注册的技能清单。</param>
    void Apply(IList<PersonaProfile> personaProfiles, IReadOnlyList<SkillManifest> manifests);
}