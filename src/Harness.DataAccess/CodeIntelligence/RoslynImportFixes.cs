using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    private const int MaximumMissingImportCandidates = 50;

    public async ValueTask<CodeIntelligenceMissingImportResult> GetMissingImportsAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return MissingImportFailure(snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return MissingImportFailure(snapshot, prepared.State,
                    prepared.Issue.Code.Value, prepared.Issue.Message.Value);
            }

            IReadOnlyList<MissingImportCandidateDocument> candidates =
                await FindMissingImportCandidatesAsync(prepared, cancellationToken);
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                candidates.Select(item => new CodeIntelligenceMissingImportCandidate(
                    new(item.Namespace), new(item.Symbol), Range(prepared.Text!, item.Span)))
                    .ToArray(),
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return MissingImportFailure(snapshot, CodeIntelligenceResultState.Failed,
                "missing_import_discovery_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    private static async ValueTask<IReadOnlyList<MissingImportCandidateDocument>>
        FindMissingImportCandidatesAsync(
            PreparedInteractive prepared,
            CancellationToken cancellationToken)
    {
        Document document = prepared.Document!;
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken);
        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken);
        if (root is not CompilationUnitSyntax compilationUnit || model is null)
        {
            return [];
        }

        SimpleNameSyntax? name = MissingTypeName(root, model, prepared.Offset, cancellationToken);
        if (name is null)
        {
            return [];
        }

        string identifier = name.Identifier.ValueText;
        IEnumerable<ISymbol> declarations = await SymbolFinder.FindDeclarationsAsync(
            document.Project, identifier, ignoreCase: false, SymbolFilter.Type, cancellationToken);
        SyntaxAnnotation target = new("HarnessMissingImportTarget");
        CompilationUnitSyntax annotated = compilationUnit.ReplaceNode(
            name, name.WithAdditionalAnnotations(target));
        Document annotatedDocument = document.WithSyntaxRoot(annotated);
        List<MissingImportCandidateDocument> candidates = [];
        foreach (string namespaceName in declarations
                     .OfType<INamedTypeSymbol>()
                     .Where(symbol => !symbol.ContainingNamespace.IsGlobalNamespace &&
                         symbol.CanBeReferencedByName)
                     .Select(symbol => symbol.ContainingNamespace.ToDisplayString())
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(value => value, StringComparer.Ordinal)
                     .Take(MaximumMissingImportCandidates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Document candidate = await AddUsingAsync(
                annotatedDocument, namespaceName, cancellationToken);
            SyntaxNode? candidateRoot = await candidate.GetSyntaxRootAsync(cancellationToken);
            SimpleNameSyntax? candidateName = candidateRoot?.GetAnnotatedNodes(target)
                .OfType<SimpleNameSyntax>().SingleOrDefault();
            SemanticModel? candidateModel = await candidate.GetSemanticModelAsync(cancellationToken);
            ISymbol? bound = candidateName is null || candidateModel is null
                ? null
                : candidateModel.GetSymbolInfo(candidateName, cancellationToken).Symbol;
            if (bound is INamedTypeSymbol type &&
                type.Name.Equals(identifier, StringComparison.Ordinal) &&
                type.ContainingNamespace.ToDisplayString().Equals(
                    namespaceName, StringComparison.Ordinal))
            {
                candidates.Add(new(namespaceName, type.ToDisplayString(), name.Span, candidate));
            }
        }

        return candidates;
    }

    private static SimpleNameSyntax? MissingTypeName(
        SyntaxNode root,
        SemanticModel model,
        int offset,
        CancellationToken cancellationToken)
    {
        int position = Math.Clamp(offset, 0, Math.Max(0, root.FullSpan.End - 1));
        SimpleNameSyntax? name = root.FindToken(position).Parent?.AncestorsAndSelf()
            .OfType<SimpleNameSyntax>()
            .FirstOrDefault(candidate => candidate.Span.Contains(position) ||
                candidate.Span.End == offset);
        if (name is null || model.GetSymbolInfo(name, cancellationToken).Symbol is not null)
        {
            return null;
        }

        return model.GetDiagnostics(name.Span, cancellationToken).Any(diagnostic =>
            (diagnostic.Id is "CS0246" or "CS0103") &&
            diagnostic.Location.SourceSpan.IntersectsWith(name.Span))
                ? name
                : null;
    }

    private static async ValueTask<Document> AddUsingAsync(
        Document document,
        string namespaceName,
        CancellationToken cancellationToken)
    {
        if (await document.GetSyntaxRootAsync(cancellationToken) is not CompilationUnitSyntax root)
        {
            return document;
        }

        if (root.Usings.Any(item => item.Alias is null && item.StaticKeyword.IsKind(SyntaxKind.None) &&
                item.Name?.ToString().Equals(namespaceName, StringComparison.Ordinal) is true))
        {
            return document;
        }

        UsingDirectiveSyntax directive = SyntaxFactory.UsingDirective(
            SyntaxFactory.ParseName(namespaceName));
        Document added = document.WithSyntaxRoot(root.AddUsings(directive));
        added = await Formatter.OrganizeImportsAsync(added, cancellationToken);
        return await Formatter.FormatAsync(added, cancellationToken: cancellationToken);
    }

    private static async ValueTask<Document> RemoveUnusedImportsAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        if (await document.GetSyntaxRootAsync(cancellationToken) is not CompilationUnitSyntax root ||
            await document.GetSemanticModelAsync(cancellationToken) is not { } model)
        {
            return document;
        }

        HashSet<TextSpan> unnecessary = model.GetDiagnostics(cancellationToken: cancellationToken)
            .Where(diagnostic => diagnostic.Id is "CS8019" or "IDE0005")
            .Select(diagnostic => diagnostic.Location.SourceSpan)
            .ToHashSet();
        UsingDirectiveSyntax[] removals = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(item => unnecessary.Any(span => span.IntersectsWith(item.Span)) &&
                item.GetLeadingTrivia().Concat(item.GetTrailingTrivia()).All(trivia =>
                    trivia.IsKind(SyntaxKind.WhitespaceTrivia) ||
                    trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
            .ToArray();
        if (removals.Length == 0)
        {
            return document;
        }

        SyntaxNode changed = root.RemoveNodes(removals, SyntaxRemoveOptions.KeepNoTrivia)!;
        Document removed = document.WithSyntaxRoot(changed);
        removed = await Formatter.OrganizeImportsAsync(removed, cancellationToken);
        return await Formatter.FormatAsync(removed, cancellationToken: cancellationToken);
    }

    private static CodeIntelligenceMissingImportResult MissingImportFailure(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CodeIntelligenceResultState state,
        string code,
        string message) => new(
        snapshot.ContextId,
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        state,
        [],
        [Issue(code, message)]);

    private sealed record MissingImportCandidateDocument(
        string Namespace,
        string Symbol,
        TextSpan Span,
        Document Document);
}
