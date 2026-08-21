using Avalonia;
using System;
using System.Threading;
using System.Windows.Forms;

namespace Lumine.Fairy.Desktop;

sealed class Program
{
    private const string SingleInstanceMutexName = "Local\\Lumine.Fairy.Desktop";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        using var instanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "QwenPaw Remote PC 已经在运行。请使用系统托盘中的已有实例；本次启动已退出。",
                "QwenPaw Remote PC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
