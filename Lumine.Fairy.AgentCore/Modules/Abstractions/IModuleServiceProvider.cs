namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 提供模块内共享服务解析能力。
/// </summary>
public interface IModuleServiceProvider
{
    /// <summary>
    /// 获取指定服务；不存在时抛出异常。
    /// </summary>
    /// <typeparam name="T">服务类型。</typeparam>
    /// <returns>服务实例。</returns>
    T GetRequiredService<T>() where T : class;

    /// <summary>
    /// 获取指定服务；不存在时返回 <see langword="null" />。
    /// </summary>
    /// <typeparam name="T">服务类型。</typeparam>
    /// <returns>服务实例或空。</returns>
    T? GetService<T>() where T : class;
}