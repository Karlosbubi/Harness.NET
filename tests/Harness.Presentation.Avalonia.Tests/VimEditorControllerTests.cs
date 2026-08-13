using Avalonia.Controls;
using Avalonia.Input;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Editor;

namespace Harness.Presentation.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class VimEditorControllerTests
{
    [Fact]
    public void Normal_insert_visual_counts_operators_and_undo_share_one_live_buffer()
    {
        FakeEditor editor = new("one two\nthree four\nfive\n");
        VimEditorController vim = new(editor, EditorInputMode.Vim);

        Assert.Equal(VimEditorMode.Normal, vim.Mode);
        Assert.True(Press(vim, Key.W));
        Assert.Equal(4, editor.CaretOffset);

        Assert.True(Press(vim, Key.D));
        Assert.Contains("d", vim.StatusText, StringComparison.Ordinal);
        Assert.True(Press(vim, Key.W));
        Assert.Equal("one three four\nfive\n", editor.Text);
        Assert.Equal("two\n", editor.ClipboardText);

        Assert.True(Press(vim, Key.U));
        Assert.Equal("one two\nthree four\nfive\n", editor.Text);

        editor.CaretOffset = 0;
        Assert.True(Press(vim, Key.D2));
        Assert.True(Press(vim, Key.D));
        Assert.True(Press(vim, Key.D));
        Assert.Equal("five\n", editor.Text);
        Assert.Equal("one two\nthree four\n", editor.ClipboardText);
        Assert.True(Press(vim, Key.P, KeyModifiers.Shift));
        Assert.Equal("one two\nthree four\nfive\n", editor.Text);

        Assert.True(Press(vim, Key.I));
        Assert.Equal(VimEditorMode.Insert, vim.Mode);
        editor.Insert(editor.CaretOffset, "new ");
        editor.CaretOffset += 4;
        Assert.True(Press(vim, Key.Escape));
        Assert.Equal(VimEditorMode.Normal, vim.Mode);

        editor.CaretOffset = 0;
        Assert.True(Press(vim, Key.V));
        Assert.Equal(VimEditorMode.Visual, vim.Mode);
        Assert.True(Press(vim, Key.E));
        Assert.True(editor.SelectionLength > 0);
        Assert.True(Press(vim, Key.Y));
        Assert.Equal(VimEditorMode.Normal, vim.Mode);
        Assert.NotEmpty(editor.ClipboardText);

        editor.Text = "one\ntwo\nthree\n";
        Assert.True(Press(vim, Key.V, KeyModifiers.Shift));
        Assert.True(Press(vim, Key.J));
        Assert.True(Press(vim, Key.Y));
        Assert.Equal("one\ntwo\n", editor.ClipboardText);
    }

    [Fact]
    public void Counts_before_and_after_an_operator_are_multiplied()
    {
        FakeEditor editor = new("a b c d e f g");
        VimEditorController vim = new(editor, EditorInputMode.Vim);

        Assert.True(Press(vim, Key.D2));
        Assert.True(Press(vim, Key.D));
        Assert.True(Press(vim, Key.D3));
        Assert.True(Press(vim, Key.W));

        Assert.Equal("g", editor.Text);
        Assert.Equal("a b c d e f ", editor.ClipboardText);
    }

    [Fact]
    public void Composition_and_platform_shortcuts_are_not_stolen()
    {
        FakeEditor editor = new("alpha") { IsTextCompositionActive = true };
        VimEditorController vim = new(editor, EditorInputMode.Vim);
        Assert.True(Press(vim, Key.I));

        Assert.False(Press(vim, Key.Escape));
        Assert.False(vim.ShouldHandle(Args(Key.Escape)));
        Assert.Equal(VimEditorMode.Insert, vim.Mode);
        Assert.False(Press(vim, Key.C, KeyModifiers.Control));
        Assert.False(Press(vim, Key.F4, KeyModifiers.Alt));

        editor.IsTextCompositionActive = false;
        Assert.True(vim.ShouldHandle(Args(Key.OemOpenBrackets, KeyModifiers.Control)));
        Assert.True(Press(vim, Key.OemOpenBrackets, KeyModifiers.Control));
        Assert.Equal(VimEditorMode.Normal, vim.Mode);
    }

    [Fact]
    public void Read_only_documents_allow_navigation_but_reject_modal_mutation()
    {
        FakeEditor editor = new("alpha beta") { IsReadOnly = true };
        VimEditorController vim = new(editor, EditorInputMode.Vim);

        Assert.True(Press(vim, Key.W));
        Assert.Equal(6, editor.CaretOffset);
        Assert.True(Press(vim, Key.X));
        Assert.Equal("alpha beta", editor.Text);

        vim.SetInputMode(EditorInputMode.Standard);
        Assert.Equal(VimEditorMode.Inactive, vim.Mode);
        Assert.False(Press(vim, Key.H));
    }

    private static bool Press(
        VimEditorController controller,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None) => controller.Handle(Args(key, modifiers));

    private static KeyEventArgs Args(
        Key key,
        KeyModifiers modifiers = KeyModifiers.None) => new()
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        };

    private sealed class FakeEditor : IWorkbenchEditorAdapter
    {
        private readonly Stack<(string Text, int Caret)> undo = [];
        private readonly Stack<(string Text, int Caret)> redo = [];
        private string text;
        private int caretOffset;

        internal FakeEditor(string text) => this.text = text;

        public Control Control { get; } = new TextBox();
        public string Text { get => text; set { text = value; caretOffset = 0; } }
        public bool IsReadOnly { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int SelectionLength { get; private set; }
        public int SelectionStart { get; private set; }
        public string SelectedText => text.Substring(SelectionStart, SelectionLength);
        public bool IsTextCompositionActive { get; set; }
        public int LineCount => Lines().Length;
        public int TextLength => text.Length;
        public int CaretOffset
        {
            get => caretOffset;
            set => caretOffset = Math.Clamp(value, 0, TextLength);
        }
        public WorkbenchCodePosition CaretPosition => GetPosition(CaretOffset);
        public WorkbenchCodeRange? SelectionRange => SelectionLength == 0
            ? null
            : new(GetPosition(SelectionStart), GetPosition(SelectionStart + SelectionLength));
        internal string ClipboardText { get; private set; } = string.Empty;

        public event EventHandler? TextChanged { add { } remove { } }
        public event EventHandler? CaretChanged { add { } remove { } }
        public event EventHandler? ViewportChanged { add { } remove { } }
        public event EventHandler<KeyEventArgs>? KeyDown { add { } remove { } }
        public event EventHandler<TextInputEventArgs>? TextEntered { add { } remove { } }
        public event EventHandler<WorkbenchEditorPasteEventArgs>? TextPasted { add { } remove { } }
        public event EventHandler<WorkbenchEditorPointerEventArgs>? PointerPositionChanged { add { } remove { } }
        public event EventHandler? PointerExited { add { } remove { } }
        public event EventHandler<WorkbenchCodeLensInvokedEventArgs>? CodeLensInvoked { add { } remove { } }

        public int GetOffset(WorkbenchCodePosition position)
        {
            string[] lines = Lines();
            int line = Math.Clamp(position.Line, 0, lines.Length - 1);
            int offset = 0;
            for (int index = 0; index < line; index++) offset += lines[index].Length + 1;
            return Math.Min(TextLength, offset + Math.Clamp(position.Character, 0, lines[line].Length));
        }

        public WorkbenchCodePosition GetPosition(int offset)
        {
            offset = Math.Clamp(offset, 0, TextLength);
            int line = 0;
            int lineStart = 0;
            for (int index = 0; index < offset; index++)
            {
                if (text[index] != '\n') continue;
                line++;
                lineStart = index + 1;
            }
            return new(line, offset - lineStart);
        }

        public char GetCharAt(int offset) => text[offset];
        public void Replace(int offset, int length, string value)
        {
            RecordUndo();
            text = text.Remove(offset, length).Insert(offset, value);
            caretOffset = Math.Min(caretOffset, text.Length);
        }
        public void Insert(int offset, string value)
        {
            RecordUndo();
            text = text.Insert(offset, value);
        }
        public void Select(int offset, int length)
        {
            SelectionStart = Math.Clamp(offset, 0, TextLength);
            SelectionLength = Math.Clamp(length, 0, TextLength - SelectionStart);
        }
        public void Undo()
        {
            if (!undo.TryPop(out (string Text, int Caret) previous)) return;
            redo.Push((text, caretOffset));
            (text, caretOffset) = previous;
        }
        public void Redo()
        {
            if (!redo.TryPop(out (string Text, int Caret) next)) return;
            undo.Push((text, caretOffset));
            (text, caretOffset) = next;
        }
        public ValueTask CopyToClipboardAsync(string value)
        {
            ClipboardText = value;
            return ValueTask.CompletedTask;
        }
        public void SetCaretPosition(WorkbenchCodePosition position) => CaretOffset = GetOffset(position);
        public void ScrollTo(WorkbenchCodePosition position) { }
        public void Focus() { }
        public void ApplyTheme() { }
        public void SetDiagnostics(IReadOnlyList<WorkbenchCodeDiagnostic> diagnostics) { }
        public void SetDocumentPresentation(WorkbenchCodeDocumentPresentationView presentation) { }
        public void SetOccurrences(IReadOnlyList<WorkbenchCodeOccurrence> occurrences) { }
        public WorkbenchCodeRange? GetVisibleRange() => null;
        public void Dispose() { }

        private string[] Lines() => text.Split('\n');
        private void RecordUndo()
        {
            undo.Push((text, caretOffset));
            redo.Clear();
        }
    }
}
