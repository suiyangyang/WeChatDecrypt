using Lumine.Fairy.Models;

namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 为模块包在宿主运行时实例化模块提供必要上下文。
/// </summary>
public interface IModuleRuntimeContext
{
    AgentMode CurrentMode { get; }

    IReadOnlyList<LlmEndpointConfig> LlmEndpoints { get; }

    IReadOnlyDictionary<AgentMode, List<string>> AllowedDirectories { get; }

    IReadOnlyDictionary<AgentMode, int> DirectoryAccessDepthLimits { get; }

    IReadOnlyDictionary<AgentMode, List<DirectoryAccessRule>> DirectoryRules { get; }

    IReadOnlyList<HabitItem> HabitItems { get; }

    IModuleConfigAccessor ModuleConfigAccessor { get; }

    TService? GetOptionalHostService<TService>() where TService : class;

    bool IsSkillEnabledForPersona(string skillName, SubAgentSkillContext context);

    void SaveConfig();
}