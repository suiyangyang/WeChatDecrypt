using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Lumine.Fairy.ContactManagement.Abstractions.Services;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;
using System;
using Lumine.Fairy.TodoList.App.Services;
using Lumine.Fairy.TodoList.App.ViewModels;

namespace Lumine.Fairy.TodoList.App;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var storageOptions = new ContactManagementStorageOptions();
            var validationService = new ContactManagementValidationService();
            var store = new ContactManagementSqliteStore(storageOptions, validationService);
            var workspaceService = new ContactManagementWorkspaceService(store, validationService);
            var snapshot = workspaceService.LoadAsync().GetAwaiter().GetResult();
            var settingsService = new JsonTodoListSettingsService();
            var uploadService = new ReportMindRecentDailyReportUploadService(settingsService);

            var viewModel = new TodoMainWindowViewModel(
                storageOptions.DatabaseFilePath,
                workspaceService,
                snapshot,
                recentDailyReportUploadService: uploadService,
                todoListSettingsService: settingsService);
            viewModel.UpdateStatus("SQLite 数据已加载，可以开始维护 Todo / 日报工作区。");

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            if (Program.SingleInstanceCoordinator is not null)
            {
                Program.SingleInstanceCoordinator.ActivationRequested += HandleActivationRequested;
                Program.SingleInstanceCoordinator.DispatchPendingActivationRequests();
            }

            desktop.Exit += (_, _) =>
            {
                if (Program.SingleInstanceCoordinator is not null)
                {
                    Program.SingleInstanceCoordinator.ActivationRequested -= HandleActivationRequested;
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void HandleActivationRequested(object? sender, string payload)
    {
        if (!string.Equals(payload, AppSingleInstanceCoordinator.ActivateCommand, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow mainWindow })
            {
                EnsureWindowVisible(mainWindow);
            }
        }, DispatcherPriority.Input);
    }

    private static void EnsureWindowVisible(MainWindow mainWindow)
    {
        mainWindow.Show();
        if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
        {
            mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
        }

        mainWindow.Activate();
        NativeWindowActivator.TrySetForegroundWindow(mainWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
    }
}
