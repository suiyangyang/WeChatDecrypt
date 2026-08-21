namespace Lumine.Fairy.Models;

/// <summary>
/// 指定桌面内容识别优先采用的技术路径。
/// </summary>
public enum DesktopContentRecognitionMode
{
    /// <summary>
    /// 由调用方按程序能力自行决定。
    /// </summary>
    Auto = 0,

    /// <summary>
    /// 优先使用 UIA 识别内容。
    /// </summary>
    PreferUiAutomation = 1,

    /// <summary>
    /// 不使用 UIA 识别内容。
    /// </summary>
    DisableUiAutomation = 2,

    /// <summary>
    /// 优先使用视觉识别。
    /// </summary>
    PreferVision = 3,

    /// <summary>
    /// 仅使用视觉识别。
    /// </summary>
    VisionOnly = 4
}