using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed class CodeSemanticRenderer : IDisposable
{
    private readonly TextEditor editor;
    private readonly SemanticColorizer colorizer;
    private readonly OccurrenceRenderer occurrences;
    private readonly FoldingManager foldingManager;

    internal CodeSemanticRenderer(TextEditor editor)
    {
        this.editor = editor;
        colorizer = new(editor);
        occurrences = new(editor);
        foldingManager = FoldingManager.Install(editor.TextArea);
        editor.TextArea.TextView.LineTransformers.Add(colorizer);
        editor.TextArea.TextView.BackgroundRenderers.Add(occurrences);
    }

    internal int ClassificationCount => colorizer.SegmentCount;
    internal int OccurrenceCount => occurrences.SegmentCount;
    internal int FoldingCount => foldingManager.AllFoldings.Count();

    internal void SetPresentation(WorkbenchCodeDocumentPresentationView presentation)
    {
        colorizer.SetClassifications(presentation.Classifications);
        List<NewFolding> foldings = presentation.FoldingRanges
            .Select(ToFolding)
            .Where(value => value is not null)
            .Cast<NewFolding>()
            .OrderBy(value => value.StartOffset)
            .ThenByDescending(value => value.EndOffset)
            .ToList();
        foldingManager.UpdateFoldings(foldings, firstErrorOffset: -1);
    }

    internal void SetOccurrences(IReadOnlyList<WorkbenchCodeOccurrence> values) =>
        occurrences.SetOccurrences(values);

    internal void ApplyTheme()
    {
        colorizer.ApplyTheme();
        occurrences.ApplyTheme();
    }

    public void Dispose()
    {
        editor.TextArea.TextView.LineTransformers.Remove(colorizer);
        editor.TextArea.TextView.BackgroundRenderers.Remove(occurrences);
        FoldingManager.Uninstall(foldingManager);
    }

    private NewFolding? ToFolding(WorkbenchCodeFoldingRange value)
    {
        int start = Offset(value.Range.Start);
        int end = Offset(value.Range.End);
        if (start < 0 || end <= start || end > editor.Document.TextLength)
            return null;
        return new(start, end)
        {
            Name = value.Display.Value,
            DefaultClosed = value.IsDefaultCollapsed,
        };
    }

    private int Offset(WorkbenchCodePosition position)
    {
        if (position.Line < 0 || position.Line >= editor.Document.LineCount)
            return -1;
        DocumentLine line = editor.Document.GetLineByNumber(position.Line + 1);
        if (position.Character < 0 || position.Character > line.Length)
            return -1;
        return line.Offset + position.Character;
    }

    private sealed class SemanticColorizer(TextEditor editor) : DocumentColorizingTransformer
    {
        private IReadOnlyList<SemanticSegment> segments = [];
        internal int SegmentCount => segments.Count;

        internal void SetClassifications(IReadOnlyList<WorkbenchCodeClassifiedSpan> values)
        {
            segments = values.Select(ToSegment).Where(item => item is not null)
                .Cast<SemanticSegment>().ToArray();
            editor.TextArea.TextView.Redraw();
        }

        internal void ApplyTheme() => editor.TextArea.TextView.Redraw();

        protected override void ColorizeLine(DocumentLine line)
        {
            int lineEnd = line.EndOffset;
            foreach (SemanticSegment segment in segments)
            {
                int start = Math.Max(line.Offset, segment.Offset);
                int end = Math.Min(lineEnd, segment.Offset + segment.Length);
                if (end <= start || Brush(segment.Kind) is not { } brush)
                    continue;
                ChangeLinePart(start, end, element =>
                    element.TextRunProperties.SetForegroundBrush(brush));
            }
        }

        private SemanticSegment? ToSegment(WorkbenchCodeClassifiedSpan value)
        {
            int start = PositionOffset(value.Range.Start);
            int end = PositionOffset(value.Range.End);
            return start < 0 || end <= start ? null : new(start, end - start, value.Kind);
        }

        private int PositionOffset(WorkbenchCodePosition position)
        {
            if (position.Line < 0 || position.Line >= editor.Document.LineCount)
                return -1;
            DocumentLine line = editor.Document.GetLineByNumber(position.Line + 1);
            if (position.Character < 0 || position.Character > line.Length)
                return -1;
            return line.Offset + position.Character;
        }

        private static IBrush? Brush(WorkbenchCodeClassificationKind kind)
        {
            UiThemeColorToken? token = kind switch
            {
                WorkbenchCodeClassificationKind.Keyword or
                    WorkbenchCodeClassificationKind.ControlKeyword => UiThemeColorToken.CodeKeyword,
                WorkbenchCodeClassificationKind.Comment or
                    WorkbenchCodeClassificationKind.DocumentationComment => UiThemeColorToken.CodeComment,
                WorkbenchCodeClassificationKind.String => UiThemeColorToken.CodeString,
                WorkbenchCodeClassificationKind.Number => UiThemeColorToken.CodeNumber,
                WorkbenchCodeClassificationKind.Preprocessor => UiThemeColorToken.CodePreprocessor,
                WorkbenchCodeClassificationKind.Namespace => UiThemeColorToken.CodeNamespace,
                WorkbenchCodeClassificationKind.Type or
                    WorkbenchCodeClassificationKind.TypeParameter => UiThemeColorToken.CodeType,
                WorkbenchCodeClassificationKind.Method => UiThemeColorToken.CodeMethod,
                WorkbenchCodeClassificationKind.Property => UiThemeColorToken.CodeProperty,
                WorkbenchCodeClassificationKind.Field or
                    WorkbenchCodeClassificationKind.Event => UiThemeColorToken.CodeField,
                WorkbenchCodeClassificationKind.Parameter => UiThemeColorToken.CodeParameter,
                WorkbenchCodeClassificationKind.Local => UiThemeColorToken.CodeLocal,
                WorkbenchCodeClassificationKind.Operator or
                    WorkbenchCodeClassificationKind.Punctuation => UiThemeColorToken.CodePunctuation,
                WorkbenchCodeClassificationKind.ExcludedCode => UiThemeColorToken.TextDim,
                _ => null,
            };
            return token is { } value && Application.Current?.TryFindResource(
                HarnessThemeResources.Key(value), out object? resource) is true
                ? resource as IBrush
                : null;
        }
    }

    private sealed class OccurrenceRenderer(TextEditor editor) : IBackgroundRenderer
    {
        private IReadOnlyList<OccurrenceSegment> segments = [];
        internal int SegmentCount => segments.Count;
        public KnownLayer Layer => KnownLayer.Selection;

        internal void SetOccurrences(IReadOnlyList<WorkbenchCodeOccurrence> values)
        {
            segments = values.Select(ToSegment).Where(value => value is not null)
                .Cast<OccurrenceSegment>().ToArray();
            editor.TextArea.TextView.InvalidateLayer(Layer);
        }

        internal void ApplyTheme() => editor.TextArea.TextView.InvalidateLayer(Layer);

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView.Document is null || !textView.VisualLinesValid)
                return;
            foreach (OccurrenceSegment segment in segments)
            {
                IBrush brush = Resource(segment.Kind is WorkbenchCodeOccurrenceKind.Definition
                    ? UiThemeColorToken.AccentSoft
                    : UiThemeColorToken.Hover) ?? Brushes.Transparent;
                foreach (Rect rectangle in BackgroundGeometryBuilder.GetRectsForSegment(
                             textView, new SimpleSegment(segment.Offset, segment.Length)))
                    drawingContext.FillRectangle(brush, rectangle);
            }
        }

        private OccurrenceSegment? ToSegment(WorkbenchCodeOccurrence value)
        {
            int start = PositionOffset(value.Range.Start);
            int end = PositionOffset(value.Range.End);
            return start < 0 || end <= start ? null : new(start, end - start, value.Kind);
        }

        private int PositionOffset(WorkbenchCodePosition position)
        {
            if (position.Line < 0 || position.Line >= editor.Document.LineCount)
                return -1;
            DocumentLine line = editor.Document.GetLineByNumber(position.Line + 1);
            if (position.Character < 0 || position.Character > line.Length)
                return -1;
            return line.Offset + position.Character;
        }

        private static IBrush? Resource(UiThemeColorToken token) =>
            Application.Current?.TryFindResource(HarnessThemeResources.Key(token), out object? value)
                is true ? value as IBrush : null;
    }

    private sealed record SemanticSegment(
        int Offset, int Length, WorkbenchCodeClassificationKind Kind);
    private sealed record OccurrenceSegment(
        int Offset, int Length, WorkbenchCodeOccurrenceKind Kind);
}
