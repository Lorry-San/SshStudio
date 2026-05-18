using Avalonia.Controls;
using Avalonia.Interactivity;
using SshStudio.Models;
using SshStudio.ViewModels;

namespace SshStudio.Views;

public sealed partial class HostEditorWindow : Window
{
    private MainWindowViewModel? _mainViewModel;

    public HostEditorWindow()
    {
        InitializeComponent();
    }

    public HostProfile? Result { get; private set; }

    public MainWindowViewModel? MainViewModel
    {
        get => _mainViewModel;
        set => _mainViewModel = value;
    }

    public static HostProfile Clone(HostProfile? host)
    {
        return host is null
            ? new HostProfile { Port = 22, DefaultPath = "/root" }
            : new HostProfile
            {
                Name = host.Name,
                Address = host.Address,
                Port = host.Port,
                User = host.User,
                Password = host.Password,
                PrivateKey = host.PrivateKey,
                DefaultPath = host.DefaultPath,
                Status = host.Status,
                Cpu = host.Cpu,
                Memory = host.Memory,
                Bandwidth = host.Bandwidth
            };
    }

    private void Save(object? sender, RoutedEventArgs e)
    {
        Result = DataContext as HostProfile;
        Close(true);
    }

    private void Cancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void RevealPassword(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HostProfile host)
        {
            return;
        }

        if (_mainViewModel is not null && !await RequireMasterPasswordAsync(_mainViewModel))
        {
            return;
        }

        var window = new RevealSecretWindow
        {
            DataContext = new RevealSecretViewModel
            {
                Title = "查看 SSH 密码",
                Secret = host.Password
            }
        };
        await window.ShowDialog(this);
    }

    private async Task<bool> RequireMasterPasswordAsync(MainWindowViewModel viewModel)
    {
        var unlock = new UnlockWindow
        {
            IsSetup = !viewModel.VaultExists,
            PasswordVerifier = viewModel.VerifyMasterPassword
        };
        var result = await unlock.ShowDialog<bool>(this);
        return result && unlock.Accepted;
    }
}
