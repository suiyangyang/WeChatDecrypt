using System.Collections.Generic;

namespace Lumine.Fairy.Models;

/// <summary>
/// 描述某个桌面程序的内容识别策略。
/// </summary>
public sealed class DesktopAppRecognitionProfile
{
    /// <summary>
    /// 获取或设置程序标识键。
    /// </summary>
    public string ProgramKey { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置可匹配的进程名别名。
    /// </summary>
    public List<string> ProcessAliases { get; set; } = [];

    /// <summary>
    /// 获取或设置可匹配的窗口标题别名。
    /// </summary>
    public List<string> TitleAliases { get; set; } = [];

    /// <summary>
    /// 获取或设置内容识别模式。
    /// </summary>
    public DesktopContentRecognitionMode ContentRecognitionMode { get; set; } = DesktopContentRecognitionMode.Auto;
}