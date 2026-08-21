using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Lumine.Fairy.Models;

/// <summary>
/// LLM 意图分析后返回的待确认文件操作。
/// 展示给用户确认后再执行，避免误操作。
/// </summary>
public sealed partial class PendingFileAction : ObservableObject
{
    public const int MaxDisplayedCandidates = 6;

    /// <summary>
    /// 获取产生该待确认动作的技能名称，用于在用户确认后正确路由回对应技能执行。
    /// </summary>
    public required string SourceSkillName { get; init; }

    /// <summary>操作类型，与 FileOpsPlugin KernelFunction 名称对应。</summary>
    public required string Action { get; init; }   // open / delete / rename / copy / move / create_folder

    /// <summary>人类可读的操作说明，例如"打开 通微 文件夹"。</summary>
    public required string ActionLabel { get; init; }

    /// <summary>操作的主目标路径（LLM 从候选列表中选择的最高置信度路径）。</summary>
    public required string TargetPath { get; init; }

    /// <summary>重命名时的新名称（仅 rename 操作有效）。</summary>
    public string? NewName { get; init; }

    /// <summary>复制/移动时的目标目录（仅 copy/move 操作有效）。</summary>
    public string? DestinationPath { get; init; }

    /// <summary>LLM 返回的候选路径列表（含置信度），供用户查看和切换。</summary>
    public required List<PendingCandidate> Candidates { get; init; }

    /// <summary>
    /// 获取当前操作是否允许一次勾选多个目标。
    /// </summary>
    public bool AllowsMultipleSelection => Action is "open" or "delete" or "copy" or "move";

    /// <summary>命中候选总数，可能大于当前实际展示的候选数量。</summary>
    public int TotalCandidateCount { get; init; }

    /// <summary>获取当前候选列表是否已被裁剪。</summary>
    public bool IsCandidateListTruncated => TotalCandidateCount > Candidates.Count;

    /// <summary>获取候选说明文案。</summary>
    public string CandidateSummary => IsCandidateListTruncated
        ? $"共匹配到 {TotalCandidateCount} 项，当前仅显示前 {Candidates.Count} 项，请{(AllowsMultipleSelection ? "勾选 1 个或多个目标后执行" : "勾选 1 个目标后执行")}。"
        : $"共匹配到 {Candidates.Count} 项，请{(AllowsMultipleSelection ? "勾选 1 个或多个目标后执行" : "勾选 1 个目标后执行")}。";

    /// <summary>
    /// 获取当前已勾选的候选项数量。
    /// </summary>
    public int SelectedCount => Candidates.Count == 0
        ? (string.IsNullOrWhiteSpace(TargetPath) ? 0 : 1)
        : Candidates.Count(x => x.IsSelected);

    /// <summary>
    /// 获取当前已勾选的候选项列表。
    /// </summary>
    public IReadOnlyList<PendingCandidate> SelectedCandidates =>
        Candidates.Count == 0
            ? []
            : Candidates.Where(x => x.IsSelected).ToList();

    /// <summary>
    /// 获取当前最终生效的路径列表。
    /// </summary>
    public IReadOnlyList<string> SelectedPaths => Candidates.Count == 0
        ? (string.IsNullOrWhiteSpace(TargetPath) ? [] : [TargetPath])
        : Candidates.Where(x => x.IsSelected).Select(x => x.Path).ToList();

    /// <summary>
    /// 获取当前最终生效的主路径，优先返回首个勾选项，否则回退到 <see cref="TargetPath"/>。
    /// </summary>
    public string SelectedPath => SelectedPaths.FirstOrDefault() ?? TargetPath;

    /// <summary>
    /// 获取当前选择提示文案。
    /// </summary>
    public string SelectionHint => AllowsMultipleSelection
        ? "请勾选一个或多个目标，再点击执行。"
        : "请勾选一个目标，再点击执行。";

    /// <summary>
    /// 获取执行按钮文案。
    /// </summary>
    public string ExecuteButtonText => AllowsMultipleSelection
        ? (SelectedCount > 0 ? $"✅ 执行所选（{SelectedCount}）" : "✅ 执行所选")
        : "✅ 确认执行";

    /// <summary>操作已被用户执行或取消，用于让按钮变灰。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSelection))]
    [NotifyPropertyChangedFor(nameof(IsExecutable))]
    [NotifyPropertyChangedFor(nameof(ExecuteButtonText))]
    private bool _isDone;

    /// <summary>
    /// 获取当前是否仍允许继续勾选候选项。
    /// </summary>
    public bool CanEditSelection => !IsDone;

    /// <summary>
    /// 获取当前操作是否仍可执行。
    /// </summary>
    public bool IsExecutable => CanEditSelection && SelectedCount > 0;

    /// <summary>
    /// 切换候选项的选中状态。
    /// </summary>
    /// <param name="candidate">用户点击的候选项。</param>
    public void ToggleCandidateSelection(PendingCandidate candidate)
    {
        if (candidate is null)
        {
            return;
        }

        var containsCandidate = Candidates.Any(x => ReferenceEquals(x, candidate));
        if (!containsCandidate || IsDone)
        {
            return;
        }

        if (AllowsMultipleSelection)
        {
            candidate.IsSelected = !candidate.IsSelected;
            NotifySelectionStateChanged();
            return;
        }

        var nextSelectedState = !candidate.IsSelected;
        foreach (var item in Candidates)
        {
            item.IsSelected = false;
        }

        candidate.IsSelected = nextSelectedState;
        NotifySelectionStateChanged();
    }

    /// <summary>
    /// 主动刷新依赖选中状态的派生属性。
    /// </summary>
    public void NotifySelectionStateChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCandidates));
        OnPropertyChanged(nameof(SelectedPaths));
        OnPropertyChanged(nameof(SelectedPath));
        OnPropertyChanged(nameof(IsExecutable));
        OnPropertyChanged(nameof(ExecuteButtonText));
    }
}

/// <summary>
/// 表示待确认文件操作中的一个候选目标。
/// </summary>
public sealed partial class PendingCandidate : ObservableObject
{
    /// <summary>
    /// 获取或设置候选项在候选列表中的顺序索引。
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// 获取或设置候选目标的完整路径。
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// 获取或设置该候选项被模型判定为目标的置信度。
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// 获取或设置候选目标是否为目录。
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// 生成 <c>IsSelected</c> 属性的后备字段，表示当前候选项在 UI 中是否被选中。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardBackground))]
    [NotifyPropertyChangedFor(nameof(CardBorderBrush))]
    [NotifyPropertyChangedFor(nameof(SelectionIndicatorFill))]
    [NotifyPropertyChangedFor(nameof(SelectionIndicatorBorderBrush))]
    [NotifyPropertyChangedFor(nameof(IsSelectionBadgeVisible))]
    private bool _isSelected;

    public string DisplayName => System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    public string DirectoryTypeLabel => IsDirectory ? "文件夹" : "文件";

    /// <summary>
    /// 获取供界面显示的置信度文本。
    /// </summary>
    public string ConfidenceLabel => $"{Confidence:P0}";

    /// <summary>
    /// 获取供界面显示的类型图标。
    /// </summary>
    public string TypeIcon => IsDirectory ? "📁" : "📄";

    /// <summary>
    /// 获取候选卡片的背景色。
    /// </summary>
    public string CardBackground => IsSelected ? "#1E3A8A" : "#0F172A";

    /// <summary>
    /// 获取候选卡片的边框颜色。
    /// </summary>
    public string CardBorderBrush => IsSelected ? "#60A5FA" : "#334155";

    public string SelectionIndicatorFill => IsSelected ? "#60A5FA" : "Transparent";

    public string SelectionIndicatorBorderBrush => IsSelected ? "#93C5FD" : "#64748B";

    public bool IsSelectionBadgeVisible => IsSelected;
}
