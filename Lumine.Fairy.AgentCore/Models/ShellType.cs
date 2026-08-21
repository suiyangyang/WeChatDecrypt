namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 ShellSkill 当前支持的 Shell 类型。
/// </summary>
public enum ShellType
{
    /// <summary>
    /// Windows PowerShell 5.x（powershell.exe）。
    /// </summary>
    PowerShell = 0,

    /// <summary>
    /// Windows 命令提示符（cmd.exe）。
    /// </summary>
    Cmd = 1,

    /// <summary>
    /// PowerShell 7+（pwsh.exe）。
    /// </summary>
    Pwsh = 2
}