using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SshStudio.Views;

public sealed partial class CommandReviewWindow : Window
{
    public CommandReviewWindow()
    {
        InitializeComponent();
    }

    public string CommandText { get; set; } = "";

    private void Execute(object? sender, RoutedEventArgs e)
    {
        CommandText = CommandBox.Text ?? "";
        Close(true);
    }

    private void Cancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
