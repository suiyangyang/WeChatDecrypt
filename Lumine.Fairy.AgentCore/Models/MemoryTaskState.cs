using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Models;

public sealed class MemoryTaskState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;
    public string TaskKind { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> CompletedSteps { get; set; } = [];
    public List<string> PendingSteps { get; set; } = [];
    public List<string> ToolResultSummaries { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}