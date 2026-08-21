using Lumine.Fairy.Modules.Abstractions;

namespace Lumine.Fairy.Services;

public sealed class ConversationRouteRegistryService
{
    private readonly List<IConversationRouteProvider> _providers = [];

    public void Register(IConversationRouteProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.RemoveAll(existing => string.Equals(existing.RouteId, provider.RouteId, StringComparison.OrdinalIgnoreCase));
        _providers.Add(provider);
    }

    public void RegisterRange(IEnumerable<IConversationRouteProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        foreach (var provider in providers)
        {
            Register(provider);
        }
    }

    public IReadOnlyList<IConversationRouteProvider> GetRegisteredRoutes()
    {
        return _providers
            .OrderBy(provider => provider.Order)
            .ThenBy(provider => provider.RouteId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}