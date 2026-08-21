using Lumine.Fairy.Models;
using Lumine.Fairy.Modules.Abstractions;

namespace Lumine.Fairy.Modules;

/// <summary>
/// 为固定内置角色补回模块声明的默认技能绑定。
/// </summary>
public sealed class FixedBuiltInPersonaSkillDefaultsProvider : IModuleSkillDefaultsProvider
{
    private readonly IReadOnlyList<string> _skillNames;

    public FixedBuiltInPersonaSkillDefaultsProvider(IEnumerable<string> skillNames)
    {
        _skillNames = (skillNames ?? [])
            .Where(static skillName => !string.IsNullOrWhiteSpace(skillName))
            .Select(static skillName => skillName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Apply(IList<PersonaProfile> personaProfiles, IReadOnlyList<SkillManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(personaProfiles);
        ArgumentNullException.ThrowIfNull(manifests);

        if (_skillNames.Count == 0)
        {
            return;
        }

        var builtInPersona = personaProfiles.FirstOrDefault(x =>
            x.IsFixed && string.Equals(x.Name, "桌面助手", StringComparison.OrdinalIgnoreCase));
        if (builtInPersona is null)
        {
            return;
        }

        builtInPersona.BoundSkillNames ??= [];
        foreach (var skillName in _skillNames)
        {
            if (!manifests.Any(manifest => string.Equals(manifest.Name, skillName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!builtInPersona.BoundSkillNames.Any(existing => string.Equals(existing, skillName, StringComparison.OrdinalIgnoreCase)))
            {
                builtInPersona.BoundSkillNames.Add(skillName);
            }
        }

        builtInPersona.BoundSkillNames = builtInPersona.BoundSkillNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name =>
            {
                var manifest = manifests.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                return manifest?.SortOrder ?? int.MaxValue;
            })
            .ThenBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}