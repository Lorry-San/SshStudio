using Avalonia.Controls;
using Avalonia.Interactivity;
using SshStudio.Models;
using SshStudio.ViewModels;

namespace SshStudio.Views;

public sealed partial class ApiConfigWindow : Window
{
    private MainWindowViewModel? _mainViewModel;

    public ApiConfigWindow()
    {
        InitializeComponent();
    }

    public ApiConfig? Result { get; private set; }

    public MainWindowViewModel? MainViewModel
    {
        get => _mainViewModel;
        set => _mainViewModel = value;
    }

    private void Save(object? sender, RoutedEventArgs e)
    {
        Result = DataContext as ApiConfig;
        Close(true);
    }

    private void Cancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void RevealApiKey(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ApiConfig config)
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
                Title = "查看 API Key",
                Secret = config.ApiKey
            }
        };
        await window.ShowDialog(this);
    }

    private async Task<bool> RequireMasterPasswordAsync(MainWindowViewModel viewModel)
    {
        var unlock = new UnlockWindow
        {
            IsSetup = !viewModel.VaultExists
        };
        var result = await unlock.ShowDialog<bool>(this);
        return result && unlock.Accepted && viewModel.VerifyMasterPassword(unlock.MasterPassword);
    }
}
