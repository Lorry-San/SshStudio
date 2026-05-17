using Avalonia.Controls;
using Avalonia.Interactivity;
using SshStudio.ViewModels;

namespace SshStudio.Views;

public sealed partial class RevealSecretWindow : Window
{
    public RevealSecretWindow()
    {
        InitializeComponent();
    }

    private async void Copy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RevealSecretViewModel model && Clipboard is not null)
        {
            await Clipboard.SetTextAsync(model.Secret);
        }
    }

    private void CloseWindow(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
