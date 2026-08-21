using System.Collections.Generic;

namespace Lumine.Fairy.Models;

public sealed class MemoryPromptContext
{
    public IReadOnlyList<ChatMessage> RecentConversation { get; init; } = [];
    public string ConversationSummary { get; init; } = string.Empty;
    public string TaskState { get; init; } = string.Empty;
    public string UserProfile { get; init; } = string.Empty;
    public string DomainContext { get; init; } = string.Empty;
    public string RetrievedMemories { get; init; } = string.Empty;
}
