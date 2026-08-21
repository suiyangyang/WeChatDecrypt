using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 DesktopOps 返回给宿主、等待用户确认后再执行的桌面动作。
/// </summary>
public sealed partial class PendingDesktopAction : ObservableObject
{
    /// <summary>
    /// 获取或设置产生该待确认动作的技能名称。
    /// </summary>
    public required string SourceSkillName { get; init; }

    /// <summary>
    /// 获取或设置人类可读的动作说明。
    /// </summary>
    public required string ActionLabel { get; init; }

    /// <summary>
    /// 获取或设置人类可读的目标摘要。
    /// </summary>
    public string TargetSummary { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置待确认的桌面动作请求快照。
    /// </summary>
    public required DesktopActionRequest Request { get; init; }

    /// <summary>
    /// 获取或设置触发确认的原因说明。
    /// </summary>
    public string ConfirmationMessage { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置本次动作的预期影响摘要。
    /// </summary>
    public string ExpectedImpact { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置回滚性说明。
    /// </summary>
    public string ReversibilityNote { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置微信等批处理类操作预计处理的项目数量。
    /// </summary>
    public int EstimatedItemCount { get; init; }

    /// <summary>
    /// 获取或设置是否会滚动读取历史聊天内容。
    /// </summary>
    public bool ReadsChatHistory { get; init; }

    /// <summary>
    /// 获取或设置是否允许调用外部视觉模型。
    /// </summary>
    public bool UsesExternalVision { get; init; }

    /// <summary>
    /// 获取或设置待确认操作的自定义载荷 JSON。
    /// </summary>
    public string CustomPayloadJson { get; init; } = string.Empty;

    /// <summary>
    /// 操作已被用户执行或取消，用于让按钮变灰。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    [NotifyPropertyChangedFor(nameof(ExecuteButtonText))]
    private bool _isDone;

    [ObservableProperty]
    private bool _isCancelled;

    /// <summary>
    /// 操作正在执行中，用于在确认卡片中展示活动指示器。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    [NotifyPropertyChangedFor(nameof(ExecuteButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowExecutingIndicator))]
    private bool _isExecuting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailToggleText))]
    [NotifyPropertyChangedFor(nameof(ToggleGlyph))]
    private bool _isDetailExpanded;

    /// <summary>
    /// 获取当前是否仍允许继续执行该待确认动作。
    /// </summary>
    public bool CanExecute => !IsDone && !IsExecuting;

    /// <summary>
    /// 获取执行按钮文案。
    /// </summary>
    public string ExecuteButtonText => IsExecuting ? "执行中..." : IsDone ? "已处理" : "确认执行桌面动作";

    /// <summary>
    /// 获取确认卡片标题。
    /// </summary>
    public string ConfirmationTitle => "确认执行桌面动作";

    /// <summary>
    /// 获取预计处理项目数量展示文本。
    /// </summary>
    public string EstimatedItemCountSummary => EstimatedItemCount > 0 ? $"{EstimatedItemCount} 个" : "未声明";

    /// <summary>
    /// 获取是否读取历史聊天的展示文本。
    /// </summary>
    public string ReadsChatHistorySummary => ReadsChatHistory ? "是" : "否";

    /// <summary>
    /// 获取是否调用外部视觉模型的展示文本。
    /// </summary>
    public string UsesExternalVisionSummary => UsesExternalVision ? "是" : "否";

    /// <summary>
    /// 获取是否显示执行中的活动指示器。
    /// </summary>
    public bool ShowExecutingIndicator => IsExecuting;

    /// <summary>
    /// 获取详情切换按钮文案。
    /// </summary>
    public string DetailToggleText => IsDetailExpanded ? "收起详情" : "展开详情";

    /// <summary>
    /// 获取详情切换箭头。
    /// </summary>
    public string ToggleGlyph => IsDetailExpanded ? "▴" : "▾";

    /// <summary>
    /// 获取供展示的动作类型文本。
    /// </summary>
    public string ActionKindLabel => Request.ActionKind switch
    {
        DesktopActionKind.LaunchApplication => "启动应用",
        DesktopActionKind.ActivateWindow => "激活窗口",
        DesktopActionKind.ClickTarget => "点击目标",
        DesktopActionKind.DoubleClickTarget => "双击目标",
        DesktopActionKind.EnterText => "输入文本",
        DesktopActionKind.SendHotkey => "发送快捷键",
        DesktopActionKind.SendCurrentInput => "发送当前输入",
        DesktopActionKind.ScrollTarget => "滚动目标",
        DesktopActionKind.CaptureScreen => "屏幕截图",
        DesktopActionKind.WaitForWindow => "等待窗口",
        _ => Request.ActionKind.ToString()
    };

    /// <summary>
    /// 获取供展示的风险等级文本。
    /// </summary>
    public string RiskLevelLabel => Request.RiskLevel switch
    {
        DesktopActionRiskLevel.ReadOnly => "只读",
        DesktopActionRiskLevel.LowRiskWrite => "低风险写操作",
        DesktopActionRiskLevel.DangerousWrite => "高风险写操作",
        _ => Request.RiskLevel.ToString()
    };

    /// <summary>
    /// 获取供展示的目标窗口摘要。
    /// </summary>
    public string TargetWindowSummary
    {
        get
        {
            var window = Request.Target?.Window ?? Request.CurrentWindow;
            if (window is null)
            {
                return "未指定窗口";
            }

            if (!string.IsNullOrWhiteSpace(window.Title))
            {
                return window.Title;
            }

            if (!string.IsNullOrWhiteSpace(window.ProcessName))
            {
                return window.ProcessName;
            }

            return "未命名窗口";
        }
    }

    /// <summary>
    /// 获取供展示的输入摘要。
    /// </summary>
    public string InputSummary
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Request.InputText))
            {
                return Request.InputText.Length <= 48
                    ? Request.InputText
                    : Request.InputText[..45] + "...";
            }

            if (!string.IsNullOrWhiteSpace(Request.KeyChord))
            {
                return Request.KeyChord;
            }

            if (!string.IsNullOrWhiteSpace(Request.LaunchArguments))
            {
                return Request.LaunchArguments;
            }

            if (Request.ScrollAmount != 0)
            {
                return $"滚动量：{Request.ScrollAmount}";
            }

            return "无";
        }
    }

    /// <summary>
    /// 获取供展示的预期结果摘要。
    /// </summary>
    public string ExpectedOutcomeSummary => string.IsNullOrWhiteSpace(Request.ExpectedOutcome)
        ? "未提供"
        : Request.ExpectedOutcome;

    /// <summary>
    /// 获取供展示的工作区文本。
    /// </summary>
    public string WorkingDirectoryDisplay => string.IsNullOrWhiteSpace(Request.WorkingDirectory)
        ? string.Empty
        : Request.WorkingDirectory;

    /// <summary>
    /// 获取供展示的超时时间文本。
    /// </summary>
    public string TimeoutDisplay => $"{Request.TimeoutSeconds} 秒";

    /// <summary>
    /// 获取供展示的底层服务摘要。
    /// </summary>
    public string RequiredServicesSummary => Request.RequiredServices.Count == 0
        ? "未声明"
        : string.Join(", ", Request.RequiredServices);
}