using Avalonia;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Lumine.Fairy.Desktop.Services;

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
        LifecycleLog.Initialize();

        using var instanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            LifecycleLog.NoteExit("检测到已有 Fairy 实例，本次启动直接退出");
            LifecycleLog.Info("检测到已有 Fairy 实例在运行，本次启动直接退出。");
            if (!TryActivateExistingWindow())
            {
                MessageBox.Show(
                    "Fairy 已经在运行，请从系统托盘打开已有实例。",
                    "Fairy",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return;
        }

        try
        {
            LifecycleLog.Info("进入 Avalonia 桌面主循环。");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            LifecycleLog.NoteExit("桌面主循环正常返回（应用生命周期结束）");
        }
        catch (Exception ex)
        {
            LifecycleLog.Crash("桌面主循环", ex, isTerminating: true);
            throw;
        }
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

    private static bool TryActivateExistingWindow()
    {
        var window = FindWindow(null, "Fairy");
        if (window == nint.Zero)
            return false;

        ShowWindow(window, ShowNormal);
        return SetForegroundWindow(window);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    private const int ShowNormal = 9;
}
