namespace Lumine.Fairy.Models;

/// <summary>
/// 表示代理当前所处的工作模式，用于影响技能权限、目录访问范围和交互策略。
/// </summary>
public enum AgentMode
{
    /// <summary>
    /// 工作模式，优先处理生产力与办公类任务。
    /// </summary>
    Work,

    /// <summary>
    /// 游戏模式，侧重娱乐和游戏相关的行为约束。
    /// </summary>
    Game,

    /// <summary>
    /// 媒体模式，适用于音视频播放与多媒体控制场景。
    /// </summary>
    Media,

    /// <summary>
    /// 休眠模式，通常用于降低主动行为或限制部分能力。
    /// </summary>
    Sleep,

    /// <summary>
    /// 混合模式，允许在多个场景之间采用更宽松的策略。
    /// </summary>
    Hybrid
}
