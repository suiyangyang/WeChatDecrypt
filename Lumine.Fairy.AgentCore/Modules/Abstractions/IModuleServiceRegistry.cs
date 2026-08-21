using System;

namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 提供模块内部共享服务注册能力。
/// </summary>
public interface IModuleServiceRegistry
{
    /// <summary>
    /// 使用工厂注册一个单例服务。
    /// </summary>
    /// <typeparam name="TService">服务类型。</typeparam>
    /// <param name="factory">创建服务的工厂方法。</param>
    void AddSingleton<TService>(Func<IModuleServiceProvider, TService> factory)
        where TService : class;

    /// <summary>
    /// 使用实现类型注册一个单例服务。
    /// </summary>
    /// <typeparam name="TService">服务接口或基类。</typeparam>
    /// <typeparam name="TImplementation">实现类型。</typeparam>
    void AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;
}