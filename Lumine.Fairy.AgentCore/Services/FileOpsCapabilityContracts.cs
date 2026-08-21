using Lumine.Fairy.Models;
using Lumine.Fairy.Modules.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lumine.Fairy.Services;

public interface IFileAccessCapability : ISkillCapability
{
    string Open(string path);

    string CreateFolder(string baseDir, string name);

    string CreateFile(string baseDir, string name);

    string Delete(string path);

    string Rename(string source, string newName);

    string Move(string source, string targetDir);

    string Copy(string source, string targetDir);

    string Paste(string targetDir);

    bool IsPathAllowed(
        string path,
        AgentMode currentMode,
        Dictionary<AgentMode, List<string>> allowedDirectories,
        Dictionary<AgentMode, int>? depthLimits = null,
        Dictionary<AgentMode, List<DirectoryAccessRule>>? directoryRules = null);

    bool CanWritePath(
        string path,
        AgentMode currentMode,
        Dictionary<AgentMode, List<string>> allowedDirectories,
        Dictionary<AgentMode, int>? depthLimits = null,
        Dictionary<AgentMode, List<DirectoryAccessRule>>? directoryRules = null);
}

public interface IHabitCapability : ISkillCapability
{
    void RecordOpen(List<HabitItem> habits, string path);

    HabitItem? FindBestMatch(string keyword, IEnumerable<HabitItem> source);

    List<HabitItem> GetMatchCandidates(string keyword, IEnumerable<HabitItem> source, double minConfidence = 0.8);
}

public interface ILlmChatCapability : ISkillCapability
{
    Task<string> ChatAsync(
        LlmEndpointConfig endpoint,
        IReadOnlyList<ChatMessage> history,
        string userMessage,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default);

    Task<LlmDeepMatchResult> DeepMatchAsync(
        LlmEndpointConfig endpoint,
        string userVoiceMessage,
        IReadOnlyList<LlmDeepMatchCandidate> candidates,
        CancellationToken cancellationToken = default);
}

public interface IWorkspaceIndexCapability : ISkillCapability
{
    void EnsureDatabaseCreated();

    void RebuildIndex(IEnumerable<string> roots, Dictionary<string, int>? depthLimitsByRoot = null);

    void StartWatching(IEnumerable<string> roots);

    void StopWatching();

    List<WorkspaceIndexMatchResult> GetMatchCandidates(
        string keyword,
        IEnumerable<string> roots,
        int maxDepth,
        double minConfidence = 0.5,
        double minTextOrPinyinScore = 0.0);

    void RecordOpen(string path);

    void RecordFeedback(string path, bool success);

    WorkspaceIndexMatcherStatsSnapshot GetMatcherStatsSnapshot(int recentLimit = 20);

    void Dispose();
}
