using CommunityToolkit.Mvvm.ComponentModel;

namespace SshStudio.ViewModels;

public sealed partial class UnlockViewModel : ObservableObject
{
    [ObservableProperty] private string title = "设置主密码";
    [ObservableProperty] private string description = "主密码用于加密 SSH 密码、私钥密码和 API Key。当前 Windows 账户之后可自动解锁；换机后可用主密码恢复。";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string confirmPassword = "";
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool isSetup = true;
}
