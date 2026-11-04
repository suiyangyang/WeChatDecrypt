using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Lumine.Fairy.Desktop.ViewModels;
using Lumine.Fairy.Desktop.Views;
using Lumine.Fairy.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Lumine.Fairy.Desktop;

public partial class App : Avalonia.Application
{
    private TrayIconService? _tray;
    private ViewModels.MainWindowViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new ViewModels.MainWindowViewModel();
            var window = new MainWindow
            {
                DataContext = _viewModel,
            };
            _tray = new TrayIconService(
                window,
                () =>
                {
                    LifecycleLog.NoteExit("用户从托盘菜单选择退出");
                    desktop.Shutdown();
                },
                () => _viewModel.MarkAllReadCommand.ExecuteAsync(null),
                () =>
                {
                    _viewModel.NavigateToPageCommand.Execute("3");
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                },
                BuiltInAppLauncher.LaunchTodoList);
            _viewModel.SetDesktopNotificationPresenter(_tray.ShowNotification);
            window.Closing += (_, args) =>
            {
                if (_tray?.AllowExit == true)
                {
                    LifecycleLog.Info("主窗口随托盘退出流程关闭。");
                    return;
                }

                args.Cancel = true;
                LifecycleLog.Info("主窗口关闭转为隐藏到托盘，进程继续运行。");
                _tray?.HideWindow();
            };
            desktop.ShutdownRequested += (_, _) =>
            {
                LifecycleLog.NoteExit("Avalonia 请求关闭应用（ShutdownRequested）");
                LifecycleLog.Info("收到 ShutdownRequested，正在释放托盘图标。");
                _tray?.Dispose();
            };
            desktop.MainWindow = window;
            LifecycleLog.Info("界面初始化完成，主窗口已创建。");
            LifecycleLog.StartHeartbeat(TimeSpan.FromMinutes(1));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
