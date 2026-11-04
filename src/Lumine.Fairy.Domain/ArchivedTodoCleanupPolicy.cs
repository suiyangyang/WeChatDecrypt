using Lumine.Fairy.Protocol;

namespace Lumine.Fairy.Domain;

/// <summary>
/// Shared rules used by the periodic cleanup of archived tasks.
/// </summary>
public static class ArchivedTodoCleanupPolicy
{
    public static DateTimeOffset GetCutoff(DateTimeOffset now, int retentionDays)
        => now.AddDays(-WorkSettings.NormalizeArchivedRetentionDays(retentionDays));

    public static DateTimeOffset? GetArchivedAt(TodoRecord todo)
        => todo.ArchivedAt ?? todo.UpdatedAt ?? todo.CompletedAt ?? todo.CreatedAt;

    public static bool IsCleanupCandidate(TodoRecord todo, DateTimeOffset cutoff)
        => todo.IsArchived
           && GetArchivedAt(todo) is { } archivedAt
           && archivedAt < cutoff;

    public static IReadOnlyList<TodoRecord> SelectCleanupCandidates(IEnumerable<TodoRecord> todos, DateTimeOffset cutoff)
        => todos.Where(todo => IsCleanupCandidate(todo, cutoff)).ToList();
}