using Lumine.Fairy.Models;
using Lumine.Fairy.Modules.Abstractions;

namespace Lumine.Fairy.Services;

/// <summary>
/// 提供动态设置分区注册与查询能力。
/// </summary>
public sealed class SettingsSectionRegistryService
{
    private readonly Dictionary<string, ISettingsSectionProvider> _sections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册一个动态设置分区；若同名分区已存在则用新实例覆盖。
    /// </summary>
    /// <param name="provider">分区提供器。</param>
    public void Register(ISettingsSectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _sections[provider.Manifest.Id] = provider;
    }

    /// <summary>
    /// 批量注册多个动态设置分区。
    /// </summary>
    /// <param name="providers">分区提供器集合。</param>
    public void RegisterRange(IEnumerable<ISettingsSectionProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        foreach (var provider in providers)
        {
            Register(provider);
        }
    }

    /// <summary>
    /// 尝试按标识获取一个动态设置分区。
    /// </summary>
    /// <param name="sectionId">分区标识。</param>
    /// <param name="provider">输出分区提供器。</param>
    /// <returns>找到时返回 <see langword="true" />；否则返回 <see langword="false" />。</returns>
    public bool TryGetSection(string sectionId, out ISettingsSectionProvider? provider)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
        {
            provider = null;
            return false;
        }

        return _sections.TryGetValue(sectionId.Trim(), out provider);
    }

    /// <summary>
    /// 获取所有已注册的设置分区，并按排序值与标题排序。
    /// </summary>
    /// <returns>设置分区提供器集合。</returns>
    public IReadOnlyList<ISettingsSectionProvider> GetRegisteredSections() =>
        _sections.Values
            .OrderBy(x => x.Manifest.Order)
            .ThenBy(x => x.Manifest.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// 获取所有已注册设置分区的清单。
    /// </summary>
    /// <returns>设置分区清单集合。</returns>
    public IReadOnlyList<SettingsSectionManifest> GetRegisteredManifests() =>
        GetRegisteredSections()
            .Select(x => x.Manifest)
            .ToList();
}