using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SshStudio.Controls;
using SshStudio.Models;
using SshStudio.ViewModels;

namespace SshStudio.Views;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _subscribedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SubscribeViewModel();
        Opened += (_, _) => SubscribeViewModel();
        Closed += (_, _) => UnsubscribeViewModel();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void SubscribeViewModel()
    {
        UnsubscribeViewModel();
        _subscribedViewModel = ViewModel;
        if (_subscribedViewModel is null)
        {
            return;
        }

        _subscribedViewModel.TerminalOutputReceived += OnTerminalOutputReceived;
        _subscribedViewModel.TerminalResetRequested += OnTerminalResetRequested;
    }

    private void UnsubscribeViewModel()
    {
        if (_subscribedViewModel is null)
        {
            return;
        }

        _subscribedViewModel.TerminalOutputReceived -= OnTerminalOutputReceived;
        _subscribedViewModel.TerminalResetRequested -= OnTerminalResetRequested;
        _subscribedViewModel = null;
    }

    private void OnTerminalOutputReceived(string text)
    {
        Terminal.FeedOutput(text);
    }

    private void OnTerminalResetRequested()
    {
        Terminal.ResetTerminal();
    }

    private async void OpenHostEditor(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var source = sender is Button { Tag: HostProfile taggedHost } ? taggedHost : null;
        var editor = new HostEditorWindow
        {
            DataContext = HostEditorWindow.Clone(source),
            MainViewModel = ViewModel
        };
        var result = await editor.ShowDialog<bool>(this);
        if (result && editor.Result is not null)
        {
            ViewModel.AddOrUpdateHost(editor.Result, source);
        }
    }

    private async void OpenApiConfig(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var dialog = new ApiConfigWindow
        {
            DataContext = ViewModel.ApiConfig,
            MainViewModel = ViewModel
        };
        var result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            ViewModel.SaveApiConfig();
        }
    }

    private async void OpenSettings(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var model = new SettingsViewModel
        {
            RequireMasterPasswordOnStartup = ViewModel.RequireMasterPasswordOnStartup
        };
        var dialog = new SettingsWindow
        {
            DataContext = model,
            MainViewModel = ViewModel
        };
        await dialog.ShowDialog<bool>(this);
    }

    private void OpenSftpPage(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var window = new SftpWindow
        {
            DataContext = ViewModel
        };
        window.Show(this);
    }

    private async void ReviewPendingCommand(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || string.IsNullOrWhiteSpace(ViewModel.PendingCommand))
        {
            return;
        }

        var command = ViewModel.PendingCommand;
        ViewModel.ClearPendingCommand();
        await ViewModel.ExecuteCommandAsync(command, returnToAi: true);
    }

    private void ClearPendingCommand(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ClearPendingCommand();
    }

    private void DeleteHost(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { Tag: HostProfile host })
        {
            return;
        }

        ViewModel.DeleteHost(host);
    }

    private async void ConnectHostButton(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not Button { Tag: HostProfile host })
        {
            return;
        }

        ViewModel.SelectedHost = host;
        if (ViewModel.ConnectSelectedCommand.CanExecute(null))
        {
            await ViewModel.ConnectSelectedCommand.ExecuteAsync(null);
        }
    }

    private async void ConnectSelectedHost(object? sender, TappedEventArgs e)
    {
        if (ViewModel?.ConnectSelectedCommand.CanExecute(null) == true)
        {
            await ViewModel.ConnectSelectedCommand.ExecuteAsync(null);
        }
    }

    private async void OnTerminalInput(object? sender, TerminalInputEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.WritePtyAsync(e.Text);
        }
    }

    private void OnTerminalResize(object? sender, TerminalResizeEventArgs e)
    {
        ViewModel?.ResizePty(e.Columns, e.Rows, e.Width, e.Height);
    }

    private async void SendShellInput(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var text = ShellInputBox.Text ?? "";
        ShellInputBox.Text = "";
        await ViewModel.SendShellTextAsync(text);
    }
}
