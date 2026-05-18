using CommunityToolkit.Mvvm.ComponentModel;

namespace SshStudio.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool requireMasterPasswordOnStartup;
    [ObservableProperty] private string currentPassword = "";
    [ObservableProperty] private string newPassword = "";
    [ObservableProperty] private string confirmNewPassword = "";
    [ObservableProperty] private string message = "";
}
