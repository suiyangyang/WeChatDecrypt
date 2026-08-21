using Lumine.Fairy.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Lumine.Fairy.Skills.Abstractions;

/// <summary>
/// 定义支持执行待确认动作的技能能力。
/// </summary>
public interface IPendingActionExecutableSkill
{
    /// <summary>
    /// 执行用户已确认的待处理动作。
    /// </summary>
    /// <param name="action">待执行的动作描述。</param>
    /// <param name="context">技能执行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>动作执行结果。</returns>
    Task<SubAgentSkillResult> ExecutePendingActionAsync(
        PendingFileAction action,
        SubAgentSkillContext context,
        CancellationToken cancellationToken = default);
}