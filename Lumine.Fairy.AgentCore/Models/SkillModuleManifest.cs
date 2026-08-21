using System.Collections.Generic;

namespace Lumine.Fairy.Models;

/// <summary>
/// 描述一个 Skill 模块的静态元数据，用于展示、排序与模块治理。
/// </summary>
public sealed class SkillModuleManifest
{
    /// <summary>
    /// 获取或设置模块的内部唯一名称。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 获取或设置模块的展示名称。
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置模块说明。
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置模块版本号。
    /// </summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// 获取或设置是否为宿主内置模块。
    /// </summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>
    /// 获取或设置模块在导航或清单中的排序值，数值越小越靠前。
    /// </summary>
    public int SortOrder { get; init; }

    /// <summary>
    /// 获取或设置该模块对应的配置分区键。
    /// </summary>
    public string ConfigSectionKey { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置模块标签集合，便于分组和检索。
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}