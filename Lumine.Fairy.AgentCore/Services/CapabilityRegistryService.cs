using Lumine.Fairy.Modules.Abstractions;
using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Services;

/// <summary>
/// 提供模块能力注册与解析能力。
/// </summary>
public sealed class CapabilityRegistryService : ICapabilityRegistrar, ICapabilityResolver
{
    private readonly Dictionary<Type, ISkillCapability> _capabilities = new();

    /// <inheritdoc />
    public void Register<TCapability>(TCapability capability)
        where TCapability : class, ISkillCapability
    {
        ArgumentNullException.ThrowIfNull(capability);
        _capabilities[typeof(TCapability)] = capability;
    }

    /// <inheritdoc />
    public TCapability GetRequiredCapability<TCapability>()
        where TCapability : class, ISkillCapability
    {
        var capability = GetCapability<TCapability>();
        if (capability is not null)
        {
            return capability;
        }

        throw new InvalidOperationException($"未注册模块能力：{typeof(TCapability).FullName}。");
    }

    /// <inheritdoc />
    public TCapability? GetCapability<TCapability>()
        where TCapability : class, ISkillCapability
    {
        return _capabilities.TryGetValue(typeof(TCapability), out var capability)
            ? capability as TCapability
            : null;
    }
}