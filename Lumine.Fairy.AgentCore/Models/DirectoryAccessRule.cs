namespace Lumine.Fairy.Models;

/// <summary>
/// 表示目录访问规则的优先级，数值越小优先级越高。
/// </summary>
public enum DirectoryPriority
{
    /// <summary>
    /// 高优先级规则，应优先参与匹配与排序。
    /// </summary>
    High = 0,

    /// <summary>
    /// 中优先级规则，适用于常规访问场景。
    /// </summary>
    Medium = 1,

    /// <summary>
    /// 低优先级规则，通常作为兜底策略。
    /// </summary>
    Low = 2
}

/// <summary>
/// 描述目录规则允许的访问模式。
/// </summary>
public enum DirectoryAccessMode
{
    /// <summary>
    /// 仅允许读取、打开和枚举。
    /// </summary>
    ReadOnly = 0,

    /// <summary>
    /// 允许读写与修改。
    /// </summary>
    ReadWrite = 1
}

/// <summary>
/// 描述某个目录的访问控制规则，包括路径、访问模式、可递归深度和优先级。
/// </summary>
public sealed class DirectoryAccessRule
{
    /// <summary>
    /// 获取或设置规则作用的根目录路径。
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置允许访问的最大递归深度。
    /// </summary>
    public int MaxDepth { get; set; } = 3;

    /// <summary>
    /// 获取或设置目录的访问模式，默认只读。
    /// </summary>
    public DirectoryAccessMode AccessMode { get; set; } = DirectoryAccessMode.ReadOnly;

    /// <summary>
    /// 获取或设置该规则在匹配冲突时的优先级。
    /// </summary>
    public DirectoryPriority Priority { get; set; } = DirectoryPriority.Medium;
}