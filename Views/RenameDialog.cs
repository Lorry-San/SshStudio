using Avalonia.Controls;
using Avalonia.Layout;

namespace SshStudio.Views;

public sealed class RenameDialog : Window
{
    private readonly TextBox _nameBox;

    public RenameDialog(string currentName)
    {
        Title = "重命名";
        Width = 420;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _nameBox = new TextBox
        {
            Text = currentName,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };

        var cancel = new Button { Content = "取消" };
        cancel.Click += (_, _) => Close(null);

        var ok = new Button { Content = "确定", Classes = { "primary" } };
        ok.Click += (_, _) => Close(_nameBox.Text);

        Content = new Border
        {
            Padding = new Avalonia.Thickness(18),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "输入新文件名", FontWeight = Avalonia.Media.FontWeight.Bold },
                    _nameBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancel, ok }
                    }
                }
            }
        };
    }
}
