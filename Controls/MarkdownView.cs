using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace SshStudio.Controls;

public sealed partial class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string>(nameof(Markdown), "");

    private static readonly Regex InlinePattern = InlineMarkdownRegex();

    public MarkdownView()
    {
        Spacing = 6;
    }

    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
        {
            RenderMarkdown(change.GetNewValue<string>() ?? "");
        }
    }

    private void RenderMarkdown(string markdown)
    {
        Children.Clear();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inCode = false;
        var codeLines = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    AddCodeBlock(string.Join('\n', codeLines));
                    codeLines.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                }
                continue;
            }

            if (inCode)
            {
                codeLines.Add(raw);
                continue;
            }

            AddMarkdownLine(line);
        }

        if (inCode && codeLines.Count > 0)
        {
            AddCodeBlock(string.Join('\n', codeLines));
        }
    }

    private void AddMarkdownLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            Children.Add(new Border { Height = 2 });
            return;
        }

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("### ", StringComparison.Ordinal))
        {
            Children.Add(CreateText(trimmed[4..], 14, FontWeight.Bold));
            return;
        }
        if (trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            Children.Add(CreateText(trimmed[3..], 15, FontWeight.Bold));
            return;
        }
        if (trimmed.StartsWith("# ", StringComparison.Ordinal))
        {
            Children.Add(CreateText(trimmed[2..], 17, FontWeight.Bold));
            return;
        }
        if (trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            Children.Add(new Border
            {
                BorderBrush = Brush.Parse("#BFD2E2"),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(10, 2, 0, 2),
                Child = CreateText(trimmed[2..], 13, FontWeight.Normal, Brush.Parse("#42566F"))
            });
            return;
        }
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            Children.Add(CreateBullet(trimmed[2..]));
            return;
        }
        if (IsOrderedList(trimmed, out var content))
        {
            Children.Add(CreateBullet(content));
            return;
        }

        Children.Add(CreateText(line, 13, FontWeight.Normal));
    }

    private static TextBlock CreateText(string text, double fontSize, FontWeight weight, IBrush? foreground = null)
    {
        var block = new SelectableTextBlock
        {
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = foreground ?? Brush.Parse("#102033"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21
        };
        AddInlineRuns(block, text);
        return block;
    }

    private static Grid CreateBullet(string text)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        grid.Children.Add(new TextBlock
        {
            Text = "•",
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse("#0F766E"),
            VerticalAlignment = VerticalAlignment.Top
        });
        var content = CreateText(text, 13, FontWeight.Normal);
        content.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private void AddCodeBlock(string code)
    {
        Children.Add(new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Brush.Parse("#0B1625"),
            BorderBrush = Brush.Parse("#1A2B42"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = new SelectableTextBlock
            {
                Text = code.TrimEnd(),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                Foreground = Brush.Parse("#D6E4F0"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            }
        });
    }

    private static void AddInlineRuns(TextBlock block, string text)
    {
        var index = 0;
        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > index)
            {
                block.Inlines!.Add(new Run(text[index..match.Index]));
            }

            var token = match.Value;
            if (token.StartsWith("`", StringComparison.Ordinal))
            {
                block.Inlines!.Add(new Run(token.Trim('`'))
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    Foreground = Brush.Parse("#0F766E")
                });
            }
            else if (token.StartsWith("**", StringComparison.Ordinal))
            {
                block.Inlines!.Add(new Run(token[2..^2]) { FontWeight = FontWeight.Bold });
            }

            index = match.Index + match.Length;
        }

        if (index < text.Length)
        {
            block.Inlines!.Add(new Run(text[index..]));
        }
    }

    private static bool IsOrderedList(string line, out string content)
    {
        var dot = line.IndexOf('.');
        if (dot > 0 && dot < 4 && dot + 1 < line.Length && line[(dot + 1)] == ' ' && line[..dot].All(char.IsDigit))
        {
            content = line[(dot + 2)..];
            return true;
        }

        content = line;
        return false;
    }

    [GeneratedRegex(@"(`[^`]+`|\*\*[^*]+\*\*)")]
    private static partial Regex InlineMarkdownRegex();
}
