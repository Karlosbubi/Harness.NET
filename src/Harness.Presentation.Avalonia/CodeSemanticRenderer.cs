using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
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
    private readonly CodeAdornmentGenerator adornments;

    internal CodeSemanticRenderer(TextEditor editor)
    {
        this.editor = editor;
        colorizer = new(editor);
        occurrences = new(editor);
        foldingManager = FoldingManager.Install(editor.TextArea);
        adornments = new(editor);
        adornments.CodeLensInvoked += (_, args) => CodeLensInvoked?.Invoke(this, args);
        editor.TextArea.TextView.LineTransformers.Add(colorizer);
        editor.TextArea.TextView.BackgroundRenderers.Add(occurrences);
        editor.TextArea.TextView.ElementGenerators.Add(adornments);
    }

    internal int ClassificationCount => colorizer.SegmentCount;
    internal int OccurrenceCount => occurrences.SegmentCount;
    internal int FoldingCount => foldingManager.AllFoldings.Count();
    internal int InlayHintCount => adornments.InlayHintCount;
    internal int CodeLensCount => adornments.CodeLensCount;
    internal event EventHandler<WorkbenchCodeLensInvokedEventArgs>? CodeLensInvoked;

    internal void SetPresentation(WorkbenchCodeDocumentPresentationView presentation)
    {
        colorizer.SetClassifications(presentation.Classifications);
        adornments.SetAdornments(presentation.InlayHints, presentation.CodeLenses);
        if (presentation.FoldingRanges.Count == 0 && presentation.Outline.Count == 0)
        {
            return;
        }
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
        editor.TextArea.TextView.ElementGenerators.Remove(adornments);
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

    private sealed class CodeAdornmentGenerator(TextEditor editor) : VisualLineElementGenerator
    {
        private IReadOnlyList<AdornmentGroup> groups = [];
        internal int InlayHintCount { get; private set; }
        internal int CodeLensCount { get; private set; }
        internal event EventHandler<WorkbenchCodeLensInvokedEventArgs>? CodeLensInvoked;

        internal void SetAdornments(
            IReadOnlyList<WorkbenchCodeInlayHint> hints,
            IReadOnlyList<WorkbenchCodeLens> lenses)
        {
            InlayHintCount = hints.Count;
            CodeLensCount = lenses.Count;
            groups = hints.Select(hint => new AdornmentItem(
                    Offset(hint.Position), hint.Label.Value, hint.Tooltip.Value,
                    hint, Lens: null))
                .Concat(lenses.Select(lens => new AdornmentItem(
                    Offset(lens.Position), lens.Display.Value, lens.Display.Value,
                    Hint: null, lens)))
                .Where(item => item.Offset >= 0 && item.Offset <= editor.Document.TextLength)
                .GroupBy(item => item.Offset)
                .OrderBy(group => group.Key)
                .Select(group => new AdornmentGroup(group.Key, group.ToArray()))
                .ToArray();
            editor.TextArea.TextView.Redraw();
        }

        public override int GetFirstInterestedOffset(int startOffset) =>
            groups.FirstOrDefault(group => group.Offset >= startOffset)?.Offset ?? -1;

        public override VisualLineElement? ConstructElement(int offset)
        {
            AdornmentGroup? group = groups.FirstOrDefault(item => item.Offset == offset);
            if (group is null)
            {
                return null;
            }

            StackPanel content = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };
            foreach (AdornmentItem item in group.Items)
            {
                if (item.Lens is { } lens)
                {
                    Button action = new()
                    {
                        Content = item.Label,
                        FontSize = 10,
                        Padding = new Thickness(3, 0),
                        MinHeight = 0,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                    };
                    AutomationProperties.SetName(action,
                        $"{item.Label} at line {lens.Target.Line + 1}");
                    action.Click += (_, _) => CodeLensInvoked?.Invoke(this,
                        new WorkbenchCodeLensInvokedEventArgs(lens));
                    content.Children.Add(action);
                }
                else
                {
                    Border hint = new()
                    {
                        Background = Resource(UiThemeColorToken.Hover),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(3, 0),
                        IsHitTestVisible = false,
                        Child = new TextBlock
                        {
                            Text = item.Label,
                            FontSize = 10,
                            Foreground = Resource(UiThemeColorToken.TextDim),
                        },
                    };
                    AutomationProperties.SetName(hint, item.Tooltip);
                    ToolTip.SetTip(hint, item.Tooltip);
                    content.Children.Add(hint);
                }
            }
            return new InlineObjectElement(0, content);
        }

        private int Offset(WorkbenchCodePosition position)
        {
            if (position.Line < 0 || position.Line >= editor.Document.LineCount)
            {
                return -1;
            }
            DocumentLine line = editor.Document.GetLineByNumber(position.Line + 1);
            return position.Character < 0 || position.Character > line.Length
                ? -1
                : line.Offset + position.Character;
        }

        private sealed record AdornmentItem(
            int Offset,
            string Label,
            string Tooltip,
            WorkbenchCodeInlayHint? Hint,
            WorkbenchCodeLens? Lens);
        private sealed record AdornmentGroup(int Offset, IReadOnlyList<AdornmentItem> Items);
    }

    private static IBrush? Resource(UiThemeColorToken token) =>
        Application.Current?.TryFindResource(HarnessThemeResources.Key(token), out object? value)
            is true ? value as IBrush : null;

    private sealed record SemanticSegment(
        int Offset, int Length, WorkbenchCodeClassificationKind Kind);
    private sealed record OccurrenceSegment(
        int Offset, int Length, WorkbenchCodeOccurrenceKind Kind);
}

internal sealed class WorkbenchCodeLensInvokedEventArgs(
    WorkbenchCodeLens lens) : EventArgs
{
    internal WorkbenchCodeLens Lens { get; } = lens;
}
