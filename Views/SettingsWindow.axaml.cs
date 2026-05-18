using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SshStudio.ViewModels;

namespace SshStudio.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public MainWindowViewModel? MainViewModel { get; set; }

    private SettingsViewModel? Model => DataContext as SettingsViewModel;

    private void ChangePassword(object? sender, RoutedEventArgs e)
    {
        if (MainViewModel is null || Model is null)
        {
            return;
        }

        if (Model.NewPassword.Length < 6)
        {
            Model.Message = "新主密码至少 6 位。";
            return;
        }

        if (Model.NewPassword != Model.ConfirmNewPassword)
        {
            Model.Message = "两次输入的新主密码不一致。";
            return;
        }

        if (!MainViewModel.ChangeMasterPassword(Model.CurrentPassword, Model.NewPassword))
        {
            Model.Message = "主密码修改失败，请检查当前主密码。";
            return;
        }

        Model.CurrentPassword = "";
        Model.NewPassword = "";
        Model.ConfirmNewPassword = "";
        Model.Message = "主密码已修改，现有 SSH/API 密文已重新加密。";
    }

    private async void ExportConfig(object? sender, RoutedEventArgs e)
    {
        if (MainViewModel is null || Model is null)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出 SshStudio 配置包",
            SuggestedFileName = "sshstudio-config.sspkg.json",
            FileTypeChoices =
            [
                new FilePickerFileType("SshStudio 配置包") { Patterns = ["*.json"] }
            ]
        });

        var path = file?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        MainViewModel.ExportConfigPackage(path);
        Model.Message = "配置包已导出: " + path;
    }

    private async void ImportConfig(object? sender, RoutedEventArgs e)
    {
        if (MainViewModel is null || Model is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "导入 SshStudio 配置包",
            FileTypeFilter =
            [
                new FilePickerFileType("SshStudio 配置包") { Patterns = ["*.json"] }
            ]
        });

        var path = files.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            MainViewModel.ImportConfigPackage(path);
            Model.Message = "配置包已导入，请重启应用并输入主密码。";
        }
        catch (Exception ex)
        {
            Model.Message = "导入失败: " + ex.Message;
        }
    }

    private void CloseWindow(object? sender, RoutedEventArgs e)
    {
        if (MainViewModel is not null && Model is not null)
        {
            MainViewModel.RequireMasterPasswordOnStartup = Model.RequireMasterPasswordOnStartup;
        }

        Close(true);
    }
}
