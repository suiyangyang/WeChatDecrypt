using Lumine.Fairy.Models;
using Lumine.Fairy.Modules.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lumine.Fairy.Services;

/// <summary>
/// 提供 Skill 模块注册与查询能力。
/// </summary>
public sealed class ModuleRegistryService
{
    private readonly Dictionary<string, ISkillModule> _registeredModules = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册一个模块；若存在同名模块则用新模块覆盖。
    /// </summary>
    /// <param name="module">要注册的模块。</param>
    public void Register(ISkillModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _registeredModules[module.Manifest.Name] = module;
    }

    /// <summary>
    /// 批量注册多个模块。
    /// </summary>
    /// <param name="modules">模块集合。</param>
    public void RegisterRange(IEnumerable<ISkillModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var module in modules)
        {
            Register(module);
        }
    }

    /// <summary>
    /// 尝试按名称获取已注册模块。
    /// </summary>
    /// <param name="moduleName">模块名称。</param>
    /// <param name="module">输出模块实例。</param>
    /// <returns>找到时返回 <see langword="true" />；否则返回 <see langword="false" />。</returns>
    public bool TryGetModule(string moduleName, out ISkillModule? module)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            module = null;
            return false;
        }

        return _registeredModules.TryGetValue(moduleName.Trim(), out module);
    }

    /// <summary>
    /// 获取当前已注册的模块清单，并按排序值与名称排序。
    /// </summary>
    /// <returns>模块清单集合。</returns>
    public IReadOnlyList<SkillModuleManifest> GetRegisteredManifests() =>
        _registeredModules.Values
            .Select(x => x.Manifest)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// 获取当前所有已注册模块实例，并按排序值与名称排序。
    /// </summary>
    /// <returns>模块实例集合。</returns>
    public IReadOnlyList<ISkillModule> GetRegisteredModules() =>
        _registeredModules.Values
            .OrderBy(x => x.Manifest.SortOrder)
            .ThenBy(x => x.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}