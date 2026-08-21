using System.Collections.Generic;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一次子代理内部发起的工具调用及其结果摘要。
/// </summary>
public sealed class SubAgentToolCall
{
    /// <summary>
    /// 获取或设置被调用的工具名称。
    /// </summary>
    public required string ToolName { get; set; }

    /// <summary>
    /// 获取或设置工具调用参数集合。
    /// </summary>
    public Dictionary<string, string> Arguments { get; set; } = [];

    /// <summary>
    /// 获取或设置工具调用是否成功。
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 获取或设置工具调用结果的摘要描述。
    /// </summary>
    public string ResultSummary { get; set; } = string.Empty;
}