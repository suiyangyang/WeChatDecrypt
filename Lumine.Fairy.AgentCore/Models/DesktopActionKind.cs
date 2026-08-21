namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 DesktopOps 第一版支持的桌面动作类型。
/// </summary>
public enum DesktopActionKind
{
    /// <summary>
    /// 打开指定应用。
    /// </summary>
    LaunchApplication = 0,

    /// <summary>
    /// 激活已存在的窗口。
    /// </summary>
    ActivateWindow = 1,

    /// <summary>
    /// 单击已解析出的 UI 目标。
    /// </summary>
    ClickTarget = 2,

    /// <summary>
    /// 双击已解析出的 UI 目标。
    /// </summary>
    DoubleClickTarget = 3,

    /// <summary>
    /// 向目标输入文本。
    /// </summary>
    EnterText = 4,

    /// <summary>
    /// 发送快捷键组合。
    /// </summary>
    SendHotkey = 5,

    /// <summary>
    /// 对目标区域执行滚动。
    /// </summary>
    ScrollTarget = 6,

    /// <summary>
    /// 捕获屏幕或窗口截图。
    /// </summary>
    CaptureScreen = 7,

    /// <summary>
    /// 等待目标窗口出现或进入前台。
    /// </summary>
    WaitForWindow = 8,

    /// <summary>
    /// 提交当前输入内容，例如发送聊天输入框中的文本。
    /// </summary>
    SendCurrentInput = 9
}