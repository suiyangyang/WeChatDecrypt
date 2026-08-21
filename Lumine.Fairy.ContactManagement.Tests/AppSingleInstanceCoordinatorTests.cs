using Lumine.Fairy.ContactManagement.Abstractions.Services;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Lumine.Fairy.ContactManagement.Tests;

public class AppSingleInstanceCoordinatorTests
{
    [Fact]
    public void TryCreatePrimary_ReturnsNull_ForSecondRegistration()
    {
        var applicationId = $"test-{Guid.NewGuid():N}";
        using var primary = AppSingleInstanceCoordinator.TryCreatePrimary(applicationId);

        Assert.NotNull(primary);
        using var duplicate = AppSingleInstanceCoordinator.TryCreatePrimary(applicationId);
        Assert.Null(duplicate);
    }

    [Fact]
    public async Task TrySignalPrimaryInstance_RaisesActivationRequested()
    {
        var applicationId = $"test-{Guid.NewGuid():N}";
        using var primary = AppSingleInstanceCoordinator.TryCreatePrimary(applicationId);
        Assert.NotNull(primary);

        var activationReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary!.ActivationRequested += (_, payload) => activationReceived.TrySetResult(payload);

        var signaled = AppSingleInstanceCoordinator.TrySignalPrimaryInstance(applicationId);

        Assert.True(signaled);
        var payload = await activationReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(AppSingleInstanceCoordinator.ActivateCommand, payload);
    }

    [Fact]
    public async Task DispatchPendingActivationRequests_ReplaysQueuedSignal()
    {
        var applicationId = $"test-{Guid.NewGuid():N}";
        using var primary = AppSingleInstanceCoordinator.TryCreatePrimary(applicationId);
        Assert.NotNull(primary);

        var signaled = AppSingleInstanceCoordinator.TrySignalPrimaryInstance(applicationId);
        Assert.True(signaled);

        string? receivedPayload = null;
        primary!.ActivationRequested += (_, payload) => receivedPayload = payload;

        var startedAt = DateTime.UtcNow;
        while (receivedPayload is null && DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(3))
        {
            primary.DispatchPendingActivationRequests();
            if (receivedPayload is null)
            {
                await Task.Delay(50);
            }
        }

        Assert.Equal(AppSingleInstanceCoordinator.ActivateCommand, receivedPayload);
    }

    [Fact]
    public void BuildPathScopedApplicationId_ReturnsSameId_ForEquivalentWindowsPaths()
    {
        const string applicationId = "test-app";

        var first = AppSingleInstanceCoordinator.BuildPathScopedApplicationId(applicationId, @"E:\Apps\Lumine.Fairy\");
        var second = AppSingleInstanceCoordinator.BuildPathScopedApplicationId(applicationId, @"e:\apps\lumine.fairy");

        Assert.Equal(first, second);
    }

    [Fact]
    public void TryCreatePrimary_AllowsDifferentPaths_ForSameApp()
    {
        const string applicationId = "test-app";
        var firstScopedId = AppSingleInstanceCoordinator.BuildPathScopedApplicationId(applicationId, @"E:\Apps\Lumine.Fairy-A");
        var secondScopedId = AppSingleInstanceCoordinator.BuildPathScopedApplicationId(applicationId, @"E:\Apps\Lumine.Fairy-B");

        using var first = AppSingleInstanceCoordinator.TryCreatePrimary(firstScopedId);
        using var second = AppSingleInstanceCoordinator.TryCreatePrimary(secondScopedId);

        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public void TryAcquireOrActivate_ReturnsNull_ForSecondScopedRegistration()
    {
        var applicationId = $"test-{Guid.NewGuid():N}";
        var baseDirectory = @"E:\Apps\Lumine.Fairy";

        using var primary = AppSingleInstanceStartup.TryAcquireOrActivate(applicationId, baseDirectory);
        Assert.NotNull(primary);

        using var duplicate = AppSingleInstanceStartup.TryAcquireOrActivate(applicationId, baseDirectory);
        Assert.Null(duplicate);
    }
}