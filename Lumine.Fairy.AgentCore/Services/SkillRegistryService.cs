using Lumine.Fairy.Models;
using Lumine.Fairy.Modules.Abstractions;
using Lumine.Fairy.Skills.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lumine.Fairy.Services;

/// <summary>
/// 提供技能注册与查询能力，用于维护当前宿主可用的子代理技能集合。
/// </summary>
public sealed class SkillRegistryService : ISkillRegistrar
{
    private readonly Dictionary<string, ISubAgentSkill> _registeredSkills = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册一个子代理技能；若同名技能已存在则会被新实例覆盖。
    /// </summary>
    /// <param name="skill">要注册的技能实例。</param>
    public void Register(ISubAgentSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        _registeredSkills[skill.Manifest.Name] = skill;
    }

    /// <summary>
    /// 批量注册多个子代理技能。
    /// </summary>
    /// <param name="skills">要注册的技能集合。</param>
    public void RegisterRange(IEnumerable<ISubAgentSkill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        foreach (var skill in skills)
        {
            Register(skill);
        }
    }

    /// <summary>
    /// 尝试按名称获取已注册的技能。
    /// </summary>
    /// <param name="skillName">技能名称。</param>
    /// <param name="skill">当返回 <see langword="true"/> 时输出匹配到的技能实例。</param>
    /// <returns>存在同名技能时返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    public bool TryGetSkill(string skillName, out ISubAgentSkill? skill)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            skill = null;
            return false;
        }

        return _registeredSkills.TryGetValue(skillName.Trim(), out skill);
    }

    /// <summary>
    /// 获取当前所有已注册技能的清单，并按名称排序。
    /// </summary>
    /// <returns>只读的技能清单集合。</returns>
    public IReadOnlyList<SkillManifest> GetRegisteredManifests() =>
        _registeredSkills.Values
            .Select(x => x.Manifest)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}