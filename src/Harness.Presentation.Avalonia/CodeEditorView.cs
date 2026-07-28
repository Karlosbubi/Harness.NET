using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;

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
        };
        if (!string.IsNullOrWhiteSpace(path))
        {
            editor.SyntaxHighlighting = HighlightingManager.Instance
                .GetDefinitionByExtension(Path.GetExtension(path));
        }

        return editor;
    }
}
