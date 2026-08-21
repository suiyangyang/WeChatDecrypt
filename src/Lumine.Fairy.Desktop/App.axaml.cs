using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Lumine.Fairy.Desktop.ViewModels;
using Lumine.Fairy.Desktop.Views;
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
            _tray = new TrayIconService(window, () => desktop.Shutdown(), () => _viewModel.MarkAllReadCommand.ExecuteAsync(null));
            _viewModel.SetDesktopNotificationPresenter(_tray.ShowNotification);
            window.Closing += (_, args) =>
            {
                if (_tray?.AllowExit == true) return;
                args.Cancel = true;
                _tray?.HideWindow();
            };
            desktop.ShutdownRequested += (_, _) => _tray?.Dispose();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
