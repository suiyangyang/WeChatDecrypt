using System;
using System.Collections.Generic;

namespace Lumine.Fairy.Models;

public sealed class MemoryEpisodeRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceSessionId { get; set; } = string.Empty;
    public string EpisodeType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}