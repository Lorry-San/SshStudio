using CommunityToolkit.Mvvm.ComponentModel;

namespace SshStudio.ViewModels;

public sealed partial class UnlockViewModel : ObservableObject
{
    [ObservableProperty] private string title = "设置主密码";
    [ObservableProperty] private string description = "主密码用于加密本机保存的 SSH 密码、私钥密码和 API Key。忘记后无法恢复。";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string confirmPassword = "";
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool isSetup = true;
}
