namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 为模块提供按分区读取和保存配置的能力。
/// </summary>
public interface IModuleConfigAccessor
{
    /// <summary>
    /// 获取指定分区的配置对象；若不存在则返回新实例。
    /// </summary>
    /// <typeparam name="T">配置对象类型。</typeparam>
    /// <param name="sectionKey">配置分区键。</param>
    /// <returns>配置对象。</returns>
    T GetSection<T>(string sectionKey) where T : new();

    /// <summary>
    /// 保存指定分区的配置对象。
    /// </summary>
    /// <typeparam name="T">配置对象类型。</typeparam>
    /// <param name="sectionKey">配置分区键。</param>
    /// <param name="value">要保存的配置对象。</param>
    void SaveSection<T>(string sectionKey, T value);
}