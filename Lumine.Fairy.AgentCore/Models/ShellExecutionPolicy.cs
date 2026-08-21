namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 Shell 执行的最小策略配置。
/// </summary>
public sealed class ShellExecutionPolicy
{
    /// <summary>
    /// 获取或设置默认使用的 Shell 类型。
    /// </summary>
    public ShellType DefaultShellType { get; set; } = ShellType.PowerShell;

    /// <summary>
    /// 获取或设置默认超时时间（秒）。
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 获取或设置允许的最大超时时间（秒）。
    /// </summary>
    public int MaxTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 获取或设置从哪个风险等级开始需要确认。
    /// </summary>
    public ShellCommandRiskLevel ConfirmationRiskLevel { get; set; } = ShellCommandRiskLevel.Caution;

    /// <summary>
    /// 获取或设置单路输出的最大保留字符数。
    /// </summary>
    public int MaxOutputChars { get; set; } = 12000;
}