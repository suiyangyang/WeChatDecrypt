using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 ShellSkill 返回给宿主、等待用户确认后再执行的命令动作。
/// </summary>
public sealed partial class PendingCommandAction : ObservableObject
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
    /// 获取或设置待确认的命令请求快照。
    /// </summary>
    public required ShellCommandRequest Request { get; init; }

    /// <summary>
    /// 获取或设置触发确认的原因说明。
    /// </summary>
    public string ConfirmationMessage { get; init; } = string.Empty;

    /// <summary>
    /// 操作已被用户执行或取消，用于让按钮变灰。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    [NotifyPropertyChangedFor(nameof(ExecuteButtonText))]
    [NotifyPropertyChangedFor(nameof(CommandPreview))]
    private bool _isDone;

    /// <summary>
    /// 命令正在执行中，用于在确认卡片中展示活动指示器。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    [NotifyPropertyChangedFor(nameof(ExecuteButtonText))]
    [NotifyPropertyChangedFor(nameof(ShowExecutingIndicator))]
    private bool _isExecuting;

    /// <summary>
    /// 获取当前是否仍允许继续执行该待确认命令。
    /// </summary>
    public bool CanExecute => !IsDone && !IsExecuting;

    /// <summary>
    /// 获取执行按钮文案。
    /// </summary>
    public string ExecuteButtonText => IsExecuting ? "执行中..." : IsDone ? "已处理" : "确认执行命令";

    /// <summary>
    /// 获取确认卡片标题。
    /// </summary>
    public string ConfirmationTitle => "确认执行命令";

    /// <summary>
    /// 获取是否显示执行中的活动指示器。
    /// </summary>
    public bool ShowExecutingIndicator => IsExecuting;

    /// <summary>
    /// 获取供展示的 Shell 类型文本。
    /// </summary>
    public string ShellTypeLabel => Request.ShellType switch
    {
        ShellType.PowerShell => "Windows PowerShell",
        ShellType.Cmd => "命令提示符",
        ShellType.Pwsh => "PowerShell 7+",
        _ => Request.ShellType.ToString()
    };

    /// <summary>
    /// 获取供展示的命令预览文本。
    /// </summary>
    public string CommandPreview => Request.CommandText.Length <= 120
        ? Request.CommandText
        : Request.CommandText[..117] + "...";

    /// <summary>
    /// 获取供展示的工作目录文本。
    /// </summary>
    public string WorkingDirectoryDisplay => string.IsNullOrWhiteSpace(Request.WorkingDirectory)
        ? string.Empty
        : Request.WorkingDirectory;

    /// <summary>
    /// 获取供展示的超时时间文本。
    /// </summary>
    public string TimeoutDisplay => $"{Request.TimeoutSeconds} 秒";

    /// <summary>
    /// 获取供展示的风险等级文本。
    /// </summary>
    public string RiskLevelLabel => Request.RiskLevel switch
    {
        ShellCommandRiskLevel.Safe => "低风险",
        ShellCommandRiskLevel.Caution => "需确认",
        ShellCommandRiskLevel.Destructive => "高风险",
        _ => Request.RiskLevel.ToString()
    };
}
