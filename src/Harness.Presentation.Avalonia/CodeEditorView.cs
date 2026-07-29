using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal static class CodeEditorView
{
    internal static TextEditor Create(
        string text = "",
        bool isReadOnly = true,
        bool wordWrap = false,
        bool showLineNumbers = true,
        string? path = null)
    {
        TextEditor editor = new()
        {
            Text = text,
            IsReadOnly = isReadOnly,
            WordWrap = wordWrap,
            ShowLineNumbers = showLineNumbers,
            FontFamily = new FontFamily("Cascadia Code,JetBrains Mono,Consolas,Menlo,monospace"),
            FontSize = 13,
            Options = new TextEditorOptions
            {
                AllowScrollBelowDocument = true,
                EnableRectangularSelection = true,
                HighlightCurrentLine = true,
            },
        };
        if (!string.IsNullOrWhiteSpace(path))
        {
            editor.SyntaxHighlighting = HighlightingManager.Instance
                .GetDefinitionByExtension(Path.GetExtension(path));
            ApplyTheme(editor);
        }

        return editor;
    }

    internal static void ApplyTheme(TextEditor editor)
    {
        if (editor.SyntaxHighlighting is null)
        {
            return;
        }

        foreach (HighlightingColor color in editor.SyntaxHighlighting.NamedHighlightingColors)
        {
            if (Application.Current?.TryFindResource(
                    HarnessThemeResources.Key(Token(color.Name)), out object? value) is true &&
                value is SolidColorBrush brush)
            {
                color.Foreground = new SimpleHighlightingBrush(brush.Color);
            }
        }

        editor.TextArea.TextView.Redraw();
    }

    private static UiThemeColorToken Token(string? name)
    {
        string normalized = name ?? string.Empty;
        if (normalized.Contains("Comment", StringComparison.OrdinalIgnoreCase))
        {
            return UiThemeColorToken.TextDim;
        }

        if (normalized.Contains("String", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Char", StringComparison.OrdinalIgnoreCase))
        {
            return UiThemeColorToken.CodeString;
        }

        if (normalized.Contains("Type", StringComparison.OrdinalIgnoreCase))
        {
            return UiThemeColorToken.CodeType;
        }

        if (normalized.Contains("Number", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Method", StringComparison.OrdinalIgnoreCase))
        {
            return UiThemeColorToken.Info;
        }

        if (normalized.Contains("Preprocessor", StringComparison.OrdinalIgnoreCase))
        {
            return UiThemeColorToken.Warning;
        }

        if (normalized.Contains("Punctuation", StringComparison.OrdinalIgnoreCase))
        {
            return UiThemeColorToken.TextMuted;
        }

        return UiThemeColorToken.CodeKeyword;
    }
}
