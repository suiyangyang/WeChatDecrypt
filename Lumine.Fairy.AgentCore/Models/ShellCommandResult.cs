namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一次 Shell 命令执行后的结构化结果。
/// </summary>
public sealed class ShellCommandResult
{
    /// <summary>
    /// 获取或设置对应的命令请求标识。
    /// </summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置实际执行所使用的 Shell 类型。
    /// </summary>
    public ShellType ShellType { get; set; } = ShellType.PowerShell;

    /// <summary>
    /// 获取或设置命令执行是否成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 获取或设置命令执行状态。
    /// </summary>
    public ShellCommandStatus Status { get; set; } = ShellCommandStatus.Failed;

    /// <summary>
    /// 获取或设置进程退出码。
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// 获取或设置标准输出内容。
    /// </summary>
    public string StandardOutput { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置标准错误内容。
    /// </summary>
    public string StandardError { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置合并后的输出内容，便于上层直接展示或总结。
    /// </summary>
    public string CombinedOutput { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置命令是否因超时而结束。
    /// </summary>
    public bool TimedOut { get; set; }

    /// <summary>
    /// 获取或设置命令是否被外部取消。
    /// </summary>
    public bool Cancelled { get; set; }

    /// <summary>
    /// 获取或设置命令是否被策略阻止执行。
    /// </summary>
    public bool BlockedByPolicy { get; set; }

    /// <summary>
    /// 获取或设置命令输出是否已被裁剪。
    /// </summary>
    public bool OutputTruncated { get; set; }

    /// <summary>
    /// 获取或设置结构化失败原因，便于上层决定是否展示确认或重试。
    /// </summary>
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置命令执行工作目录。
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置用于展示的命令预览文本。
    /// </summary>
    public string ExecutedCommandPreview { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置执行判定后的风险等级。
    /// </summary>
    public ShellCommandRiskLevel RiskLevel { get; set; } = ShellCommandRiskLevel.Safe;
}