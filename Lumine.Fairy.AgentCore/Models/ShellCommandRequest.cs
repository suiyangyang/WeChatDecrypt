using System;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一次结构化 Shell 命令执行请求。
/// </summary>
public sealed class ShellCommandRequest
{
    /// <summary>
    /// 获取或设置命令请求唯一标识。
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 获取或设置目标 Shell 类型。
    /// </summary>
    public ShellType ShellType { get; set; } = ShellType.PowerShell;

    /// <summary>
    /// 获取或设置待执行的命令文本。
    /// </summary>
    public string CommandText { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置命令执行工作目录。
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置命令超时时间（秒）。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 获取或设置该命令是否要求在执行前进入确认流。
    /// </summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>
    /// 获取或设置命令风险等级。
    /// </summary>
    public ShellCommandRiskLevel RiskLevel { get; set; } = ShellCommandRiskLevel.Safe;

    /// <summary>
    /// 获取或设置命令是否属于只读操作。
    /// </summary>
    public bool IsReadOnly { get; set; } = true;

    /// <summary>
    /// 获取或设置该命令是否已由用户完成确认。
    /// </summary>
    public bool ConfirmedByUser { get; set; }
}