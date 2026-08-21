namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一个大语言模型接入端点的配置信息。
/// </summary>
public sealed class LlmEndpointConfig
{
    /// <summary>
    /// 获取或设置端点名称，用于在配置与角色中引用。
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// 获取或设置服务提供商标识，例如 OpenAI、Azure 或自定义实现。
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// 获取或设置实际调用的模型名称。
    /// </summary>
    public required string Model { get; set; }

    /// <summary>
    /// 获取或设置可选的 API 基础地址；对于兼容接口或自建服务尤为重要。
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 获取或设置访问该端点所需的 API 密钥。
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 获取或设置该端点是否为默认端点。
    /// </summary>
    public bool IsDefault { get; set; }
}
