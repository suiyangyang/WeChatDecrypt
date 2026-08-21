namespace Lumine.Fairy.Models;

public sealed class WeChatScanProgressCard
{
    public string OperationKey { get; init; } = string.Empty;

    public string Title { get; init; } = "微信扫描进度";

    public string StageText { get; init; } = string.Empty;

    public string DetailText { get; init; } = string.Empty;

    public string CurrentConversationName { get; init; } = string.Empty;

    public string ProgressRangeText { get; init; } = string.Empty;

    public int Percent { get; init; }

    public bool IsCompleted { get; init; }

    public bool IsError { get; init; }

    public bool IsWarning { get; init; }

    public bool IsCancelled { get; init; }

    public bool IsCollapsed { get; init; }

    public bool IsStopRequested { get; init; }

    public bool IsTerminal => IsCompleted || IsCancelled || IsError;

    public bool CanToggleCollapsedState => HasDetail && (IsCompleted || IsCancelled) && !IsError;

    public bool CanStop => !IsTerminal && !IsStopRequested;

    public bool ShowStopButton => !IsTerminal;

    public bool IsCompactCompleted => (IsCompleted || IsCancelled) && IsCollapsed && !IsError;

    public string BadgeText => IsCancelled
        ? "已停止"
        : IsCompleted
        ? "已完成"
        : IsError
            ? "失败"
            : $"{Percent}%";

    public string CompactSummaryText => !string.IsNullOrWhiteSpace(DetailText)
        ? DetailText
        : StageText;

    public string ToggleHintText => !CanToggleCollapsedState
        ? string.Empty
        : IsCollapsed
            ? "展开详情"
            : "收起详情";

    public string StopButtonText => IsStopRequested ? "停止中" : "停止";

    public string ToggleGlyph => !CanToggleCollapsedState
        ? string.Empty
        : IsCollapsed
            ? "▾"
            : "▴";

    public double CardOpacity => IsCompactCompleted ? 0.78 : 1d;

    public bool HasCurrentConversation => !string.IsNullOrWhiteSpace(CurrentConversationName);

    public bool HasDetail => !string.IsNullOrWhiteSpace(DetailText);

    public bool HasProgressRange => !string.IsNullOrWhiteSpace(ProgressRangeText);

    public bool ShowCurrentConversation => HasCurrentConversation && !IsCompactCompleted;

    public bool ShowProgressBar => !IsCancelled && !IsError;

    public bool ShowProgressRange => HasProgressRange && !IsCancelled && !IsError;

    public bool ShowDetail => HasDetail && (IsError || (IsTerminal && !IsCollapsed));

    public bool ShowCompletedSummary => false;

    public WeChatScanProgressCard WithCollapseState(bool isCollapsed)
    {
        return new WeChatScanProgressCard
        {
            OperationKey = OperationKey,
            Title = Title,
            StageText = StageText,
            DetailText = DetailText,
            CurrentConversationName = CurrentConversationName,
            ProgressRangeText = ProgressRangeText,
            Percent = Percent,
            IsCompleted = IsCompleted,
            IsError = IsError,
            IsWarning = IsWarning,
            IsCancelled = IsCancelled,
            IsCollapsed = isCollapsed,
            IsStopRequested = IsStopRequested
        };
    }

    public WeChatScanProgressCard WithStopRequested(bool isStopRequested)
    {
        return new WeChatScanProgressCard
        {
            OperationKey = OperationKey,
            Title = Title,
            StageText = isStopRequested ? "正在停止扫描" : StageText,
            DetailText = DetailText,
            CurrentConversationName = CurrentConversationName,
            ProgressRangeText = ProgressRangeText,
            Percent = Percent,
            IsCompleted = IsCompleted,
            IsError = IsError,
            IsWarning = IsWarning,
            IsCancelled = IsCancelled,
            IsCollapsed = IsCollapsed,
            IsStopRequested = isStopRequested
        };
    }
}