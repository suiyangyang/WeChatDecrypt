namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一个 Windows 桌面窗口的最小描述信息。
/// </summary>
public sealed class WindowDescriptor
{
    /// <summary>
    /// 获取或设置窗口所属进程标识。
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// 获取或设置窗口标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置窗口所属进程名。
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置窗口类名。
    /// </summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置原生窗口句柄的字符串表示。
    /// </summary>
    public string NativeHandle { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置窗口在屏幕中的边界区域。
    /// </summary>
    public ScreenRegion? Bounds { get; set; }

    /// <summary>
    /// 获取或设置该窗口是否为桌面本身。
    /// </summary>
    public bool IsDesktopWindow { get; set; }

    /// <summary>
    /// 获取或设置该窗口当前是否处于前台。
    /// </summary>
    public bool IsForeground { get; set; }
}