namespace Lumine.Fairy.Models;

/// <summary>
/// 表示一次工具调用的轻量记录，可用于日志、回放或统计。
/// </summary>
public sealed class ToolInvocationRecord
{
    /// <summary>
    /// 获取初始化时设置的工具名称。
    /// </summary>
    public string ToolName { get; init; } = string.Empty;

    public string ModelToolCallFunctionName { get; init; } = string.Empty;

    public string ModelToolCallArgumentsJson { get; init; } = string.Empty;

    public DateTimeOffset? ModelToolCallStartedAt { get; init; }

    public DateTimeOffset? ModelToolCallCompletedAt { get; init; }

    public double? ModelToolCallDurationMilliseconds { get; init; }

    /// <summary>
    /// 获取初始化时设置的调用结果是否成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 获取初始化时设置的结果摘要。
    /// </summary>
    public string ResultSummary { get; init; } = string.Empty;
}