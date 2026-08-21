using System;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示子代理技能在执行过程中发出的事件。
/// </summary>
public sealed class SubAgentSkillEvent
{
    /// <summary>
    /// 获取或设置事件类型。
    /// </summary>
    public SubAgentSkillEventKind Kind { get; set; } = SubAgentSkillEventKind.Status;

    /// <summary>
    /// 获取或设置事件附带的文本消息。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置事件产生的时间戳（UTC）。
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 获取或设置与当前事件相关的工具调用信息。
    /// </summary>
    public SubAgentToolCall? ToolCall { get; set; }

    /// <summary>
    /// 获取或设置需要用户确认的待执行文件操作。
    /// </summary>
    public PendingFileAction? PendingAction { get; set; }

    /// <summary>
    /// 获取或设置需要用户确认的待执行命令操作。
    /// </summary>
    public PendingCommandAction? PendingCommandAction { get; set; }

    /// <summary>
    /// 获取或设置需要用户确认的待执行桌面操作。
    /// </summary>
    public PendingDesktopAction? PendingDesktopAction { get; set; }

    /// <summary>
    /// 获取或设置微信扫描进度卡片数据。
    /// </summary>
    public WeChatScanProgressCard? WeChatScanProgressCard { get; set; }
}