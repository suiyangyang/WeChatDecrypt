using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lumine.Fairy.Models;

/// <summary>
/// 表示子代理技能在加载阶段可获取的宿主上下文。
/// </summary>
public sealed class SubAgentBootContext
{
    /// <summary>
    /// 获取或设置技能运行时协议版本。
    /// </summary>
    public string RuntimeVersion { get; set; } = "1.0";

    /// <summary>
    /// 获取或设置宿主应用版本。
    /// </summary>
    public string HostVersion { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置宿主应用的数据目录。
    /// </summary>
    public string AppDataDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置事件发布委托，供技能在加载期或运行期向宿主发送事件。
    /// </summary>
    public Func<SubAgentSkillEvent, CancellationToken, Task>? PublishEventAsync { get; set; }
}