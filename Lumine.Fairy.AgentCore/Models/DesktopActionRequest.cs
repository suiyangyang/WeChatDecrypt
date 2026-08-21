using System;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一次结构化 DesktopOps 动作请求。
/// </summary>
public sealed class DesktopActionRequest
{
    /// <summary>
    /// 获取或设置动作请求唯一标识。
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 获取或设置动作类型。
    /// </summary>
    public DesktopActionKind ActionKind { get; set; } = DesktopActionKind.CaptureScreen;

    /// <summary>
    /// 获取或设置截图类动作是否应强制使用全屏截图。
    /// 为 false 时由执行层按上下文做保守推断，通常优先当前窗口。
    /// </summary>
    public bool CaptureFullScreen { get; set; }

    /// <summary>
    /// 获取或设置动作风险等级。
    /// </summary>
    public DesktopActionRiskLevel RiskLevel { get; set; } = DesktopActionRiskLevel.ReadOnly;

    /// <summary>
    /// 获取或设置动作目标描述。
    /// </summary>
    public UiTarget? Target { get; set; }

    /// <summary>
    /// 获取或设置待启动的应用名称。
    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置待启动应用的显式路径。
    /// </summary>
    public string ApplicationPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置应用启动参数。
    /// </summary>
    public string LaunchArguments { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置待输入文本。
    /// </summary>
    public string InputText { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置快捷键组合，例如 Ctrl+S。
    /// </summary>
    public string KeyChord { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置滚动幅度；正负方向由后续执行层解释。
    /// </summary>
    public int ScrollAmount { get; set; }

    /// <summary>
    /// 获取或设置当前工作区或上下文目录。
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置动作发起时的当前窗口。
    /// </summary>
    public WindowDescriptor? CurrentWindow { get; set; }

    /// <summary>
    /// 获取或设置动作发起时所在的代理模式。
    /// </summary>
    public AgentMode CurrentMode { get; set; } = AgentMode.Work;

    /// <summary>
    /// 获取或设置生成该结构化动作的规划器名称。
    /// </summary>
    public string PlannerName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置本次动作依赖的底层服务名称列表。
    /// </summary>
    public List<string> RequiredServices { get; set; } = [];

    /// <summary>
    /// 获取或设置期望执行后观察到的结果摘要。
    /// </summary>
    public string ExpectedOutcome { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置动作超时时间（秒）。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// 获取或设置失败后的最大重试次数。
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 获取或设置该动作是否要求进入确认流。
    /// </summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>
    /// 获取或设置该动作是否已经被用户确认。
    /// </summary>
    public bool ConfirmedByUser { get; set; }
}