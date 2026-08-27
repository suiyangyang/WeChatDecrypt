using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Lumine.Fairy.ContactManagement.Abstractions.Services;
using Lumine.Fairy.ContactManagement.App.ImportExport;
using Lumine.Fairy.ContactManagement.App.ViewModels;
using Lumine.Fairy.ContactManagement.Contracts;
using Lumine.Fairy.ContactManagement.Core.Services;
using Lumine.Fairy.ContactManagement.Infrastructure.Options;
using Lumine.Fairy.ContactManagement.Infrastructure.Persistence;
using Lumine.Fairy.ContactManagement.Infrastructure.WeChat;
using Lumine.Fairy.Infrastructure;
using System;

namespace Lumine.Fairy.ContactManagement.App;

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
            IDataImportService dataImportService = new ContactDomainDataImportService(store, storageOptions, validationService);
            IDataExportService dataExportService = new ContactDomainDataExportService(store, storageOptions);
            var snapshot = workspaceService.LoadAsync().GetAwaiter().GetResult();
            var weChatClient = new WeChatDataAnalysisClient();
            var groupChatProvider = new WeChatGroupChatProvider(weChatClient);
            var groupChatDirectoryService = new WeChatGroupChatDirectoryService(store, groupChatProvider);

            var content = new MainWindow
            {
                DataContext = new ContactMainWindowViewModel(storageOptions.DatabaseFilePath, workspaceService, dataImportService, dataExportService, storageOptions.SchemaVersion, snapshot, groupChatDirectoryService)
            };
            desktop.MainWindow = new Window
            {
                Width = 860,
                Height = 540,
                MinWidth = 760,
                MinHeight = 480,
                FontSize = 13,
                Title = "Lumine.Fairy Contact Management",
                Icon = new WindowIcon("Assets/contact-management.ico"),
                Content = content,
            };

            if (Program.SingleInstanceCoordinator is not null)
            {
                Program.SingleInstanceCoordinator.ActivationRequested += HandleActivationRequested;
                Program.SingleInstanceCoordinator.DispatchPendingActivationRequests();
            }

            desktop.Exit += (_, _) =>
            {
                weChatClient.Dispose();
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
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: Window mainWindow })
            {
                EnsureWindowVisible(mainWindow);
            }
        }, DispatcherPriority.Input);
    }

    private static void EnsureWindowVisible(Window mainWindow)
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
