namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 提供能力注册能力。
/// </summary>
public interface ICapabilityRegistrar
{
    /// <summary>
    /// 注册一个能力实例。
    /// </summary>
    /// <typeparam name="TCapability">能力契约类型。</typeparam>
    /// <param name="capability">能力实例。</param>
    void Register<TCapability>(TCapability capability)
        where TCapability : class, ISkillCapability;
}