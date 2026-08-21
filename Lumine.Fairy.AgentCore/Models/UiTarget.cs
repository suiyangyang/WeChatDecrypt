namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一次桌面动作要寻找或操作的 UI 目标。
/// </summary>
public sealed class UiTarget
{
    /// <summary>
    /// 获取或设置目标所在窗口描述。
    /// </summary>
    public WindowDescriptor? Window { get; set; }

    /// <summary>
    /// 获取或设置目标控件或文本标签。
    /// </summary>
    public string ControlText { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置目标控件类型，例如按钮、输入框、菜单项。
    /// </summary>
    public string ControlType { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置目标桌面图标名称。
    /// </summary>
    public string DesktopIconName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置用于视觉匹配的 OCR 文本线索。
    /// </summary>
    public string OcrTextHint { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置允许搜索目标的屏幕区域。
    /// 第一版仅作为约束区域使用，不用于自由生成任意点击坐标。
    /// </summary>
    public ScreenRegion? SearchRegion { get; set; }

    /// <summary>
    /// 获取或设置目标经视觉/UIA 解析后的矩形区域。
    /// </summary>
    public ScreenRegion? ResolvedBounds { get; set; }

    /// <summary>
    /// 获取或设置与该目标关联的截图引用。
    /// </summary>
    public ScreenSnapshotRef? Snapshot { get; set; }

    /// <summary>
    /// 获取或设置目标匹配置信度。
    /// </summary>
    public double Confidence { get; set; }
}