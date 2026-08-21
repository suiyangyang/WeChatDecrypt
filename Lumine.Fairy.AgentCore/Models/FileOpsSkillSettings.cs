using System.Collections.Generic;

namespace Lumine.Fairy.Models;

public sealed class FileOpsSkillSettings
{
    public Dictionary<AgentMode, List<string>> AllowedDirectories { get; set; } = new();
    public Dictionary<AgentMode, int> DirectoryAccessDepthLimits { get; set; } = new();
    public Dictionary<AgentMode, List<DirectoryAccessRule>> DirectoryRules { get; set; } = new();
    public List<HabitItem> HabitItems { get; set; } = new();
}