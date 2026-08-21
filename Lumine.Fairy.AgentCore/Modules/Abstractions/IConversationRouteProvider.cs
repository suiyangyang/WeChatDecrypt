using Lumine.Fairy.Models;

namespace Lumine.Fairy.Modules.Abstractions;

public interface IConversationRouteProvider
{
    string RouteId { get; }

    string SkillName { get; }

    int Order { get; }

    bool RequiresLlmFallbackOnFailure { get; }

    int GetMatchScore(string input, SubAgentSkillContext context)
    {
        return Matches(input, context) ? 100 : 0;
    }

    bool Matches(string input, SubAgentSkillContext context);
}