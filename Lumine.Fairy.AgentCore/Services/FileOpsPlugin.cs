using Lumine.Fairy.Models;
using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lumine.Fairy.Services;

/// <summary>
/// Semantic Kernel Plugin：文件/文件夹操作。
/// 大模型通过 Function Calling 决定是否以及如何调用这些函数。
/// 调用前需设置运行时上下文（ConfirmAsync 委托、权限检查）。
/// </summary>
public sealed class FileOpsPlugin
{
    private static readonly string[] ExplicitFileTargetSuffixes = ["这个文件夹", "该文件夹", "这个目录", "该目录", "文件夹", "资料夹", "目录", "文件"];
    private static readonly string[] ExplicitFileTargetPrefixes = ["名为", "叫做", "叫", "这个", "那个", "该"];
    private readonly IFileAccessCapability _fileAccessCapability;
    private readonly IHabitCapability _habitCapability;
    private readonly IWorkspaceIndexCapability _workspaceIndexCapability;
    private readonly FileOpsSkillSettings _settings;
    private readonly AgentMode _currentMode;
    private readonly Func<SkillConfirmationRequest, Task<bool>> _confirmAsync;
    private readonly Func<string, bool> _isPathAllowed;
    private readonly Func<string, bool> _canWritePath;
    private readonly Action<string> _recordOpen;
    private readonly ILlmChatCapability _llmChatCapability;
    private readonly LlmEndpointConfig _llmEndpoint;
    private readonly Action<string> _postChatMessage;
    private readonly List<ToolInvocationRecord> _invoked = [];

    /// <summary>
    /// 在 FileOps Skill 内执行两阶段路径解析。
    ///
    /// 阶段1：代码侧匹配（索引字符/拼音 + 目录规则 + 用户习惯）
    ///   - 若命中，返回最佳路径；
    ///   - 若未命中，进入阶段2。
    ///
    /// 阶段2：LLM 深度匹配（基于候选列表）
    ///   - 将候选项与原始用户消息发送给 LLM，返回置信度达标的路径列表；
    ///   - 若无匹配则返回失败。
    ///
    /// 返回 (候选项列表, 通知消息, 是否成功, 是否需要用户选择)
    /// </summary>
    public async Task<(List<string> candidates, string? message, bool success, bool needUserSelect)>
        ResolveBestMatchWithDeepFallbackAsync(
            string keyword,
            string originalUserMessage,
            IReadOnlyList<DirectoryAccessRule> directoryRules,
            bool announceDeepMatchProgress = true,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return ([], "关键词不能为空", false, false);
        }

        var effectiveRules = directoryRules
            .Where(r => !string.IsNullOrWhiteSpace(r.Path))
            .Select(r => new DirectoryAccessRule
            {
                Path = Path.GetFullPath(r.Path),
                MaxDepth = Math.Clamp(r.MaxDepth, 0, 10),
                AccessMode = r.AccessMode,
                Priority = r.Priority
            })
            .GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => x.Priority).ThenByDescending(x => x.MaxDepth).First())
            .ToList();

        if (effectiveRules.Count == 0)
        {
            return ([], "未配置可访问目录。", false, false);
        }

        var roots = effectiveRules.Select(r => r.Path).ToList();
        var maxDepth = effectiveRules.Max(r => r.MaxDepth);
        var searchKeywords = BuildSearchKeywords(keyword);

        var exactMatches = GetWorkspaceCandidates(
                searchKeywords,
                roots,
                maxDepth,
                minConfidence: 0.0,
                minTextOrPinyinScore: 0.0)
            .Where(x => IsExactTargetNameMatch(Path.GetFileName(x.Path), searchKeywords))
            .Where(x => _isPathAllowed(x.Path) && TryGetRuleForPath(x.Path, effectiveRules, out _))
            .ToList();

        if (exactMatches.Count == 1)
        {
            return ([exactMatches[0].Path], null, true, false);
        }

        if (exactMatches.Count > 1)
        {
            var preferredExact = SelectPreferredCandidate(exactMatches);
            return ([preferredExact.Path], null, true, false);
        }

        var dbCandidates = GetWorkspaceCandidates(
            searchKeywords,
            roots,
            maxDepth,
            minConfidence: 0.5,
            minTextOrPinyinScore: 0.0);

        var allowedDb = dbCandidates
            .Where(x => _isPathAllowed(x.Path) && TryGetRuleForPath(x.Path, effectiveRules, out _))
            .ToList();

        if (allowedDb.Count == 1)
        {
            return ([allowedDb[0].Path], null, true, false);
        }

        if (allowedDb.Count > 1)
        {
            var best = SelectPreferredCandidate(allowedDb);
            return ([best.Path], null, true, false);
        }

        if (announceDeepMatchProgress)
        {
            _postChatMessage("字符/读音匹配失败，Fairy 将尝试智能深度匹配...");
        }

        var allCandidates = GetWorkspaceCandidates(
            searchKeywords,
            roots,
            maxDepth,
            minConfidence: 0.0,
            minTextOrPinyinScore: 0.0)
            .Where(x => _isPathAllowed(x.Path))
            .Select(x =>
            {
                if (!TryGetRuleForPath(x.Path, effectiveRules, out var matchedRule) || matchedRule is null)
                {
                    return null;
                }

                return new LlmDeepMatchCandidate
                {
                    Path = x.Path,
                    IsDirectory = x.IsDirectory,
                    Priority = matchedRule.Priority
                };
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        if (allCandidates.Count == 0)
        {
            return ([], "深度匹配失败：索引中未找到任何候选项，请检查工作区索引是否已构建。", false, false);
        }

        var deepResult = await _llmChatCapability.DeepMatchAsync(
            _llmEndpoint,
            originalUserMessage,
            allCandidates,
            cancellationToken);

        if (!deepResult.HasMatches)
        {
            return ([], BuildDeepMatchFailureMessage(deepResult.RawReply), false, false);
        }

        var sb = new StringBuilder();
        sb.AppendLine("🔍 深度匹配结果，请选择操作目标：");
        for (var index = 0; index < deepResult.Matches.Count; index++)
        {
            var match = deepResult.Matches[index];
            var name = Path.GetFileName(match.Path);
            var type = Directory.Exists(match.Path) ? "📁" : "📄";
            sb.AppendLine($"{index + 1}. {type} {name}（置信度 {match.Confidence:P0}）");
            sb.AppendLine($"   路径：{match.Path}");
        }

        var matchedPaths = deepResult.Matches.Select(match => match.Path).ToList();
        return (matchedPaths, sb.ToString().TrimEnd(), true, deepResult.Matches.Count > 1);
    }

    private List<WorkspaceIndexMatchResult> GetWorkspaceCandidates(
        IReadOnlyList<string> keywords,
        IReadOnlyList<string> roots,
        int maxDepth,
        double minConfidence,
        double minTextOrPinyinScore)
    {
        var merged = new Dictionary<string, WorkspaceIndexMatchResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var searchKeyword in keywords)
        {
            var matches = _workspaceIndexCapability.GetMatchCandidates(
                searchKeyword,
                roots,
                maxDepth,
                minConfidence,
                minTextOrPinyinScore);

            foreach (var match in matches)
            {
                if (!merged.TryGetValue(match.Path, out var existing)
                    || match.Confidence > existing.Confidence)
                {
                    merged[match.Path] = match;
                }
            }
        }

        return merged.Values
            .OrderByDescending(x => x.Confidence)
            .ToList();
    }

    private WorkspaceIndexMatchResult SelectPreferredCandidate(IReadOnlyList<WorkspaceIndexMatchResult> candidates)
    {
        return candidates
            .Select(x =>
            {
                var habitScore = _habitCapability
                    .GetMatchCandidates(Path.GetFileName(x.Path), _settings.HabitItems, minConfidence: 0.0)
                    .FirstOrDefault(h => string.Equals(h.Path, x.Path, StringComparison.OrdinalIgnoreCase));
                var combined = x.Confidence * 0.6 + (habitScore is not null
                    ? Math.Min(1.0, (double)habitScore.OpenCount / Math.Max(1, habitScore.OpenCount + 10)) * 0.4
                    : 0.0);
                return (Candidate: x, Combined: combined);
            })
            .OrderByDescending(x => x.Combined)
            .Select(x => x.Candidate)
            .First();
    }

    private static List<string> BuildSearchKeywords(string keyword)
    {
        var variants = new List<string>();
        AddKeywordVariant(variants, keyword);

        var trimmed = keyword.Trim();
        var unwrapped = trimmed.Trim('"', '\'', '“', '”', '‘', '’', '《', '》', '「', '」', '『', '』');
        AddKeywordVariant(variants, unwrapped);

        var stripped = StripExplicitTargetDescriptors(unwrapped);
        AddKeywordVariant(variants, stripped);

        return variants;
    }

    private static void AddKeywordVariant(List<string> variants, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var normalized = candidate.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        if (!variants.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            variants.Add(normalized);
        }
    }

    private static string StripExplicitTargetDescriptors(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return string.Empty;
        }

        var candidate = keyword.Trim();
        foreach (var prefix in ExplicitFileTargetPrefixes)
        {
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && candidate.Length > prefix.Length)
            {
                candidate = candidate[prefix.Length..].Trim();
                break;
            }
        }

        foreach (var suffix in ExplicitFileTargetSuffixes)
        {
            if (candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && candidate.Length > suffix.Length)
            {
                candidate = candidate[..^suffix.Length].Trim();
                break;
            }
        }

        return candidate;
    }

    private static bool IsExactTargetNameMatch(string candidateName, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(candidateName))
        {
            return false;
        }

        var normalizedCandidate = NormalizeTargetName(candidateName);
        return keywords.Any(keyword => string.Equals(normalizedCandidate, NormalizeTargetName(keyword), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeTargetName(string text)
    {
        return StripExplicitTargetDescriptors(text)
            .Trim('"', '\'', '“', '”', '‘', '’', '《', '》', '「', '」', '『', '』')
            .Trim();
    }

    private static string BuildDeepMatchFailureMessage(string? rawReply)
    {
        const string genericMessage = "深度匹配失败：智能匹配未能找到符合条件的文件/文件夹，请尝试更换描述方式。";
        if (string.IsNullOrWhiteSpace(rawReply))
        {
            return genericMessage;
        }

        var trimmed = rawReply.Trim();
        if (IsNullOnlyDeepMatchReply(trimmed))
        {
            return genericMessage;
        }

        if (trimmed.Contains("LLM 请求异常：", StringComparison.OrdinalIgnoreCase))
        {
            return $"深度匹配失败：{TrimDiagnosticMessage(trimmed)}";
        }

        return $"深度匹配失败：智能匹配未能返回可用结果。{Environment.NewLine}诊断信息：{TrimDiagnosticMessage(trimmed)}";
    }

    private static bool IsNullOnlyDeepMatchReply(string rawReply)
    {
        var normalized = rawReply
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return normalized.Length > 0
            && normalized.All(line => line.StartsWith("[", StringComparison.Ordinal)
                ? line.EndsWith("NULL", StringComparison.OrdinalIgnoreCase)
                : string.Equals(line, "NULL", StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimDiagnosticMessage(string message, int maxLength = 240)
    {
        var normalized = message.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + "…";
    }

    public FileOpsPlugin(
        IFileAccessCapability fileAccessCapability,
        IHabitCapability habitCapability,
        IWorkspaceIndexCapability workspaceIndexCapability,
        FileOpsSkillSettings settings,
        AgentMode currentMode,
        Func<SkillConfirmationRequest, Task<bool>> confirmAsync,
        Action<string> recordOpen,
        ILlmChatCapability llmChatCapability,
        LlmEndpointConfig llmEndpoint,
        Action<string> postChatMessage)
    {
        _fileAccessCapability = fileAccessCapability;
        _habitCapability = habitCapability;
        _workspaceIndexCapability = workspaceIndexCapability;
        _settings = settings;
        _currentMode = currentMode;
        _confirmAsync = confirmAsync;
        _recordOpen = recordOpen;
        _llmChatCapability = llmChatCapability;
        _llmEndpoint = llmEndpoint;
        _postChatMessage = postChatMessage;

        _isPathAllowed = path =>
            currentMode == AgentMode.Hybrid
            || fileAccessCapability.IsPathAllowed(path, currentMode, settings.AllowedDirectories, settings.DirectoryAccessDepthLimits, settings.DirectoryRules);

        _canWritePath = path =>
            currentMode == AgentMode.Hybrid
            || fileAccessCapability.CanWritePath(path, currentMode, settings.AllowedDirectories, settings.DirectoryAccessDepthLimits, settings.DirectoryRules);
    }

    public async Task<(bool Handled, bool Success, string? Message, PendingFileAction? PendingAction)> TryExecuteDirectAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        PendingFileAction BuildPendingAction(
            string action,
            string actionLabel,
            List<string> paths,
            string? newName = null,
            string? destinationPath = null)
        {
            var totalCandidateCount = paths.Count;
            var displayedCandidates = paths
                .Take(PendingFileAction.MaxDisplayedCandidates)
                .ToList();

            return new PendingFileAction
            {
                Action = action,
                ActionLabel = actionLabel,
                TargetPath = displayedCandidates.FirstOrDefault() ?? string.Empty,
                Candidates = displayedCandidates
                    .Select((path, index) => new PendingCandidate
                    {
                        Index = index + 1,
                        Path = path,
                        Confidence = 1.0,
                        IsDirectory = Directory.Exists(path),
                        IsSelected = false
                    })
                    .ToList(),
                TotalCandidateCount = totalCandidateCount,
                NewName = newName,
                DestinationPath = destinationPath,
                SourceSkillName = "FileOps"
            };
        }

        var parsed = ParseIntent(input);
        if (parsed is null)
        {
            return (false, false, null, null);
        }

        if (string.Equals(parsed.Action, "open", StringComparison.OrdinalIgnoreCase))
        {
            var (resolved, matchMsg, ok, needSelect) = await ResolveBestMatchWithDeepFallbackAsync(
                parsed.Path!, input, GetAllowedDirectoryRules(), announceDeepMatchProgress: false, cancellationToken);

            if (!ok)
            {
                return (true, false, matchMsg ?? "路径解析失败", null);
            }

            if (needSelect)
            {
                return (true, false, matchMsg, BuildPendingAction("open", parsed.ActionLabel!, resolved));
            }

            var target = resolved[0];
            if (!_isPathAllowed(target))
            {
                return (true, false, $"权限不足：当前模式不允许访问 {target}", null);
            }

            var result = _fileAccessCapability.Open(target);
            _recordOpen(target);
            return (true, true, result, null);
        }

        if (string.Equals(parsed.Action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            var (resolved, matchMsg, ok, needSelect) = await ResolveBestMatchWithDeepFallbackAsync(
                parsed.Path!, input, GetAllowedDirectoryRules(), announceDeepMatchProgress: false, cancellationToken);

            if (!ok)
            {
                return (true, false, matchMsg ?? "路径解析失败", null);
            }

            if (needSelect)
            {
                return (true, false, matchMsg, BuildPendingAction("delete", parsed.ActionLabel!, resolved));
            }

            var target = resolved[0];
            if (!_canWritePath(target))
            {
                return (true, false, $"权限不足：当前模式不允许删除 {target}", null);
            }

            var confirmed = await _confirmAsync(new SkillConfirmationRequest
            {
                SourceSkillName = "FileOps",
                Title = "确认删除",
                Message = $"将要删除以下对象并移入回收站：\n{target}\n\n请确认是否继续？",
                ConfirmText = "确认删除",
                CancelText = "取消",
                IsDestructive = true
            });
            if (!confirmed)
            {
                return (true, false, $"用户已取消删除：{target}", null);
            }

            return (true, true, _fileAccessCapability.Delete(target), null);
        }

        if (string.Equals(parsed.Action, "rename", StringComparison.OrdinalIgnoreCase))
        {
            var (resolved, matchMsg, ok, needSelect) = await ResolveBestMatchWithDeepFallbackAsync(
                parsed.Path!, input, GetAllowedDirectoryRules(), announceDeepMatchProgress: false, cancellationToken);

            if (!ok)
            {
                return (true, false, matchMsg ?? "路径解析失败", null);
            }

            if (needSelect)
            {
                return (true, false, matchMsg, BuildPendingAction("rename", parsed.ActionLabel!, resolved, parsed.NewName));
            }

            var source = resolved[0];
            if (!_canWritePath(source))
            {
                return (true, false, $"权限不足：当前模式不允许重命名 {source}", null);
            }

            return (true, true, _fileAccessCapability.Rename(source, parsed.NewName!), null);
        }

        if (string.Equals(parsed.Action, "copy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parsed.Action, "move", StringComparison.OrdinalIgnoreCase))
        {
            var (resolved, matchMsg, ok, needSelect) = await ResolveBestMatchWithDeepFallbackAsync(
                parsed.Path!, input, GetAllowedDirectoryRules(), announceDeepMatchProgress: false, cancellationToken);

            if (!ok)
            {
                return (true, false, matchMsg ?? "路径解析失败", null);
            }

            if (needSelect)
            {
                return (true, false, matchMsg, BuildPendingAction(parsed.Action!, parsed.ActionLabel!, resolved, destinationPath: parsed.DestinationPath));
            }

            var source = resolved[0];
            if (!_isPathAllowed(source))
            {
                return (true, false, $"权限不足：当前模式不允许访问 {source}", null);
            }

            if (string.IsNullOrWhiteSpace(parsed.DestinationPath) || !_canWritePath(parsed.DestinationPath))
            {
                return (true, false, $"权限不足：当前模式不允许写入 {parsed.DestinationPath}", null);
            }

            if (string.Equals(parsed.Action, "move", StringComparison.OrdinalIgnoreCase) && !_canWritePath(source))
            {
                return (true, false, $"权限不足：当前模式不允许移动 {source}", null);
            }

            var result = string.Equals(parsed.Action, "copy", StringComparison.OrdinalIgnoreCase)
                ? _fileAccessCapability.Copy(source, parsed.DestinationPath)
                : _fileAccessCapability.Move(source, parsed.DestinationPath);
            return (true, true, result, null);
        }

        return (false, false, null, null);
    }

    private IReadOnlyList<DirectoryAccessRule> GetAllowedDirectoryRules()
    {
        if (_settings.DirectoryRules.TryGetValue(_currentMode, out var rules) && rules.Count > 0)
        {
            return rules;
        }

        if (_settings.AllowedDirectories.TryGetValue(_currentMode, out var allowedDirectories) && allowedDirectories.Count > 0)
        {
            var maxDepth = _settings.DirectoryAccessDepthLimits.TryGetValue(_currentMode, out var depthLimit)
                ? depthLimit
                : 3;

            return allowedDirectories
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(path => new DirectoryAccessRule
                {
                    Path = path,
                    MaxDepth = maxDepth,
                    AccessMode = DirectoryAccessMode.ReadOnly,
                    Priority = DirectoryPriority.Medium
                })
                .ToList();
        }

        return [];
    }

    private static FileIntentAnalysis? ParseIntent(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();

        var openMatch = Regex.Match(trimmed, @"^(打开|打开一下|请打开|帮我打开)(?<path>.+)$", RegexOptions.IgnoreCase);
        if (openMatch.Success && ContainsExplicitFileTarget(trimmed))
        {
            return new FileIntentAnalysis
            {
                Action = "open",
                ActionLabel = "打开",
                Path = openMatch.Groups["path"].Value.Trim()
            };
        }

        var deleteMatch = Regex.Match(trimmed, @"^(删除|移除)(?<path>.+)$", RegexOptions.IgnoreCase);
        if (deleteMatch.Success)
        {
            return new FileIntentAnalysis
            {
                Action = "delete",
                ActionLabel = "删除",
                Path = deleteMatch.Groups["path"].Value.Trim()
            };
        }

        var renameMatch = Regex.Match(trimmed, @"^(把)?(?<path>.+?)(重命名|改名)(为|成)(?<newName>.+)$", RegexOptions.IgnoreCase);
        if (renameMatch.Success)
        {
            return new FileIntentAnalysis
            {
                Action = "rename",
                ActionLabel = "重命名",
                Path = renameMatch.Groups["path"].Value.Trim(),
                NewName = renameMatch.Groups["newName"].Value.Trim()
            };
        }

        var copyMatch = Regex.Match(trimmed, @"^(复制|拷贝)(?<path>.+?)(到|去)(?<target>.+)$", RegexOptions.IgnoreCase);
        if (copyMatch.Success)
        {
            return new FileIntentAnalysis
            {
                Action = "copy",
                ActionLabel = "复制",
                Path = copyMatch.Groups["path"].Value.Trim(),
                DestinationPath = copyMatch.Groups["target"].Value.Trim()
            };
        }

        var moveMatch = Regex.Match(trimmed, @"^(移动|挪到|移到)(?<path>.+?)(到|去)(?<target>.+)$", RegexOptions.IgnoreCase);
        if (moveMatch.Success)
        {
            return new FileIntentAnalysis
            {
                Action = "move",
                ActionLabel = "移动",
                Path = moveMatch.Groups["path"].Value.Trim(),
                DestinationPath = moveMatch.Groups["target"].Value.Trim()
            };
        }

        return null;
    }

    private static bool ContainsExplicitFileTarget(string input)
    {
        return input.Contains("文件夹", StringComparison.OrdinalIgnoreCase)
            || input.Contains("文件", StringComparison.OrdinalIgnoreCase)
            || input.Contains("目录", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetRuleForPath(
        string path,
        IReadOnlyList<DirectoryAccessRule> rules,
        out DirectoryAccessRule? matchedRule)
    {
        var candidate = rules
            .Select(rule => new
            {
                Rule = rule,
                Depth = CalculateRelativeDepth(rule.Path, path)
            })
            .Where(x => x.Depth <= x.Rule.MaxDepth)
            .OrderBy(x => x.Rule.Priority)
            .ThenBy(x => x.Depth)
            .FirstOrDefault();

        if (candidate is null)
        {
            matchedRule = null;
            return false;
        }

        matchedRule = candidate.Rule;
        return true;
    }

    private static int CalculateRelativeDepth(string rootPath, string fullPath)
    {
        if (string.Equals(rootPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var root = rootPath.TrimEnd(Path.DirectorySeparatorChar);
        var current = fullPath.TrimEnd(Path.DirectorySeparatorChar);

        if (!current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return int.MaxValue;
        }

        var relativePath = current.Substring(root.Length + 1);
        return relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public IReadOnlyList<ToolInvocationRecord> ConsumeInvokedTools()
    {
        var copy = _invoked.ToArray();
        _invoked.Clear();
        return copy;
    }

    [KernelFunction("open_file_or_folder")]
    [Description("打开指定的文件或文件夹。优先传入 path（完整路径），否则传入 keyword（文件/文件夹名称关键词）由系统智能匹配。")]
    public async Task<string> OpenFileOrFolder(
        [Description("完整路径（已知时优先使用）")] string? path = null,
        [Description("文件或文件夹名称关键词（不知道完整路径时使用）")] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
            {
                if (!_isPathAllowed(path))
                {
                    return Record("open_file_or_folder", false, $"权限不足：当前模式不允许访问 {path}");
                }

                var result = _fileAccessCapability.Open(path);
                _recordOpen(path);
                return Record("open_file_or_folder", true, result);
            }

            var resolvedKeyword = string.IsNullOrWhiteSpace(keyword) ? path : keyword;
            if (string.IsNullOrWhiteSpace(resolvedKeyword))
            {
                return Record("open_file_or_folder", false, "未提供路径或关键词。");
            }

            if (!ContainsExplicitFileTarget(resolvedKeyword)
                && ConversationRoutingHeuristics.IsKnownDesktopApplicationAlias(resolvedKeyword))
            {
                return Record(
                    "open_file_or_folder",
                    false,
                    $"“{resolvedKeyword.Trim()}”更像应用程序名称，不是文件或文件夹。请改用桌面应用打开流程，例如直接执行“打开{resolvedKeyword.Trim()}”应用。");
            }

            var (resolved, matchMessage, ok, needSelect) = await ResolveBestMatchWithDeepFallbackAsync(
                resolvedKeyword, resolvedKeyword, GetAllowedDirectoryRules(), cancellationToken: cancellationToken);

            if (!ok)
            {
                return Record("open_file_or_folder", false, matchMessage ?? "路径解析失败");
            }

            if (needSelect)
            {
                _postChatMessage(matchMessage!);
                return Record("open_file_or_folder", false,
                    $"找到多个匹配项，已向用户展示选择列表，请等待用户选择后再执行。候选路径：{string.Join("；", resolved)}");
            }

            if (matchMessage is not null)
            {
                _postChatMessage(matchMessage);
            }

            var target = resolved[0];
            if (!_isPathAllowed(target))
            {
                return Record("open_file_or_folder", false, $"权限不足：当前模式不允许访问 {target}");
            }

            var finalResult = _fileAccessCapability.Open(target);
            _recordOpen(target);
            return Record("open_file_or_folder", true, finalResult);
        }
        catch (Exception ex)
        {
            return Record("open_file_or_folder", false, $"打开失败：{ex.Message}");
        }
    }

    [KernelFunction("create_folder")]
    [Description("在指定目录下创建新文件夹。")]
    public string CreateFolder(
        [Description("父目录完整路径")] string baseDir,
        [Description("新文件夹名称")] string name)
    {
        try
        {
            if (!_canWritePath(baseDir))
            {
                return Record("create_folder", false, $"权限不足：当前模式不允许在 {baseDir} 创建文件夹");
            }

            return Record("create_folder", true, _fileAccessCapability.CreateFolder(baseDir, name));
        }
        catch (Exception ex)
        {
            return Record("create_folder", false, $"创建文件夹失败：{ex.Message}");
        }
    }

    [KernelFunction("delete_file_or_folder")]
    [Description("将文件或文件夹移入回收站。操作前会弹窗确认，用户拒绝则取消。")]
    public async Task<string> DeleteFileOrFolder(
        [Description("要删除的完整路径或名称关键词")] string pathOrKeyword,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (TryRejectAmbiguousFileTarget("delete_file_or_folder", pathOrKeyword, "删除", out var rejection))
            {
                return rejection;
            }

            string target;
            if (Path.IsPathRooted(pathOrKeyword) && (File.Exists(pathOrKeyword) || Directory.Exists(pathOrKeyword)))
            {
                target = pathOrKeyword;
            }
            else
            {
                var (resolved, matchMessage, ok, needSelect) = await ResolveBestMatchWithDeepFallbackAsync(
                    pathOrKeyword, pathOrKeyword, GetAllowedDirectoryRules(), cancellationToken: cancellationToken);

                if (!ok)
                {
                    return Record("delete_file_or_folder", false, matchMessage ?? "路径解析失败");
                }

                if (needSelect)
                {
                    _postChatMessage(matchMessage!);
                    return Record("delete_file_or_folder", false,
                        $"找到多个匹配项，已向用户展示选择列表，请等待用户选择后再执行。候选路径：{string.Join("；", resolved)}");
                }

                if (matchMessage is not null)
                {
                    _postChatMessage(matchMessage);
                }

                target = resolved[0];
            }

            if (!_canWritePath(target))
            {
                return Record("delete_file_or_folder", false, $"权限不足：当前模式不允许删除 {target}");
            }

            var confirmed = await _confirmAsync(new SkillConfirmationRequest
            {
                SourceSkillName = "FileOps",
                Title = "确认删除",
                Message = $"将要删除以下对象并移入回收站：\n{target}\n\n请确认是否继续？",
                ConfirmText = "确认删除",
                CancelText = "取消",
                IsDestructive = true
            });
            if (!confirmed)
            {
                return Record("delete_file_or_folder", false, $"用户已取消删除：{target}");
            }

            return Record("delete_file_or_folder", true, _fileAccessCapability.Delete(target));
        }
        catch (Exception ex)
        {
            return Record("delete_file_or_folder", false, $"删除失败：{ex.Message}");
        }
    }

    [KernelFunction("rename_file_or_folder")]
    [Description("重命名文件或文件夹。")]
    public async Task<string> RenameFileOrFolder(
        [Description("源完整路径或名称关键词")] string sourcePathOrKeyword,
        [Description("新名称（不含路径）")] string newName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (TryRejectAmbiguousFileTarget("rename_file_or_folder", sourcePathOrKeyword, "重命名", out var rejection))
            {
                return rejection;
            }

            string source;
            if (Path.IsPathRooted(sourcePathOrKeyword) && (File.Exists(sourcePathOrKeyword) || Directory.Exists(sourcePathOrKeyword)))
            {
                source = sourcePathOrKeyword;
            }
            else
            {
                var (resolved, matchMessage, ok, needSelect) = await ResolveBestMatchWithDeepFallbackAsync(
                    sourcePathOrKeyword, sourcePathOrKeyword, GetAllowedDirectoryRules(), cancellationToken: cancellationToken);

                if (!ok)
                {
                    return Record("rename_file_or_folder", false, matchMessage ?? "路径解析失败");
                }

                if (needSelect)
                {
                    _postChatMessage(matchMessage!);
                    return Record("rename_file_or_folder", false,
                        $"找到多个匹配项，已向用户展示选择列表，请等待用户选择后再执行。候选路径：{string.Join("；", resolved)}");
                }

                if (matchMessage is not null)
                {
                    _postChatMessage(matchMessage);
                }

                source = resolved[0];
            }

            if (!_canWritePath(source))
            {
                return Record("rename_file_or_folder", false, $"权限不足：当前模式不允许重命名 {source}");
            }

            return Record("rename_file_or_folder", true, _fileAccessCapability.Rename(source, newName));
        }
        catch (Exception ex)
        {
            return Record("rename_file_or_folder", false, $"重命名失败：{ex.Message}");
        }
    }

    [KernelFunction("copy_file_or_folder")]
    [Description("复制文件或文件夹到目标目录。")]
    public async Task<string> CopyFileOrFolder(
        [Description("源完整路径或名称关键词")] string sourcePathOrKeyword,
        [Description("目标目录完整路径")] string targetDir,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (TryRejectAmbiguousFileTarget("copy_file_or_folder", sourcePathOrKeyword, "复制", out var rejection))
            {
                return rejection;
            }

            string source;
            if (Path.IsPathRooted(sourcePathOrKeyword) && (File.Exists(sourcePathOrKeyword) || Directory.Exists(sourcePathOrKeyword)))
            {
                source = sourcePathOrKeyword;
            }
            else
            {
                var (resolved, matchMessage, ok, needSelect) = await ResolveBestMatchWithDeepFallbackAsync(
                    sourcePathOrKeyword, sourcePathOrKeyword, GetAllowedDirectoryRules(), cancellationToken: cancellationToken);

                if (!ok)
                {
                    return Record("copy_file_or_folder", false, matchMessage ?? "路径解析失败");
                }

                if (needSelect)
                {
                    _postChatMessage(matchMessage!);
                    return Record("copy_file_or_folder", false,
                        $"找到多个匹配项，已向用户展示选择列表，请等待用户选择后再执行。候选路径：{string.Join("；", resolved)}");
                }

                if (matchMessage is not null)
                {
                    _postChatMessage(matchMessage);
                }

                source = resolved[0];
            }

            if (!_isPathAllowed(source))
            {
                return Record("copy_file_or_folder", false, $"权限不足：当前模式不允许访问 {source}");
            }

            if (!_canWritePath(targetDir))
            {
                return Record("copy_file_or_folder", false, $"权限不足：当前模式不允许写入 {targetDir}");
            }

            return Record("copy_file_or_folder", true, _fileAccessCapability.Copy(source, targetDir));
        }
        catch (Exception ex)
        {
            return Record("copy_file_or_folder", false, $"复制失败：{ex.Message}");
        }
    }

    [KernelFunction("move_file_or_folder")]
    [Description("移动文件或文件夹到目标目录。")]
    public async Task<string> MoveFileOrFolder(
        [Description("源完整路径或名称关键词")] string sourcePathOrKeyword,
        [Description("目标目录完整路径")] string targetDir,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (TryRejectAmbiguousFileTarget("move_file_or_folder", sourcePathOrKeyword, "移动", out var rejection))
            {
                return rejection;
            }

            string source;
            if (Path.IsPathRooted(sourcePathOrKeyword) && (File.Exists(sourcePathOrKeyword) || Directory.Exists(sourcePathOrKeyword)))
            {
                source = sourcePathOrKeyword;
            }
            else
            {
                var (resolved, matchMessage, ok, needSelect) = await ResolveBestMatchWithDeepFallbackAsync(
                    sourcePathOrKeyword, sourcePathOrKeyword, GetAllowedDirectoryRules(), cancellationToken: cancellationToken);

                if (!ok)
                {
                    return Record("move_file_or_folder", false, matchMessage ?? "路径解析失败");
                }

                if (needSelect)
                {
                    _postChatMessage(matchMessage!);
                    return Record("move_file_or_folder", false,
                        $"找到多个匹配项，已向用户展示选择列表，请等待用户选择后再执行。候选路径：{string.Join("；", resolved)}");
                }

                if (matchMessage is not null)
                {
                    _postChatMessage(matchMessage);
                }

                source = resolved[0];
            }

            if (!_canWritePath(source))
            {
                return Record("move_file_or_folder", false, $"权限不足：当前模式不允许移动 {source}");
            }

            if (!_canWritePath(targetDir))
            {
                return Record("move_file_or_folder", false, $"权限不足：当前模式不允许写入 {targetDir}");
            }

            return Record("move_file_or_folder", true, _fileAccessCapability.Move(source, targetDir));
        }
        catch (Exception ex)
        {
            return Record("move_file_or_folder", false, $"移动失败：{ex.Message}");
        }
    }

    private string Record(string tool, bool success, string message)
    {
        _invoked.Add(new ToolInvocationRecord
        {
            ToolName = tool,
            Success = success,
            ResultSummary = message
        });
        return message;
    }

    private bool TryRejectAmbiguousFileTarget(string toolName, string? pathOrKeyword, string actionDisplayName, out string result)
    {
        if (!RequiresExplicitFileReference(pathOrKeyword))
        {
            result = Record(
                toolName,
                false,
                $"“{pathOrKeyword?.Trim()}”不是明确的文件或文件夹目标。仅当用户明确提到文件/文件夹、具体路径或带扩展名的名称时，才能执行{actionDisplayName}操作。");
            return true;
        }

        result = string.Empty;
        return false;
    }

    private static bool RequiresExplicitFileReference(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (Path.IsPathRooted(trimmed)
            || trimmed.Contains(Path.DirectorySeparatorChar)
            || trimmed.Contains(Path.AltDirectorySeparatorChar)
            || trimmed.StartsWith(".\\", StringComparison.Ordinal)
            || trimmed.StartsWith("..\\", StringComparison.Ordinal)
            || trimmed.StartsWith("./", StringComparison.Ordinal)
            || trimmed.StartsWith("../", StringComparison.Ordinal)
            || ContainsExplicitFileTarget(trimmed))
        {
            return true;
        }

        return Regex.IsMatch(trimmed, @"\.[A-Za-z0-9]{1,16}$", RegexOptions.CultureInvariant);
    }

    private sealed class FileIntentAnalysis
    {
        public string? Action { get; init; }
        public string? ActionLabel { get; init; }
        public string? Path { get; init; }
        public string? NewName { get; init; }
        public string? DestinationPath { get; init; }
    }
}