using Lumine.Fairy.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Lumine.Fairy.Skills.Abstractions;

/// <summary>
/// 定义支持执行待确认命令动作的技能能力。
/// </summary>
public interface IPendingCommandExecutableSkill
{
    /// <summary>
    /// 执行用户已确认的待处理命令动作。
    /// </summary>
    /// <param name="action">待执行的命令动作描述。</param>
    /// <param name="context">技能执行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>动作执行结果。</returns>
    Task<SubAgentSkillResult> ExecutePendingCommandActionAsync(
        PendingCommandAction action,
        SubAgentSkillContext context,
        CancellationToken cancellationToken = default);
}
