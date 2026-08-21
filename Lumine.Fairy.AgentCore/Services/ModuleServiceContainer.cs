using Lumine.Fairy.Modules.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Lumine.Fairy.Services;

/// <summary>
/// 提供轻量级模块服务容器，用于注册和解析模块内部共享服务。
/// </summary>
public sealed class ModuleServiceContainer : IModuleServiceRegistry, IModuleServiceProvider
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<Type, Func<IModuleServiceProvider, object>> _singletonFactories = new();
    private readonly Dictionary<Type, object> _singletonInstances = new();

    /// <summary>
    /// 初始化一个新的模块服务容器实例。
    /// </summary>
    public ModuleServiceContainer()
    {
        RegisterInstance(typeof(IModuleServiceRegistry), this);
        RegisterInstance(typeof(IModuleServiceProvider), this);
        RegisterInstance(typeof(ModuleServiceContainer), this);
    }

    /// <inheritdoc />
    public void AddSingleton<TService>(Func<IModuleServiceProvider, TService> factory)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (_syncRoot)
        {
            _singletonFactories[typeof(TService)] = provider => factory(provider);
            _singletonInstances.Remove(typeof(TService));
        }
    }

    /// <inheritdoc />
    public void AddSingleton<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        lock (_syncRoot)
        {
            _singletonFactories[typeof(TService)] = _ => CreateInstance(typeof(TImplementation));
            _singletonInstances.Remove(typeof(TService));
        }
    }

    /// <inheritdoc />
    public T GetRequiredService<T>() where T : class
    {
        var service = GetService<T>();
        if (service is not null)
        {
            return service;
        }

        throw new InvalidOperationException($"未注册模块服务：{typeof(T).FullName}。");
    }

    /// <inheritdoc />
    public T? GetService<T>() where T : class
    {
        return GetService(typeof(T)) as T;
    }

    private object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        lock (_syncRoot)
        {
            if (_singletonInstances.TryGetValue(serviceType, out var instance))
            {
                return instance;
            }

            if (!_singletonFactories.TryGetValue(serviceType, out var factory))
            {
                return null;
            }

            instance = factory(this);
            _singletonInstances[serviceType] = instance;
            return instance;
        }
    }

    private object CreateInstance(Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(implementationType);

        var constructors = implementationType
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(static ctor => ctor.GetParameters().Length)
            .ToArray();

        foreach (var constructor in constructors)
        {
            var parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];
            var canResolve = true;

            for (var index = 0; index < parameters.Length; index++)
            {
                var resolved = GetService(parameters[index].ParameterType);
                if (resolved is null)
                {
                    canResolve = false;
                    break;
                }

                arguments[index] = resolved;
            }

            if (canResolve)
            {
                return constructor.Invoke(arguments);
            }
        }

        if (constructors.Length == 0)
        {
            return Activator.CreateInstance(implementationType)
                ?? throw new InvalidOperationException($"无法创建模块服务实例：{implementationType.FullName}。");
        }

        throw new InvalidOperationException($"无法解析模块服务 {implementationType.FullName} 的构造函数依赖。请先注册其依赖服务。");
    }

    private void RegisterInstance(Type serviceType, object instance)
    {
        _singletonInstances[serviceType] = instance;
    }
}