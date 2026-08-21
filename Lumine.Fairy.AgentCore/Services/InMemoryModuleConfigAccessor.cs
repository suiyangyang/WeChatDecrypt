using Lumine.Fairy.Modules.Abstractions;
using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Services;

/// <summary>
/// 提供一个基于内存字典的模块配置访问器，适用于宿主尚未迁移到模块分区配置前的过渡阶段。
/// </summary>
public sealed class InMemoryModuleConfigAccessor : IModuleConfigAccessor
{
    private readonly Dictionary<string, object> _sections = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public T GetSection<T>(string sectionKey) where T : new()
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            throw new ArgumentException("配置分区键不能为空。", nameof(sectionKey));
        }

        if (_sections.TryGetValue(sectionKey.Trim(), out var value) && value is T typed)
        {
            return typed;
        }

        var created = new T();
        _sections[sectionKey.Trim()] = created;
        return created;
    }

    /// <inheritdoc />
    public void SaveSection<T>(string sectionKey, T value)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            throw new ArgumentException("配置分区键不能为空。", nameof(sectionKey));
        }

        ArgumentNullException.ThrowIfNull(value);
        _sections[sectionKey.Trim()] = value;
    }
}