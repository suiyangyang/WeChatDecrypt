using Lumine.Fairy.Models;
using Lumine.Fairy.Skills.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Lumine.Fairy.Services;

/// <summary>
/// 提供技能生命周期管理、串行化执行和超时控制能力。
/// </summary>
public sealed class SkillRuntimeHost
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _skillLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _loadedSkills = new(StringComparer.OrdinalIgnoreCase);
    private readonly SubAgentBootContext _bootContext;

    /// <summary>
    /// 初始化一个新的技能运行时宿主实例。
    /// </summary>
    /// <param name="bootContext">技能加载时使用的宿主上下文；为空时使用默认上下文。</param>
    public SkillRuntimeHost(SubAgentBootContext? bootContext = null)
    {
        _bootContext = bootContext ?? new SubAgentBootContext();
    }

    /// <summary>
    /// 以非流式方式执行一个技能，并负责加载、并发控制与超时处理。
    /// </summary>
    /// <param name="skill">要执行的技能实例。</param>
    /// <param name="request">技能请求。</param>
    /// <param name="context">技能执行上下文。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <returns>技能执行结果；若发生异常或超时，将返回统一的失败结果。</returns>
    public async Task<SubAgentSkillResult> ExecuteAsync(
        ISubAgentSkill skill,
        SubAgentSkillRequest request,
        SubAgentSkillContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var gate = GetGate(skill.Manifest.Name);
        await gate.WaitAsync(cancellationToken);

        try
        {
            await EnsureLoadedAsync(skill, cancellationToken);

            using var timeoutCts = CreateTimeoutCts(skill.Manifest.TimeoutSeconds, cancellationToken);
            return await skill.ExecuteAsync(request, context, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SubAgentSkillResult
            {
                Handled = true,
                Success = false,
                ErrorCode = "skill_timeout",
                ErrorMessage = $"Skill '{skill.Manifest.Name}' 执行超时。",
                ReplyText = $"{skill.Manifest.DisplayName} 执行超时。"
            };
        }
        catch (Exception ex)
        {
            return new SubAgentSkillResult
            {
                Handled = true,
                Success = false,
                ErrorCode = "skill_execution_failed",
                ErrorMessage = ex.Message,
                ReplyText = ex.Message
            };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 以流式方式执行一个技能，并逐条输出其产生的事件。
    /// </summary>
    /// <param name="skill">要执行的技能实例。</param>
    /// <param name="request">技能请求。</param>
    /// <param name="context">技能执行上下文。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <returns>技能产生的异步事件流。</returns>
    public async IAsyncEnumerable<SubAgentSkillEvent> ExecuteStreamAsync(
        ISubAgentSkill skill,
        SubAgentSkillRequest request,
        SubAgentSkillContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var gate = GetGate(skill.Manifest.Name);
        await gate.WaitAsync(cancellationToken);

        using var timeoutCts = CreateTimeoutCts(skill.Manifest.TimeoutSeconds, cancellationToken);

        try
        {
            await EnsureLoadedAsync(skill, cancellationToken);

            await foreach (var evt in skill.ExecuteStreamAsync(request, context, timeoutCts.Token))
            {
                yield return evt;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 卸载一个已加载的技能，并调用其卸载回调。
    /// </summary>
    /// <param name="skill">要卸载的技能实例。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    public async Task UnloadAsync(ISubAgentSkill skill, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skill);

        if (_loadedSkills.TryRemove(skill.Manifest.Name, out _))
        {
            await skill.OnUnloadAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 获取指定技能对应的串行化执行信号量。
    /// </summary>
    /// <param name="skillName">技能名称。</param>
    /// <returns>与该技能绑定的信号量实例。</returns>
    private SemaphoreSlim GetGate(string skillName)
    {
        return _skillLocks.GetOrAdd(skillName, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// 确保技能已完成加载；未加载时会调用其加载回调并记录状态。
    /// </summary>
    /// <param name="skill">技能实例。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    private async Task EnsureLoadedAsync(ISubAgentSkill skill, CancellationToken cancellationToken)
    {
        if (_loadedSkills.ContainsKey(skill.Manifest.Name))
        {
            return;
        }

        await skill.OnLoadAsync(_bootContext, cancellationToken);
        _loadedSkills.TryAdd(skill.Manifest.Name, 0);
    }

    /// <summary>
    /// 创建带超时能力的链接取消令牌源。
    /// </summary>
    /// <param name="timeoutSeconds">超时时间（秒）；小于等于 0 表示不额外设置超时。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <returns>新的链接取消令牌源。</returns>
    private static CancellationTokenSource CreateTimeoutCts(int timeoutSeconds, CancellationToken cancellationToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (timeoutSeconds > 0)
        {
            linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        return linkedCts;
    }
}