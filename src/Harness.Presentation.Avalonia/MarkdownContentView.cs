using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.UI.Avalonia;
using Markdig;
using Markdig.Syntax;

namespace Harness.Presentation.Avalonia;

internal static class MarkdownContentView
{
    internal static Control Create(string content, Func<UiThemeColorToken, IBrush?> brush)
    {
        string source = ConversationContentFormatter.NormalizeSource(content);
        MarkdownDocument document = Markdown.Parse(
            source,
            ConversationContentFormatter.MarkdownPipeline);
        StackPanel blocks = new() { Spacing = 7 };
        foreach (Block block in document)
        {
            string text = BlockText(source, block);
            if (text.Length == 0)
            {
                continue;
            }

            blocks.Children.Add(CreateBlock(block, text, brush));
        }

        if (blocks.Children.Count == 0)
        {
            blocks.Children.Add(new TextBlock
            {
                Text = ConversationContentFormatter.ToReadableText(source),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return blocks;
    }

    private static Control CreateBlock(
        Block block,
        string text,
        Func<UiThemeColorToken, IBrush?> brush)
    {
        TextBlock content = new()
        {
            Text = text,
            TextWrapping = block is CodeBlock ? TextWrapping.NoWrap : TextWrapping.Wrap,
            FontFamily = block is CodeBlock ? new FontFamily("monospace") : FontFamily.Default,
            FontSize = block is HeadingBlock heading
                ? Math.Max(14, 21 - (heading.Level * 1.5))
                : 13,
            FontWeight = block is HeadingBlock ? FontWeight.SemiBold : FontWeight.Normal,
        };

        if (block is CodeBlock)
        {
            return new Border
            {
                Child = new ScrollViewer
                {
                    Content = content,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
                Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(6),
                Background = brush(UiThemeColorToken.Editor),
                BorderBrush = brush(UiThemeColorToken.Border),
                BorderThickness = new Thickness(1),
            };
        }

        if (block is QuoteBlock)
        {
            return new Border
            {
                Child = content,
                Padding = new Thickness(10, 4),
                BorderBrush = brush(UiThemeColorToken.Accent),
                BorderThickness = new Thickness(3, 0, 0, 0),
            };
        }

        return content;
    }

    private static string BlockText(string source, Block block)
    {
        int start = Math.Clamp(block.Span.Start, 0, source.Length);
        int length = Math.Clamp(block.Span.Length, 0, source.Length - start);
        return ConversationContentFormatter.ToReadableText(source.Substring(start, length));
    }
}
