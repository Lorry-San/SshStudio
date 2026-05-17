using Avalonia.Controls;
using Avalonia.Interactivity;
using SshStudio.ViewModels;

namespace SshStudio.Views;

public sealed partial class UnlockWindow : Window
{
    public UnlockWindow()
    {
        InitializeComponent();
    }

    public string MasterPassword { get; private set; } = "";
    public bool Accepted { get; private set; }

    public bool IsSetup
    {
        get => (DataContext as UnlockViewModel)?.IsSetup == true;
        set
        {
            var model = DataContext as UnlockViewModel ?? new UnlockViewModel();
            model.IsSetup = value;
            model.Title = value ? "设置主密码" : "输入主密码";
            model.Description = value
                ? "主密码用于加密本机保存的 SSH 密码、私钥密码和 API Key。忘记后无法恢复。"
                : "输入主密码解锁本机保存的 SSH 密码、私钥密码和 API Key。";
            DataContext = model;
            if (ConfirmPasswordBox is not null)
            {
                ConfirmPasswordBox.IsVisible = value;
            }
        }
    }

    private void Unlock(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not UnlockViewModel model)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(model.Password) || model.Password.Length < 6)
        {
            model.Error = "主密码至少 6 位。";
            return;
        }

        if (model.IsSetup && model.Password != model.ConfirmPassword)
        {
            model.Error = "两次输入的主密码不一致。";
            return;
        }

        MasterPassword = model.Password;
        Accepted = true;
        Close(true);
    }

    private void Cancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
