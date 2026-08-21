namespace Lumine.Fairy.Models;

/// <summary>
/// 表示 FileOps 执行后的结构化展示信息。
/// </summary>
public sealed class FileOpsExecutionCard
{
    public string StatusTitle { get; init; } = string.Empty;

    public string BadgeText { get; init; } = string.Empty;

    public bool IsSuccess { get; init; }

    public string ExecutionStatusText { get; init; } = string.Empty;

    public string ActionText { get; init; } = string.Empty;

    public string StatusDetail { get; init; } = string.Empty;

    public string FailureReason { get; init; } = string.Empty;

    public string ToolCallSummary { get; init; } = string.Empty;

    public string SelectedCountText { get; init; } = string.Empty;

    public bool HasAction => !string.IsNullOrWhiteSpace(ActionText);

    public bool HasStatusDetail => !string.IsNullOrWhiteSpace(StatusDetail);

    public bool HasFailureReason => !string.IsNullOrWhiteSpace(FailureReason);

    public bool HasToolCallSummary => !string.IsNullOrWhiteSpace(ToolCallSummary);

    public bool HasSelectedCount => !string.IsNullOrWhiteSpace(SelectedCountText);
}