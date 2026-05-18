using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using SshStudio.Services;
using SshStudio.ViewModels;
using SshStudio.Views;

namespace SshStudio;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new AppStateStore();
            if (store.TryAutoUnlock())
            {
                ShowMainWindow(desktop, store, showImmediately: false);
            }
            else
            {
                ShowUnlockWindow(desktop, store);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowUnlockWindow(IClassicDesktopStyleApplicationLifetime desktop, AppStateStore store)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var unlock = new UnlockWindow
        {
            IsSetup = !store.VaultExists,
            PasswordVerifier = store.VerifyMasterPassword
        };
        desktop.MainWindow = unlock;
        unlock.Closed += (_, _) =>
        {
            if (!store.IsVaultUnlocked)
            {
                desktop.TryShutdown();
                return;
            }

            ShowMainWindow(desktop, store, showImmediately: true);
        };
    }

    private static void ShowMainWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        AppStateStore store,
        bool showImmediately)
    {
        var main = new MainWindow
        {
            DataContext = new MainWindowViewModel(store)
        };
        desktop.MainWindow = main;
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        if (showImmediately)
        {
            Dispatcher.UIThread.Post(main.Show);
        }
    }
}
