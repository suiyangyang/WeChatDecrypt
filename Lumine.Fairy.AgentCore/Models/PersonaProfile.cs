using System;
using System.Text.Json.Serialization;

namespace Lumine.Fairy.Models;

/// <summary>
/// 描述一个可被用户选择的助手角色配置，包括人设、绑定模型和可用技能。
/// </summary>
public sealed class PersonaProfile
{
    /// <summary>
    /// 获取或设置角色唯一标识。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 获取或设置角色标签，用于列表展示与角色切换，例如“产品经理”或“桌面助手”。
    /// </summary>
    public string Name { get; set; } = "默认角色";

    /// <summary>
    /// 获取或设置助手显示名，用于对话或设置页面展示，例如“Fairy”。
    /// </summary>
    public string AssistantName { get; set; } = "Fairy";

    /// <summary>
    /// 获取或设置该角色的系统提示词，用于约束助手语气、行为和能力边界。
    /// </summary>
    public string Prompt { get; set; } = "你是 Fairy，一个友好且智能的桌面助手。请用简洁、有帮助的方式回答用户的问题。";

    /// <summary>
    /// 获取或设置绑定的 LLM 端点名称；为空时表示沿用系统默认端点。
    /// </summary>
    public string BoundEndpointName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置绑定到该角色的技能名称列表。
    /// </summary>
    public List<string> BoundSkillNames { get; set; } = new();

    /// <summary>
    /// 获取或设置角色头像资源路径，可为 Avalonia 资源 URI 或本地文件路径。
    /// </summary>
    public string AvatarPath { get; set; } = "avares://Lumine.Fairy/Assets/fairy-tray.png";

    /// <summary>
    /// 获取或设置该角色是否为内置固定角色；固定角色通常不允许被删除。
    /// </summary>
    public bool IsFixed { get; set; }

    /// <summary>
    /// 获取或设置该角色是否为当前激活角色；该值仅用于 UI 状态，不参与持久化。
    /// </summary>
    [JsonIgnore]
    public bool IsActive { get; set; }
}
