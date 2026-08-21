namespace Lumine.Fairy.Modules.Abstractions;

/// <summary>
/// 描述一个可被宿主发现的模块包入口，用于产出模块元数据与运行时模块实例。
/// </summary>
public interface ISkillModulePackage
{
    IReadOnlyList<ISkillModule> CreateMetadataModules();

    IReadOnlyList<ISkillModule> CreateRuntimeModules(IModuleRuntimeContext runtimeContext);
}