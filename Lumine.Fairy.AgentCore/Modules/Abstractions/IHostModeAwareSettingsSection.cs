using Lumine.Fairy.Models;

namespace Lumine.Fairy.Modules.Abstractions;

public interface IHostModeAwareSettingsSection
{
    void OnHostModeChanged(AgentMode currentMode);
}