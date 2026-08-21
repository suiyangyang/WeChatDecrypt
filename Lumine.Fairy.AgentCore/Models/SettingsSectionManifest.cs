namespace Lumine.Fairy.Models;

/// <summary>
/// 描述一个可挂载到宿主设置页中的动态设置分区。
/// </summary>
public sealed class SettingsSectionManifest
{
    /// <summary>
    /// 获取或设置分区唯一标识。
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 获取或设置分区标题。
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 获取或设置所属模块名称。
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置分区说明。
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置排序值，数值越小越靠前。
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// 获取或设置可选图标键。
    /// </summary>
    public string? IconKey { get; init; }
}