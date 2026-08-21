using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示提交给子代理技能的一次请求。
/// </summary>
public sealed class SubAgentSkillRequest
{
    /// <summary>
    /// 获取或设置请求唯一标识。
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 获取或设置用于跨组件串联日志与调用链的跟踪标识。
    /// </summary>
    public string TraceId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 获取或设置用户输入的原始文本。
    /// </summary>
    public string UserInput { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置上层编排阶段识别出的意图标签。
    /// </summary>
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置从输入中抽取出的槽位参数。
    /// </summary>
    public Dictionary<string, string> Slots { get; set; } = [];

    /// <summary>
    /// 获取或设置对历史对话的摘要信息，便于技能快速理解上下文。
    /// </summary>
    public string ConversationSummary { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置原始消息列表，供技能在需要时进行更细粒度分析。
    /// </summary>
    public List<ChatMessage> OriginalMessages { get; set; } = [];
}