using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Models;

public sealed class MemoryConversationSummary
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;
    public int StartMessageIndex { get; set; }
    public int EndMessageIndex { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public List<string> KeyFacts { get; set; } = [];
    public List<string> OpenQuestions { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}