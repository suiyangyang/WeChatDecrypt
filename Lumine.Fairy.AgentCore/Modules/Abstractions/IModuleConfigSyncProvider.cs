using System.Collections.Generic;
using System.Text.Json;

namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 为模块提供配置分区默认值、归一化与旧字段迁移能力。
/// </summary>
public interface IModuleConfigSyncProvider
{
    /// <summary>
    /// 当前模块配置写入到 <c>ModuleSettings</c> 中使用的分区键。
    /// </summary>
    string SectionKey { get; }

    /// <summary>
    /// 旧版配置文件中用于承载该模块配置的遗留字段名集合。
    /// </summary>
    IReadOnlyList<string> LegacySectionKeys { get; }

    /// <summary>
    /// 创建该模块配置分区的默认值。
    /// </summary>
    JsonElement CreateDefaultSection(JsonSerializerOptions jsonOptions);

    /// <summary>
    /// 将输入分区归一化为模块认可的配置结构。
    /// </summary>
    JsonElement NormalizeSection(JsonElement section, JsonSerializerOptions jsonOptions);

    /// <summary>
    /// 尝试从旧版字段中迁移配置内容。
    /// </summary>
    bool TryMigrateLegacySection(
        IReadOnlyDictionary<string, JsonElement>? legacySections,
        JsonSerializerOptions jsonOptions,
        out JsonElement migratedSection);
}