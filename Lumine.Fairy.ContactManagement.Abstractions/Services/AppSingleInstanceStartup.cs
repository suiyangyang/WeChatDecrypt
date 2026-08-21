using System;

namespace Lumine.Fairy.ContactManagement.Abstractions.Services;

public static class AppSingleInstanceStartup
{
    public static AppSingleInstanceCoordinator? TryAcquireOrActivate(string applicationId, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        var scopedApplicationId = AppSingleInstanceCoordinator.BuildPathScopedApplicationId(applicationId, baseDirectory);
        var coordinator = AppSingleInstanceCoordinator.TryCreatePrimary(scopedApplicationId);
        if (coordinator is not null)
        {
            return coordinator;
        }

        AppSingleInstanceCoordinator.TrySignalPrimaryInstance(scopedApplicationId);
        return null;
    }

    public static void RunAsSingleInstance(string applicationId, Action runApplication, Action<AppSingleInstanceCoordinator?>? setCoordinator, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentNullException.ThrowIfNull(runApplication);

        var coordinator = TryAcquireOrActivate(applicationId, baseDirectory);
        if (coordinator is null)
        {
            return;
        }

        setCoordinator?.Invoke(coordinator);
        try
        {
            runApplication();
        }
        finally
        {
            setCoordinator?.Invoke(null);
            coordinator.Dispose();
        }
    }
}