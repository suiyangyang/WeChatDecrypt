using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一次子代理技能执行时的动态上下文。
/// </summary>
public sealed class SubAgentSkillContext
{
    /// <summary>
    /// 获取或设置当前代理模式。
    /// </summary>
    public AgentMode CurrentMode { get; set; } = AgentMode.Work;

    /// <summary>
    /// 获取或设置当前会话标识。
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前激活角色的标识。
    /// </summary>
    public string ActivePersonaId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前激活的角色配置。
    /// </summary>
    public PersonaProfile? ActivePersona { get; set; }

    /// <summary>
    /// 获取或设置当前生效的大模型端点配置。
    /// </summary>
    public LlmEndpointConfig? ActiveEndpoint { get; set; }

    /// <summary>
    /// 获取或设置按代理模式划分的允许访问目录列表。
    /// </summary>
    public Dictionary<AgentMode, List<string>> AllowedDirectories { get; set; } = [];

    /// <summary>
    /// 获取或设置按代理模式划分的目录访问规则集合。
    /// </summary>
    public Dictionary<AgentMode, List<DirectoryAccessRule>> DirectoryRules { get; set; } = [];

    /// <summary>
    /// 获取或设置当前已授予技能的权限列表。
    /// </summary>
    public List<SkillPermission> GrantedPermissions { get; set; } = [];

    /// <summary>
    /// 获取或设置宿主确认回调，供技能在执行高风险或需要用户确认的操作前发起统一确认。
    /// </summary>
    public Func<SkillConfirmationRequest, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>
    /// 获取或设置记录打开动作的回调，用于更新习惯统计等附加行为。
    /// </summary>
    public Action<string>? RecordOpenAction { get; set; }

    /// <summary>
    /// 获取或设置向宿主发布结构化技能事件的回调。
    /// </summary>
    public Func<SubAgentSkillEvent, CancellationToken, Task>? PublishEventAsync { get; set; }

    /// <summary>
    /// 获取或设置向宿主持续推送文本增量的回调。
    /// </summary>
    public Func<string, CancellationToken, Task>? PublishTextDeltaAsync { get; set; }

    /// <summary>
    /// 获取或设置宿主服务提供器，供技能解析附加依赖。
    /// </summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// 获取或设置宿主从上一条桌面结果中提取出的已解析目标。
    /// </summary>
    public UiTarget? PreviousResolvedTarget { get; set; }
}