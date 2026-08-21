using System.Collections.Generic;

namespace Lumine.Fairy.Models;

/// <summary>
/// 描述一个技能的静态元数据，用于注册、展示、调度和权限控制。
/// </summary>
public sealed class SkillManifest
{
    /// <summary>
    /// 获取或设置技能的内部唯一名称。
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// 获取或设置面向用户展示的技能名称。
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置技能说明文本。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置技能版本号。
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// 获取或设置技能所遵循的宿主 API 版本。
    /// </summary>
    public string ApiVersion { get; set; } = "1.0";

    /// <summary>
    /// 获取或设置技能类型。
    /// </summary>
    public SkillKind Kind { get; set; } = SkillKind.SubAgent;

    /// <summary>
    /// 获取或设置技能执行模式。
    /// </summary>
    public SkillExecutionMode ExecutionMode { get; set; } = SkillExecutionMode.InProcess;

    /// <summary>
    /// 获取或设置技能是否启用。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取或设置技能在宿主中的默认排序值，越小越靠前。
    /// </summary>
    public int SortOrder { get; set; } = int.MaxValue;

    /// <summary>
    /// 获取或设置技能入口标识，例如程序集入口、命令路径或远程地址。
    /// </summary>
    public string Entry { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置技能专属系统提示词，用于约束其行为。
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置技能单次执行的超时时间（秒）。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 获取或设置技能允许的最大并发数。
    /// </summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// 获取或设置技能声明的权限列表。
    /// </summary>
    public List<SkillPermission> Permissions { get; set; } = [];

    /// <summary>
    /// 获取或设置技能标签集合，便于分组、筛选和检索。
    /// </summary>
    public List<string> Tags { get; set; } = [];
}