using Lumine.Fairy.Models;

namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 定义一个可被宿主装载的 Skill 模块。
/// </summary>
public interface ISkillModule
{
    /// <summary>
    /// 获取模块清单信息。
    /// </summary>
    SkillModuleManifest Manifest { get; }

    /// <summary>
    /// 向宿主注册模块内部共享服务。
    /// </summary>
    /// <param name="services">模块服务注册器。</param>
    /// <param name="configAccessor">模块配置访问器。</param>
    void RegisterServices(
        IModuleServiceRegistry services,
        IModuleConfigAccessor configAccessor);

    /// <summary>
    /// 向宿主注册模块内提供的技能。
    /// </summary>
    /// <param name="skillRegistrar">技能注册器。</param>
    /// <param name="services">模块服务提供器。</param>
    /// <param name="configAccessor">模块配置访问器。</param>
    void RegisterSkills(
        ISkillRegistrar skillRegistrar,
        IModuleServiceProvider services,
        IModuleConfigAccessor configAccessor);

    /// <summary>
    /// 向宿主注册模块向外暴露的能力接口。
    /// </summary>
    /// <param name="capabilityRegistrar">能力注册器。</param>
    /// <param name="services">模块服务提供器。</param>
    /// <param name="configAccessor">模块配置访问器。</param>
    void RegisterCapabilities(
        ICapabilityRegistrar capabilityRegistrar,
        IModuleServiceProvider services,
        IModuleConfigAccessor configAccessor);

    /// <summary>
    /// 获取该模块提供的动态设置分区集合。
    /// </summary>
    /// <param name="services">模块服务提供器。</param>
    /// <param name="configAccessor">模块配置访问器。</param>
    /// <returns>动态设置分区列表。</returns>
    IReadOnlyList<ISettingsSectionProvider> GetSettingsSections(
        IModuleServiceProvider services,
        IModuleConfigAccessor configAccessor);

    IReadOnlyList<IModuleSkillDefaultsProvider> GetSkillDefaultsProviders()
    {
        return [];
    }

    IReadOnlyList<IModuleConfigSyncProvider> GetConfigSyncProviders()
    {
        return [];
    }

    IReadOnlyList<IConversationRouteProvider> GetConversationRouteProviders(
        IModuleServiceProvider services,
        IModuleConfigAccessor configAccessor)
    {
        return [];
    }
}