using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private const int MaximumClassifiedSpans = 20_000;
    private const int MaximumStructureItems = 5_000;
    private const int MaximumOccurrences = 2_000;

    public async ValueTask<CodeIntelligenceDocumentPresentationResult>
        GetDocumentPresentationAsync(
            CodeIntelligenceDocumentPresentationRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveSession? session = MatchingSession(request.Snapshot);
        if (session is null)
        {
            return PresentationFailure(request, "session_unavailable",
                "The Roslyn session no longer matches this source context.",
                CodeIntelligenceResultState.Stale);
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, request.Snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return PresentationFailure(request, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value, prepared.State);
            }

            if (!TryGetTextSpan(prepared.Text!, request.VisibleRange, out TextSpan visibleSpan))
            {
                return PresentationFailure(request, "invalid_visible_range",
                    "The requested visible range is outside the active document buffer.");
            }

            Document document = prepared.Document!;
            SourceText text = prepared.Text!;
            SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
            if (root is null)
            {
                return PresentationFailure(request, "syntax_unavailable",
                    "Roslyn did not produce a syntax tree for the active document.",
                    CodeIntelligenceResultState.Degraded);
            }

            ClassifiedSpan[] classified = (await Classifier.GetClassifiedSpansAsync(
                document, visibleSpan, cancellationToken)).ToArray();
            bool truncated = classified.Length > MaximumClassifiedSpans;
            CodeIntelligenceClassifiedSpan[] classifications = classified
                .Take(MaximumClassifiedSpans)
                .Select(item => new CodeIntelligenceClassifiedSpan(
                    Range(text, item.TextSpan), MapClassification(item.ClassificationType)))
                .ToArray();

            List<OutlineEntry> outlineEntries = BuildOutline(root);
            List<CodeIntelligenceFoldingRange> folding = BuildFolding(root, text);
            truncated |= outlineEntries.Count > MaximumStructureItems ||
                         folding.Count > MaximumStructureItems;
            CodeIntelligenceOutlineItem[] outline = outlineEntries
                .Take(MaximumStructureItems)
                .Select(item => new CodeIntelligenceOutlineItem(
                    item.Kind,
                    new(Bound(item.Display, MaximumIssueLength)),
                    Range(text, item.Span),
                    Range(text, item.SelectionSpan),
                    item.Depth))
                .ToArray();
            CodeIntelligenceBreadcrumb[] breadcrumbs = outlineEntries
                .Where(item => item.Span.Contains(prepared.Offset))
                .OrderBy(item => item.Depth)
                .ThenByDescending(item => item.Span.Length)
                .Take(32)
                .Select(item => new CodeIntelligenceBreadcrumb(
                    item.Kind,
                    new(Bound(item.Display, MaximumIssueLength)),
                    Range(text, item.SelectionSpan)))
                .ToArray();

            return new(
                request.Snapshot.ContextId,
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                SessionState(session),
                classifications,
                folding.Take(MaximumStructureItems).ToArray(),
                outline,
                breadcrumbs,
                truncated,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            return PresentationFailure(request, "document_presentation_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public async ValueTask<CodeIntelligenceOccurrenceResult> FindOccurrencesAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return OccurrenceFailure(snapshot, "session_unavailable",
                "The Roslyn session no longer matches this source context.",
                CodeIntelligenceResultState.Stale);
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return OccurrenceFailure(snapshot, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value, prepared.State);
            }

            Document document = prepared.Document!;
            ISymbol? symbol = await SymbolFinder.FindSymbolAtPositionAsync(
                document, prepared.Offset, cancellationToken);
            if (symbol is null)
            {
                return new(
                    snapshot.ContextId,
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    SessionState(session),
                    null,
                    [],
                    false,
                    session.Issues.ToArray());
            }

            SourceText text = prepared.Text!;
            SyntaxTree? currentTree = await document.GetSyntaxTreeAsync(cancellationToken);
            SyntaxNode? currentRoot = await document.GetSyntaxRootAsync(cancellationToken);
            List<CodeIntelligenceOccurrence> occurrences = [];
            foreach (Location location in symbol.Locations.Where(item =>
                         item.IsInSource && item.SourceTree == currentTree))
            {
                occurrences.Add(new(
                    Range(text, location.SourceSpan),
                    CodeIntelligenceOccurrenceKind.Definition));
            }

            IEnumerable<ReferencedSymbol> references = await SymbolFinder.FindReferencesAsync(
                symbol, document.Project.Solution, cancellationToken);
            foreach (ReferenceLocation reference in references.SelectMany(item => item.Locations))
            {
                if (reference.Document.Id != document.Id)
                {
                    continue;
                }

                occurrences.Add(new(
                    Range(text, reference.Location.SourceSpan),
                    IsWriteReference(reference, currentRoot)
                        ? CodeIntelligenceOccurrenceKind.Write
                        : CodeIntelligenceOccurrenceKind.Read));
            }

            CodeIntelligenceOccurrence[] distinct = occurrences
                .DistinctBy(item => (item.Range, item.Kind))
                .OrderBy(item => item.Range.Start.Line)
                .ThenBy(item => item.Range.Start.Character)
                .ToArray();
            bool truncated = distinct.Length > MaximumOccurrences;
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                new(Bound(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    MaximumIssueLength)),
                distinct.Take(MaximumOccurrences).ToArray(),
                truncated,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            return OccurrenceFailure(snapshot, "occurrence_lookup_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    private static bool TryGetTextSpan(
        SourceText text,
        CodeIntelligenceRange? requested,
        out TextSpan span)
    {
        if (requested is null)
        {
            span = new(0, text.Length);
            return true;
        }

        try
        {
            int start = text.Lines.GetPosition(new(
                requested.Start.Line, requested.Start.Character));
            int end = text.Lines.GetPosition(new(
                requested.End.Line, requested.End.Character));
            if (start < 0 || end < start || end > text.Length)
            {
                span = default;
                return false;
            }

            span = TextSpan.FromBounds(start, end);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            span = default;
            return false;
        }
    }

    private static CodeIntelligenceClassificationKind MapClassification(string classification)
    {
        string value = classification.ToLowerInvariant();
        if (value.Contains("control keyword", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.ControlKeyword;
        if (value.Contains("keyword", StringComparison.Ordinal))
            return value.Contains("preprocessor", StringComparison.Ordinal)
                ? CodeIntelligenceClassificationKind.Preprocessor
                : CodeIntelligenceClassificationKind.Keyword;
        if (value.Contains("xml doc", StringComparison.Ordinal) ||
            value.Contains("documentation", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.DocumentationComment;
        if (value.Contains("comment", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Comment;
        if (value.Contains("string", StringComparison.Ordinal) ||
            value.Contains("character", StringComparison.Ordinal) ||
            value.Contains("regex", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.String;
        if (value.Contains("numeric", StringComparison.Ordinal) ||
            value.Contains("number", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Number;
        if (value.Contains("preprocessor", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Preprocessor;
        if (value.Contains("namespace", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Namespace;
        if (value.Contains("type parameter", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.TypeParameter;
        if (value.Contains("class name", StringComparison.Ordinal) ||
            value.Contains("record", StringComparison.Ordinal) ||
            value.Contains("struct name", StringComparison.Ordinal) ||
            value.Contains("interface name", StringComparison.Ordinal) ||
            value.Contains("enum name", StringComparison.Ordinal) ||
            value.Contains("delegate name", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Type;
        if (value.Contains("method", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Method;
        if (value.Contains("property", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Property;
        if (value.Contains("enum member", StringComparison.Ordinal) ||
            value.Contains("field", StringComparison.Ordinal) ||
            value.Contains("constant", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Field;
        if (value.Contains("event", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Event;
        if (value.Contains("parameter", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Parameter;
        if (value.Contains("local", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Local;
        if (value.Contains("operator", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Operator;
        if (value.Contains("punctuation", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Punctuation;
        if (value.Contains("identifier", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.Identifier;
        if (value.Contains("excluded", StringComparison.Ordinal))
            return CodeIntelligenceClassificationKind.ExcludedCode;
        return CodeIntelligenceClassificationKind.Text;
    }

    private static List<OutlineEntry> BuildOutline(SyntaxNode root)
    {
        List<OutlineEntry> result = [];
        Visit(root, depth: 0);
        return result;

        void Visit(SyntaxNode node, int depth)
        {
            int childDepth = depth;
            switch (node)
            {
                case BaseNamespaceDeclarationSyntax value:
                    Add(CodeIntelligenceSymbolKind.Namespace, value.Name.ToString(), value.Span,
                        value.Name.Span, depth);
                    childDepth++;
                    break;
                case BaseTypeDeclarationSyntax value:
                    Add(MapTypeKind(value), value.Identifier.ValueText, value.Span,
                        value.Identifier.Span, depth);
                    childDepth++;
                    break;
                case DelegateDeclarationSyntax value:
                    Add(CodeIntelligenceSymbolKind.Delegate,
                        value.Identifier.ValueText + value.ParameterList,
                        value.Span, value.Identifier.Span, depth);
                    childDepth++;
                    break;
                case MethodDeclarationSyntax value:
                    Add(CodeIntelligenceSymbolKind.Method,
                        value.Identifier.ValueText + value.ParameterList,
                        value.Span, value.Identifier.Span, depth);
                    childDepth++;
                    break;
                case ConstructorDeclarationSyntax value:
                    Add(CodeIntelligenceSymbolKind.Constructor,
                        value.Identifier.ValueText + value.ParameterList,
                        value.Span, value.Identifier.Span, depth);
                    childDepth++;
                    break;
                case PropertyDeclarationSyntax value:
                    Add(CodeIntelligenceSymbolKind.Property, value.Identifier.ValueText,
                        value.Span, value.Identifier.Span, depth);
                    childDepth++;
                    break;
                case IndexerDeclarationSyntax value:
                    Add(CodeIntelligenceSymbolKind.Property, "this" + value.ParameterList,
                        value.Span, value.ThisKeyword.Span, depth);
                    childDepth++;
                    break;
                case EventDeclarationSyntax value:
                    Add(CodeIntelligenceSymbolKind.Event, value.Identifier.ValueText,
                        value.Span, value.Identifier.Span, depth);
                    childDepth++;
                    break;
                case FieldDeclarationSyntax value:
                    foreach (VariableDeclaratorSyntax variable in value.Declaration.Variables)
                        Add(value.Modifiers.Any(SyntaxKind.ConstKeyword)
                                ? CodeIntelligenceSymbolKind.Constant
                                : CodeIntelligenceSymbolKind.Field,
                            variable.Identifier.ValueText, value.Span,
                            variable.Identifier.Span, depth);
                    break;
                case EventFieldDeclarationSyntax value:
                    foreach (VariableDeclaratorSyntax variable in value.Declaration.Variables)
                        Add(CodeIntelligenceSymbolKind.Event, variable.Identifier.ValueText,
                            value.Span, variable.Identifier.Span, depth);
                    break;
                case EnumMemberDeclarationSyntax value:
                    Add(CodeIntelligenceSymbolKind.Constant, value.Identifier.ValueText,
                        value.Span, value.Identifier.Span, depth);
                    break;
            }

            foreach (SyntaxNode child in node.ChildNodes())
                Visit(child, childDepth);
        }

        void Add(CodeIntelligenceSymbolKind kind, string display, TextSpan span,
            TextSpan selection, int depth) =>
            result.Add(new(kind, display, span, selection, depth));
    }

    private static CodeIntelligenceSymbolKind MapTypeKind(BaseTypeDeclarationSyntax value) =>
        value.Kind() switch
        {
            SyntaxKind.ClassDeclaration or SyntaxKind.RecordDeclaration =>
                CodeIntelligenceSymbolKind.Class,
            SyntaxKind.InterfaceDeclaration => CodeIntelligenceSymbolKind.Interface,
            SyntaxKind.StructDeclaration or SyntaxKind.RecordStructDeclaration =>
                CodeIntelligenceSymbolKind.Structure,
            SyntaxKind.EnumDeclaration => CodeIntelligenceSymbolKind.Enumeration,
            _ => CodeIntelligenceSymbolKind.Other,
        };

    private static List<CodeIntelligenceFoldingRange> BuildFolding(
        SyntaxNode root,
        SourceText text)
    {
        List<CodeIntelligenceFoldingRange> result = [];
        foreach (SyntaxNode node in root.DescendantNodesAndSelf())
        {
            (CodeIntelligenceFoldingKind Kind, string Display)? descriptor = node switch
            {
                BaseNamespaceDeclarationSyntax value =>
                    (CodeIntelligenceFoldingKind.Namespace, $"namespace {value.Name} …"),
                BaseTypeDeclarationSyntax value =>
                    (CodeIntelligenceFoldingKind.Type, $"{value.Identifier.ValueText} …"),
                MethodDeclarationSyntax value =>
                    (CodeIntelligenceFoldingKind.Member, $"{value.Identifier.ValueText}{value.ParameterList} …"),
                ConstructorDeclarationSyntax value =>
                    (CodeIntelligenceFoldingKind.Member, $"{value.Identifier.ValueText}{value.ParameterList} …"),
                PropertyDeclarationSyntax value =>
                    (CodeIntelligenceFoldingKind.Member, $"{value.Identifier.ValueText} …"),
                IndexerDeclarationSyntax => (CodeIntelligenceFoldingKind.Member, "this[…] …"),
                EventDeclarationSyntax value =>
                    (CodeIntelligenceFoldingKind.Member, $"{value.Identifier.ValueText} …"),
                AccessorDeclarationSyntax value =>
                    (CodeIntelligenceFoldingKind.Block, $"{value.Keyword.ValueText} …"),
                BlockSyntax when node.Parent is not BaseMethodDeclarationSyntax &&
                                      node.Parent is not AccessorDeclarationSyntax =>
                    (CodeIntelligenceFoldingKind.Block, "{ … }"),
                InitializerExpressionSyntax => (CodeIntelligenceFoldingKind.Block, "{ … }"),
                AnonymousFunctionExpressionSyntax => (CodeIntelligenceFoldingKind.Block, "lambda …"),
                _ => null,
            };
            if (descriptor is { } fold)
                Add(node.Span, fold.Kind, fold.Display, defaultCollapsed: false);
        }

        Stack<RegionDirectiveTriviaSyntax> regions = new();
        foreach (DirectiveTriviaSyntax directive in root.DescendantTrivia(descendIntoTrivia: true)
                     .Select(item => item.GetStructure())
                     .OfType<DirectiveTriviaSyntax>())
        {
            if (directive is RegionDirectiveTriviaSyntax region)
                regions.Push(region);
            else if (directive is EndRegionDirectiveTriviaSyntax && regions.TryPop(out var start))
                Add(TextSpan.FromBounds(start.FullSpan.Start, directive.FullSpan.End),
                    CodeIntelligenceFoldingKind.Region,
                    start.ToString().Trim(), defaultCollapsed: false);
        }

        foreach (SyntaxTrivia trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                Add(trivia.FullSpan, CodeIntelligenceFoldingKind.Comment, "comment …",
                    defaultCollapsed: false);
        }

        return result
            .DistinctBy(item => (item.Range, item.Kind))
            .OrderBy(item => item.Range.Start.Line)
            .ThenBy(item => item.Range.Start.Character)
            .ToList();

        void Add(TextSpan span, CodeIntelligenceFoldingKind kind, string display,
            bool defaultCollapsed)
        {
            LinePositionSpan lines = text.Lines.GetLinePositionSpan(span);
            if (lines.End.Line <= lines.Start.Line)
                return;
            result.Add(new(Range(text, span), kind,
                new(Bound(display, 256)), defaultCollapsed));
        }
    }

    private static bool IsWriteReference(
        ReferenceLocation reference,
        SyntaxNode? root)
    {
        SyntaxNode? node = root?.FindNode(reference.Location.SourceSpan,
            getInnermostNodeForTie: true);
        SyntaxNode? expression = node?.AncestorsAndSelf().FirstOrDefault(item =>
            item.Span == reference.Location.SourceSpan || item is ExpressionSyntax);
        SyntaxNode? parent = expression?.Parent;
        return parent switch
        {
            AssignmentExpressionSyntax assignment when assignment.Left.Span.Contains(
                reference.Location.SourceSpan) => true,
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(
                SyntaxKind.PreIncrementExpression) || prefix.IsKind(
                SyntaxKind.PreDecrementExpression) => true,
            PostfixUnaryExpressionSyntax postfix when postfix.IsKind(
                SyntaxKind.PostIncrementExpression) || postfix.IsKind(
                SyntaxKind.PostDecrementExpression) => true,
            ArgumentSyntax argument when argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword) ||
                                         argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) => true,
            _ => false,
        };
    }

    private static CodeIntelligenceDocumentPresentationResult PresentationFailure(
        CodeIntelligenceDocumentPresentationRequest request,
        string code,
        string message,
        CodeIntelligenceResultState state = CodeIntelligenceResultState.Failed) => new(
        request.Snapshot.ContextId,
        request.Snapshot.SessionId,
        request.Snapshot.Path,
        request.Snapshot.BufferVersion,
        state,
        [], [], [], [], false,
        [Issue(code, Bound(message, MaximumIssueLength))]);

    private static CodeIntelligenceOccurrenceResult OccurrenceFailure(
        CodeIntelligenceInteractiveSnapshot snapshot,
        string code,
        string message,
        CodeIntelligenceResultState state = CodeIntelligenceResultState.Failed) => new(
        snapshot.ContextId,
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        state,
        null,
        [],
        false,
        [Issue(code, Bound(message, MaximumIssueLength))]);

    private sealed record OutlineEntry(
        CodeIntelligenceSymbolKind Kind,
        string Display,
        TextSpan Span,
        TextSpan SelectionSpan,
        int Depth);
}
