namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 提供能力解析能力。
/// </summary>
public interface ICapabilityResolver
{
    /// <summary>
    /// 获取指定能力；不存在时抛出异常。
    /// </summary>
    /// <typeparam name="TCapability">能力契约类型。</typeparam>
    /// <returns>能力实例。</returns>
    TCapability GetRequiredCapability<TCapability>()
        where TCapability : class, ISkillCapability;

    /// <summary>
    /// 获取指定能力；不存在时返回 <see langword="null" />。
    /// </summary>
    /// <typeparam name="TCapability">能力契约类型。</typeparam>
    /// <returns>能力实例或空。</returns>
    TCapability? GetCapability<TCapability>()
        where TCapability : class, ISkillCapability;
}