using Lumine.Fairy.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lumine.Fairy.Skills.Abstractions;

/// <summary>
/// 定义子代理技能需要实现的生命周期与执行接口。
/// </summary>
public interface ISubAgentSkill
{
    /// <summary>
    /// 获取技能清单元数据。
    /// </summary>
    SkillManifest Manifest { get; }

    /// <summary>
    /// 在技能首次被宿主使用前执行加载逻辑。
    /// </summary>
    /// <param name="context">宿主提供的启动上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task OnLoadAsync(
        SubAgentBootContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 以一次性结果的方式执行技能。
    /// </summary>
    /// <param name="request">技能请求。</param>
    /// <param name="context">技能执行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>技能执行结果。</returns>
    Task<SubAgentSkillResult> ExecuteAsync(
        SubAgentSkillRequest request,
        SubAgentSkillContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 以流式事件的方式执行技能。
    /// </summary>
    /// <param name="request">技能请求。</param>
    /// <param name="context">技能执行上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>技能执行过程中产生的事件流。</returns>
    IAsyncEnumerable<SubAgentSkillEvent> ExecuteStreamAsync(
        SubAgentSkillRequest request,
        SubAgentSkillContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在技能被宿主卸载时执行清理逻辑。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task OnUnloadAsync(CancellationToken cancellationToken = default);
}