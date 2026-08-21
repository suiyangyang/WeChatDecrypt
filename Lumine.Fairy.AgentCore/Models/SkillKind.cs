namespace Lumine.Fairy.Models;

/// <summary>
/// 表示技能的逻辑分类。
/// </summary>
public enum SkillKind
{
    /// <summary>
    /// 轻量工具类技能，通常执行单一明确动作。
    /// </summary>
    Tool = 0,

    /// <summary>
    /// 子代理类技能，能够围绕某一目标进行多步决策与执行。
    /// </summary>
    SubAgent = 1
}