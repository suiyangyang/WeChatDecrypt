using System.Collections.Generic;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示子代理技能执行后的统一结果。
/// </summary>
public sealed class SubAgentSkillResult
{
    /// <summary>
    /// 获取或设置技能是否已处理当前请求。
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// 获取或设置技能执行是否成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 获取或设置返回给用户的主要文本回复。
    /// </summary>
    public string ReplyText { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置结构化结果负载，供上层流程进一步消费。
    /// </summary>
    public Dictionary<string, string> StructuredPayload { get; set; } = [];

    /// <summary>
    /// 获取或设置本次执行过程中发生的工具调用记录。
    /// </summary>
    public List<SubAgentToolCall> ToolCalls { get; set; } = [];

    /// <summary>
    /// 获取或设置当前结果是否需要用户进一步选择或确认。
    /// </summary>
    public bool NeedUserChoice { get; set; }

    /// <summary>
    /// 获取或设置待用户确认的文件操作描述。
    /// </summary>
    public PendingFileAction? PendingAction { get; set; }

    /// <summary>
    /// 获取或设置待用户确认的命令操作描述。
    /// </summary>
    public PendingCommandAction? PendingCommandAction { get; set; }

    /// <summary>
    /// 获取或设置待用户确认的桌面操作描述。
    /// </summary>
    public PendingDesktopAction? PendingDesktopAction { get; set; }

    /// <summary>
    /// 获取或设置桌面技能返回的已解析目标，供宿主在后续请求中继续复用。
    /// </summary>
    public UiTarget? ResolvedTarget { get; set; }

    /// <summary>
    /// 获取或设置错误代码；成功时通常为空字符串。
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置错误详情信息；成功时通常为空字符串。
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;
}