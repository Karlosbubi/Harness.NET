using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Harness.BusinessLogic.CodeIntelligence;

namespace Harness.Presentation.Avalonia;

internal sealed class CodeDiagnosticRenderer : IBackgroundRenderer, IDisposable
{
    private readonly TextEditor editor;
    private IReadOnlyList<DiagnosticSegment> segments = [];

    internal CodeDiagnosticRenderer(TextEditor editor)
    {
        this.editor = editor;
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
    }

    public KnownLayer Layer => KnownLayer.Text;
    internal int SegmentCount => segments.Count;

    internal void SetDiagnostics(IReadOnlyList<WorkbenchCodeDiagnostic> diagnostics)
    {
        TextDocument document = editor.Document;
        segments = diagnostics
            .Where(diagnostic => diagnostic.Severity is
                WorkbenchCodeDiagnosticSeverity.Error or
                WorkbenchCodeDiagnosticSeverity.Warning)
            .Select(diagnostic => ToSegment(document, diagnostic))
            .Where(segment => segment is not null)
            .Cast<DiagnosticSegment>()
            .ToArray();
        editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is null || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (DiagnosticSegment segment in segments)
        {
            IBrush brush = segment.Severity is WorkbenchCodeDiagnosticSeverity.Error
                ? Brushes.IndianRed
                : Brushes.Goldenrod;
            Pen pen = new(brush, 1.2);
            foreach (Rect rectangle in BackgroundGeometryBuilder.GetRectsForSegment(
                         textView,
                         new SimpleSegment(segment.Offset, segment.Length)))
            {
                double y = Math.Max(rectangle.Top, rectangle.Bottom - 1.5);
                double x = rectangle.Left;
                bool rising = true;
                while (x < rectangle.Right)
                {
                    double next = Math.Min(rectangle.Right, x + 2);
                    drawingContext.DrawLine(
                        pen,
                        new(x, rising ? y : y + 1.5),
                        new(next, rising ? y + 1.5 : y));
                    rising = !rising;
                    x = next;
                }
            }
        }
    }

    public void Dispose() => editor.TextArea.TextView.BackgroundRenderers.Remove(this);

    private static DiagnosticSegment? ToSegment(
        TextDocument document,
        WorkbenchCodeDiagnostic diagnostic)
    {
        int startLineNumber = diagnostic.Range.Start.Line + 1;
        int endLineNumber = diagnostic.Range.End.Line + 1;
        if (startLineNumber < 1 || startLineNumber > document.LineCount ||
            endLineNumber < 1 || endLineNumber > document.LineCount)
        {
            return null;
        }

        DocumentLine startLine = document.GetLineByNumber(startLineNumber);
        DocumentLine endLine = document.GetLineByNumber(endLineNumber);
        int start = startLine.Offset + Math.Clamp(
            diagnostic.Range.Start.Character,
            0,
            startLine.Length);
        int end = endLine.Offset + Math.Clamp(
            diagnostic.Range.End.Character,
            0,
            endLine.Length);
        int length = Math.Max(1, end - start);
        return new(start, Math.Min(length, document.TextLength - start), diagnostic.Severity);
    }

    private sealed record DiagnosticSegment(
        int Offset,
        int Length,
        WorkbenchCodeDiagnosticSeverity Severity);
}
