using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;

namespace Harness.Presentation.Avalonia;

internal sealed class WorkbenchEditorPointerEventArgs(
    WorkbenchCodePosition? position) : EventArgs
{
    internal WorkbenchCodePosition? Position { get; } = position;
}

/// <summary>
/// Presentation-owned boundary for the live user buffer. Third-party editor objects stay
/// behind the adapter; Business Logic continues to receive typed text and positions only.
/// </summary>
internal interface IWorkbenchEditorAdapter : IDisposable
{
    Control Control { get; }
    string Text { get; set; }
    bool IsReadOnly { get; set; }
    bool IsEnabled { get; set; }
    int SelectionLength { get; }
    int LineCount { get; }
    int TextLength { get; }
    int CaretOffset { get; set; }
    WorkbenchCodePosition CaretPosition { get; }
    WorkbenchCodeRange? SelectionRange { get; }

    event EventHandler? TextChanged;
    event EventHandler? CaretChanged;
    event EventHandler? ViewportChanged;
    event EventHandler<KeyEventArgs>? KeyDown;
    event EventHandler<TextInputEventArgs>? TextEntered;
    event EventHandler<WorkbenchEditorPointerEventArgs>? PointerPositionChanged;
    event EventHandler? PointerExited;
    event EventHandler<WorkbenchCodeLensInvokedEventArgs>? CodeLensInvoked;

    int GetOffset(WorkbenchCodePosition position);
    char GetCharAt(int offset);
    void Replace(int offset, int length, string text);
    void Insert(int offset, string text);
    void SetCaretPosition(WorkbenchCodePosition position);
    void ScrollTo(WorkbenchCodePosition position);
    void Focus();
    void ApplyTheme();
    void SetDiagnostics(IReadOnlyList<WorkbenchCodeDiagnostic> diagnostics);
    void SetDocumentPresentation(WorkbenchCodeDocumentPresentationView presentation);
    void SetOccurrences(IReadOnlyList<WorkbenchCodeOccurrence> occurrences);
    WorkbenchCodeRange? GetVisibleRange();
}

internal sealed class AvaloniaEditWorkbenchEditorAdapter : IWorkbenchEditorAdapter
{
    private readonly CodeDiagnosticRenderer diagnostics;
    private readonly CodeSemanticRenderer semantics;
    private WorkbenchCodeRange? lastVisibleRange;

    internal AvaloniaEditWorkbenchEditorAdapter(WorkbenchDocumentView view)
    {
        NativeEditor = CodeEditorView.Create(
            view.Content.Value,
            isReadOnly: view.Access is not WorkbenchDocumentAccess.Editable,
            wordWrap: false,
            showLineNumbers: true,
            path: view.Path.Value);
        diagnostics = new(NativeEditor);
        semantics = new(NativeEditor);
        semantics.CodeLensInvoked += (_, args) => CodeLensInvoked?.Invoke(this, args);
        NativeEditor.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        NativeEditor.TextArea.Caret.PositionChanged += (_, _) =>
            CaretChanged?.Invoke(this, EventArgs.Empty);
        NativeEditor.TextArea.TextView.VisualLinesChanged += (_, _) =>
        {
            WorkbenchCodeRange? current = GetVisibleRange();
            if (current == lastVisibleRange)
                return;
            lastVisibleRange = current;
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        };
        NativeEditor.KeyDown += (_, args) => KeyDown?.Invoke(this, args);
        NativeEditor.TextArea.TextEntered += (_, args) => TextEntered?.Invoke(this, args);
        NativeEditor.PointerMoved += (_, args) =>
        {
            var position = NativeEditor.GetPositionFromPoint(args.GetPosition(NativeEditor));
            PointerPositionChanged?.Invoke(this, new(position is null
                ? null
                : new(position.Value.Line - 1, position.Value.Column - 1)));
        };
        NativeEditor.PointerExited += (_, _) => PointerExited?.Invoke(this, EventArgs.Empty);
    }

    internal TextEditor NativeEditor { get; }
    public Control Control => NativeEditor;
    public string Text { get => NativeEditor.Text; set => NativeEditor.Text = value; }
    public bool IsReadOnly { get => NativeEditor.IsReadOnly; set => NativeEditor.IsReadOnly = value; }
    public bool IsEnabled { get => NativeEditor.IsEnabled; set => NativeEditor.IsEnabled = value; }
    public int SelectionLength => NativeEditor.SelectionLength;
    public int LineCount => NativeEditor.Document.LineCount;
    public int TextLength => NativeEditor.Document.TextLength;
    public int CaretOffset
    {
        get => NativeEditor.TextArea.Caret.Offset;
        set => NativeEditor.TextArea.Caret.Offset = Math.Clamp(value, 0, TextLength);
    }
    public WorkbenchCodePosition CaretPosition => new(
        NativeEditor.TextArea.Caret.Line - 1,
        NativeEditor.TextArea.Caret.Column - 1);
    public WorkbenchCodeRange? SelectionRange
    {
        get
        {
            if (NativeEditor.SelectionLength == 0)
                return null;
            var start = NativeEditor.Document.GetLocation(NativeEditor.SelectionStart);
            var end = NativeEditor.Document.GetLocation(
                NativeEditor.SelectionStart + NativeEditor.SelectionLength);
            return new(
                new(start.Line - 1, start.Column - 1),
                new(end.Line - 1, end.Column - 1));
        }
    }

    public event EventHandler? TextChanged;
    public event EventHandler? CaretChanged;
    public event EventHandler? ViewportChanged;
    public event EventHandler<KeyEventArgs>? KeyDown;
    public event EventHandler<TextInputEventArgs>? TextEntered;
    public event EventHandler<WorkbenchEditorPointerEventArgs>? PointerPositionChanged;
    public event EventHandler? PointerExited;
    public event EventHandler<WorkbenchCodeLensInvokedEventArgs>? CodeLensInvoked;

    public int GetOffset(WorkbenchCodePosition position)
    {
        int line = Math.Clamp(position.Line + 1, 1, LineCount);
        var documentLine = NativeEditor.Document.GetLineByNumber(line);
        int character = Math.Clamp(position.Character, 0, documentLine.Length);
        return documentLine.Offset + character;
    }

    public char GetCharAt(int offset) => NativeEditor.Document.GetCharAt(offset);
    public void Replace(int offset, int length, string text) =>
        NativeEditor.Document.Replace(offset, length, text);
    public void Insert(int offset, string text) => NativeEditor.Document.Insert(offset, text);

    public void SetCaretPosition(WorkbenchCodePosition position)
    {
        NativeEditor.TextArea.Caret.Offset = GetOffset(position);
    }

    public void ScrollTo(WorkbenchCodePosition position)
    {
        var location = NativeEditor.Document.GetLocation(GetOffset(position));
        NativeEditor.ScrollTo(location.Line, location.Column);
    }
    public void Focus() => _ = NativeEditor.Focus();
    public void ApplyTheme()
    {
        CodeEditorView.ApplyTheme(NativeEditor);
        semantics.ApplyTheme();
    }
    public void SetDiagnostics(IReadOnlyList<WorkbenchCodeDiagnostic> values) =>
        diagnostics.SetDiagnostics(values);
    public void SetDocumentPresentation(WorkbenchCodeDocumentPresentationView presentation) =>
        semantics.SetPresentation(presentation);
    public void SetOccurrences(IReadOnlyList<WorkbenchCodeOccurrence> values) =>
        semantics.SetOccurrences(values);
    public WorkbenchCodeRange? GetVisibleRange()
    {
        var lines = NativeEditor.TextArea.TextView.VisualLines;
        if (!NativeEditor.TextArea.TextView.VisualLinesValid || lines.Count == 0)
            return null;
        int startLine = lines[0].FirstDocumentLine.LineNumber - 1;
        int endLine = lines[^1].LastDocumentLine.LineNumber - 1;
        var endDocumentLine = NativeEditor.Document.GetLineByNumber(endLine + 1);
        return new(new(startLine, 0), new(endLine, endDocumentLine.Length));
    }
    public void Dispose()
    {
        semantics.Dispose();
        diagnostics.Dispose();
    }
}
