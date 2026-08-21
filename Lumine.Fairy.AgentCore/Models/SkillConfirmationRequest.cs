namespace Lumine.Fairy.Models;

/// <summary>
/// 表示技能向宿主发起的一次通用确认请求。
/// </summary>
public sealed class SkillConfirmationRequest
{
    /// <summary>
    /// 获取或设置发起确认请求的技能名称。
    /// </summary>
    public string SourceSkillName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置确认窗口标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置需要向用户展示的确认内容。
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置确认按钮文案。
    /// </summary>
    public string ConfirmText { get; set; } = "确认";

    /// <summary>
    /// 获取或设置取消按钮文案。
    /// </summary>
    public string CancelText { get; set; } = "取消";

    /// <summary>
    /// 获取或设置该确认是否属于高风险操作。
    /// </summary>
    public bool IsDestructive { get; set; }
}