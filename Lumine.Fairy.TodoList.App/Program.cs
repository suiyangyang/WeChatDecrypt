using Avalonia;
using Lumine.Fairy.ContactManagement.Abstractions.Services;

namespace Lumine.Fairy.TodoList.App;

internal static class Program
{
    private const string SingleInstanceAppId = "Lumine.Fairy.TodoList.App";

    internal static AppSingleInstanceCoordinator? SingleInstanceCoordinator { get; private set; }

    [STAThread]
    public static void Main(string[] args)
        => AppSingleInstanceStartup.RunAsSingleInstance(
            SingleInstanceAppId,
            () => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args),
            coordinator => SingleInstanceCoordinator = coordinator,
            AppContext.BaseDirectory);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
