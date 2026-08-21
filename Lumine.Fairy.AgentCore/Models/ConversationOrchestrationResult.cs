namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一次对话编排后的处理结果，用于决定是否已命中技能以及是否需要回退到大模型回答。
/// </summary>
public sealed class ConversationOrchestrationResult
{
    /// <summary>
    /// 获取或设置当前请求是否已被编排流程成功接管。
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// 获取或设置在未完全处理时是否需要继续走 LLM 回退流程。
    /// </summary>
    public bool RequiresLlmFallback { get; set; }

    /// <summary>
    /// 获取或设置回退到 LLM 时应使用的补充提示信息。
    /// </summary>
    public string FallbackMessage { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置需要额外展示给用户的通知消息。
    /// </summary>
    public string NoticeMessage { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置技能执行结果；若未命中技能则可能为 <see langword="null"/>。
    /// </summary>
    public SubAgentSkillResult? SkillResult { get; set; }
}