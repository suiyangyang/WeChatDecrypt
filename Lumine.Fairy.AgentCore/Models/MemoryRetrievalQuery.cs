using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Models;

public sealed class MemoryRetrievalQuery
{
    public string Input { get; init; } = string.Empty;
    public string CurrentSessionId { get; init; } = string.Empty;
    public string CurrentProject { get; init; } = string.Empty;
    public int TopK { get; init; } = 5;
    public int MaxContentLength { get; init; } = 1200;
    public DateTimeOffset? StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Types { get; init; } = [];
}