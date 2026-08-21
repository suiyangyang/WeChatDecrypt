namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 Shell 命令执行后的结构化展示信息。
/// </summary>
public sealed class ShellExecutionCard
{
    /// <summary>
    /// 获取或设置状态标题。
    /// </summary>
    public string StatusTitle { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置命令文本。
    /// </summary>
    public string CommandText { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置工作目录。
    /// </summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置退出码文本。
    /// </summary>
    public string ExitCodeText { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置输出标题。
    /// </summary>
    public string OutputTitle { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置输出内容。
    /// </summary>
    public string OutputText { get; init; } = string.Empty;

    /// <summary>
    /// 获取或设置是否成功。
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 获取是否存在工作目录。
    /// </summary>
    public bool HasWorkingDirectory => !string.IsNullOrWhiteSpace(WorkingDirectory);

    /// <summary>
    /// 获取是否存在输出。
    /// </summary>
    public bool HasOutput => !string.IsNullOrWhiteSpace(OutputText);

    /// <summary>
    /// 获取供界面展示的输出文本。
    /// </summary>
    public string DisplayOutput => HasOutput ? OutputText : "(无输出)";
}