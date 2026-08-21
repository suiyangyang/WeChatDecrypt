namespace Lumine.Fairy.Modules.Abstractions;

public interface IHostRefreshAwareSettingsSection
{
    void OnHostStateChanged();
}