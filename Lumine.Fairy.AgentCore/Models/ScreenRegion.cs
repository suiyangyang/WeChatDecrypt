namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 Windows 桌面坐标系中的矩形区域。
/// </summary>
public sealed class ScreenRegion
{
    /// <summary>
    /// 获取或设置区域左上角 X 坐标。
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// 获取或设置区域左上角 Y 坐标。
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// 获取或设置区域宽度。
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 获取或设置区域高度。
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 获取当前区域是否为空或无效。
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}