using Lumine.Fairy.Models;
using System.Collections.Generic;

namespace Lumine.Fairy.Services;

public interface IMemorySummaryStore
{
    MemoryConversationSummary? GetLatestSummary(string sessionId);
    void SaveSummary(MemoryConversationSummary summary);
    IReadOnlyList<MemoryConversationSummary> GetRecentSummaries(int take, string? sessionId = null);
}

public interface IMemoryTaskStateStore
{
    MemoryTaskState? GetTaskState(string sessionId);
    void UpsertTaskState(MemoryTaskState taskState);
    IReadOnlyList<MemoryTaskState> GetRecentTaskStates(int take);
}

public interface IMemoryFactStore
{
    IReadOnlyList<MemoryFactRecord> GetFacts(string scope, string category, bool includeExpired = false, bool includeMuted = false);
    void UpsertFact(MemoryFactRecord fact);
}

public interface IMemoryAdminStore
{
    void DeleteFact(string scope, string category, string key);
    void DeleteEpisode(string id);
    void DeleteRelation(string id);
}

public interface IMemoryAuditStore
{
    void SaveWriteAudit(MemoryWriteAuditRecord record);
    IReadOnlyList<MemoryWriteAuditRecord> GetRecentWriteAudits(int take);
}

public interface IMemoryEpisodeStore
{
    IReadOnlyList<MemoryEpisodeRecord> GetRecentEpisodes(int take, string? sessionId = null, string? episodeType = null);
    void SaveEpisode(MemoryEpisodeRecord episode);
}

public interface IMemoryRelationStore
{
    IReadOnlyList<MemoryRelationRecord> GetRecentRelations(int take, string? entity = null);
    void UpsertRelation(MemoryRelationRecord relation);
    void UpsertRelations(string sourceMemoryType, string sourceRecordId, IReadOnlyList<MemoryRelationRecord> relations);
    void DeleteRelationsBySource(string sourceMemoryType, string sourceRecordId);
}

public interface IMemorySummaryService
{
    MemoryConversationSummary? RefreshRollingSummary(string sessionId, IReadOnlyList<ChatMessage> history);
}

public interface IConversationHistoryReader
{
    bool SessionExists(string sessionId);
    IReadOnlyList<ChatMessage> LoadMessages(string sessionId);
}

public interface IMemoryPromptContextBuilder
{
    MemoryPromptContext BuildContext(string sessionId, string currentInput, IReadOnlyList<ChatMessage> fallbackHistory, PersonaProfile? activePersona);
    string BuildAugmentedSystemPrompt(string basePrompt, MemoryPromptContext context);
}

public interface IDomainContextProvider
{
    string BuildDomainContext(string currentInput, PersonaProfile? activePersona);
}

public interface IPersonaMemoryAdapter
{
    IReadOnlyList<MemoryFactRecord> DeriveFacts(PersonaProfile? activePersona);
}

public interface IFactConflictResolver
{
    MemoryFactRecord Resolve(MemoryFactRecord? existing, MemoryFactRecord incoming);
}

public interface IMemoryRetrievalService
{
    IReadOnlyList<MemoryRetrievedItem> Retrieve(MemoryRetrievalQuery query);
}

public interface IMemoryRetrievalSource
{
    IReadOnlyList<MemoryRetrievedItem> GetCandidates(MemoryRetrievalQuery query);
}

public interface IMemoryManagementService
{
    bool TryWriteFact(MemoryFactRecord fact, string triggerReason);
    bool TryWriteEpisode(MemoryEpisodeRecord episode, string triggerReason);
    MemoryMaintenanceResult RunMaintenance(string triggerReason, bool force = false);
    bool UpdateFactGovernance(string scope, string category, string key, bool? isPinned = null, bool? isFrozen = null, bool? isMuted = null);
    bool UpdateRelation(MemoryRelationRecord relation);
    void RecordPromptDebug(MemoryPromptDebugSnapshot snapshot);
    MemoryDashboardSnapshot GetDashboardSnapshot(string? sessionId = null);
    MemoryEvaluationSnapshot GetEvaluationSnapshot(string? sessionId = null);
    void DeleteFact(string scope, string category, string key);
    void DeleteEpisode(string id);
    void DeleteRelation(string id);
}
