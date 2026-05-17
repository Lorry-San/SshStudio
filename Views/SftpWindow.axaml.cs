using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SshStudio.Models;
using SshStudio.ViewModels;

namespace SshStudio.Views;

public sealed partial class SftpWindow : Window
{
    public SftpWindow()
    {
        InitializeComponent();
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private async void UploadFile(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "选择要上传的文件"
        });
        var localPath = files.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        await ViewModel.UploadSftpFileAsync(localPath);
    }

    private async void FileGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is null || FileGrid.SelectedItem is not FileItem item)
        {
            return;
        }

        if (IsNameCell(e.Source as Control))
        {
            await RenameFileAsync(item);
            return;
        }

        await DownloadAndOpenAsync(item);
    }

    private async void DownloadSelected(object? sender, RoutedEventArgs e)
    {
        if (FileGrid.SelectedItem is FileItem item)
        {
            await DownloadAndOpenAsync(item);
        }
    }

    private async void RenameSelected(object? sender, RoutedEventArgs e)
    {
        if (FileGrid.SelectedItem is FileItem item)
        {
            await RenameFileAsync(item);
        }
    }

    private async Task DownloadAndOpenAsync(FileItem item)
    {
        if (ViewModel is null)
        {
            return;
        }

        var localPath = await ViewModel.DownloadSftpFileAsync(item);
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
    }

    private async Task RenameFileAsync(FileItem item)
    {
        if (ViewModel is null)
        {
            return;
        }

        var dialog = new RenameDialog(item.Name);
        var result = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(result))
        {
            await ViewModel.RenameSftpFileAsync(item, result);
        }
    }

    private static bool IsNameCell(Control? control)
    {
        while (control is not null)
        {
            if (control.Classes.Contains("sftpName"))
            {
                return true;
            }
            control = control.GetVisualParent<Control>();
        }

        return false;
    }
}
