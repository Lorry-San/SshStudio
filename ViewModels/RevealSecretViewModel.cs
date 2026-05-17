using CommunityToolkit.Mvvm.ComponentModel;

namespace SshStudio.ViewModels;

public sealed partial class RevealSecretViewModel : ObservableObject
{
    [ObservableProperty] private string title = "查看密码";
    [ObservableProperty] private string secret = "";
}
