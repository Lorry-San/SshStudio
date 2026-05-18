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
    public Func<string, bool>? PasswordVerifier { get; set; }

    public bool IsSetup
    {
        get => (DataContext as UnlockViewModel)?.IsSetup == true;
        set
        {
            var model = DataContext as UnlockViewModel ?? new UnlockViewModel();
            model.IsSetup = value;
            model.Title = value ? "设置主密码" : "输入主密码";
            model.Description = value
                ? "主密码用于加密 SSH 密码、私钥密码和 API Key。当前 Windows 账户之后可自动解锁；换机后可用主密码恢复。"
                : "当前 Windows 账户无法自动解锁，请输入主密码恢复 SSH 密码、私钥密码和 API Key。";
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

        if (PasswordVerifier is not null && !PasswordVerifier(model.Password))
        {
            model.Error = model.IsSetup ? "主密码创建失败。" : "主密码错误。";
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
