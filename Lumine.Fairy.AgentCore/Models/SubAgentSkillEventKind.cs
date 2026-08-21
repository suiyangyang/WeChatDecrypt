namespace Lumine.Fairy.Models;

/// <summary>
/// 表示子代理技能事件的类别。
/// </summary>
public enum SubAgentSkillEventKind
{
    /// <summary>
    /// 常规状态更新事件。
    /// </summary>
    Status = 0,

    /// <summary>
    /// 文本流式增量事件。
    /// </summary>
    TextDelta = 1,

    /// <summary>
    /// 工具调用事件。
    /// </summary>
    ToolCall = 2,

    /// <summary>
    /// 需要用户参与决策或确认的事件。
    /// </summary>
    UserInteractionRequired = 3,

    /// <summary>
    /// 技能执行完成事件。
    /// </summary>
    Completed = 4,

    /// <summary>
    /// 技能执行过程中出现错误的事件。
    /// </summary>
    Error = 5
}