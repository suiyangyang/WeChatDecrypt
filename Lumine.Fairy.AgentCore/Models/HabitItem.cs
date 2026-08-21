using System;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示用户的文件或目录使用习惯项，用于根据历史访问行为优化推荐。
/// </summary>
public sealed class HabitItem
{
    /// <summary>
    /// 获取或设置习惯项显示名称。
    /// </summary>
    public required string Label { get; set; }

    /// <summary>
    /// 获取或设置习惯项对应的路径。
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// 获取或设置该路径被打开的累计次数。
    /// </summary>
    public int OpenCount { get; set; }

    /// <summary>
    /// 获取或设置最近一次访问时间。
    /// </summary>
    public DateTime LastAccessTime { get; set; } = DateTime.MinValue;
}
