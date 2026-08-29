using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;

namespace Harness.DataAccess.CodeIntelligence;

internal sealed partial class RoslynCodeIntelligenceEngine
{
    public async ValueTask<CodeIntelligenceCompletionResult> GetCompletionsAsync(
        CodeIntelligenceCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveSession? session = MatchingSession(request.Snapshot);
        if (session is null)
        {
            return CompletionFailure(request.Snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, request.Snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return CompletionFailure(
                    request.Snapshot, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            }

            CompletionService? service = CompletionService.GetService(prepared.Document!);
            if (service is null)
            {
                return CompletionFailure(request.Snapshot, CodeIntelligenceResultState.Degraded,
                    "completion_unavailable", "Completion is unavailable for this document.");
            }

            CompletionTrigger trigger = request.TriggerKind switch
            {
                CodeIntelligenceCompletionTriggerKind.Invoke => CompletionTrigger.Invoke,
                CodeIntelligenceCompletionTriggerKind.Insertion when request.TriggerCharacter is { } value =>
                    CompletionTrigger.CreateInsertionTrigger(value),
                CodeIntelligenceCompletionTriggerKind.Insertion => CompletionTrigger.Invoke,
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };
            CompletionList? list = await service.GetCompletionsAsync(
                prepared.Document!, prepared.Offset, trigger, cancellationToken: cancellationToken);
            if (list is null)
            {
                return new(
                    request.Snapshot.ContextId,
                    request.Snapshot.SessionId,
                    request.Snapshot.Path,
                    request.Snapshot.BufferVersion,
                    SessionState(session),
                    null,
                    Range(prepared.Text!, new TextSpan(prepared.Offset, 0)),
                    [],
                    session.Issues.ToArray());
            }

            CodeIntelligenceCompletionListId listId = new(Guid.NewGuid().ToString("N"));
            Dictionary<CodeIntelligenceCompletionItemId, CompletionItem> cachedItems = [];
            List<CodeIntelligenceCompletionItem> items = [];
            int index = 0;
            foreach (CompletionItem item in list.ItemsList.Take(MaximumCompletionItems))
            {
                CodeIntelligenceCompletionItemId itemId = new((index++).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                cachedItems.Add(itemId, item);
                items.Add(new(
                    itemId,
                    new(Bound(item.DisplayText + item.DisplayTextSuffix, MaximumIssueLength)),
                    new(Bound(item.FilterText, MaximumIssueLength)),
                    new(Bound(item.SortText, MaximumIssueLength)),
                    new(Bound(item.InlineDescription ?? string.Empty, MaximumIssueLength)),
                    MapSymbolKind(item.Tags),
                    CommitCharacters(item.Rules),
                    IsRecommended: false));
            }

            session.CompletionCache = new(
                listId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                Hash(request.Snapshot.Text.Value),
                service,
                cachedItems);
            return new(
                request.Snapshot.ContextId,
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                SessionState(session),
                listId,
                Range(prepared.Text!, list.Span),
                items,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException)
        {
            return CompletionFailure(request.Snapshot, CodeIntelligenceResultState.Failed,
                "completion_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public async ValueTask<CodeIntelligenceCompletionCommitResult> CommitCompletionAsync(
        CodeIntelligenceCompletionCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveSession? session = MatchingSession(request.Snapshot);
        if (session is null)
        {
            return CommitFailure(request.Snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, request.Snapshot, cancellationToken);
            CompletionCache? cache = session.CompletionCache;
            if (prepared.Issue is not null || cache is null || cache.ListId != request.ListId ||
                cache.Path != request.Snapshot.Path ||
                cache.BufferVersion != request.Snapshot.BufferVersion ||
                !cache.TextHash.Equals(Hash(request.Snapshot.Text.Value), StringComparison.Ordinal) ||
                !cache.Items.TryGetValue(request.ItemId, out CompletionItem? item))
            {
                return CommitFailure(
                    request.Snapshot,
                    prepared.Issue is null
                        ? CodeIntelligenceResultState.Stale
                        : prepared.State,
                    prepared.Issue?.Code.Value ?? "completion_stale",
                    prepared.Issue?.Message.Value ??
                        "The completion list no longer matches the active buffer.");
            }

            CompletionChange change = await cache.Service.GetChangeAsync(
                prepared.Document!, item, request.CommitCharacter, cancellationToken);
            return new(
                request.Snapshot.ContextId,
                request.Snapshot.SessionId,
                request.Snapshot.Path,
                request.Snapshot.BufferVersion,
                SessionState(session),
                [new(
                    Range(prepared.Text!, change.TextChange.Span),
                    new(change.TextChange.NewText ?? string.Empty))],
                change.NewPosition is { } position
                    ? Position(prepared.Text!, position)
                    : null,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            return CommitFailure(request.Snapshot, CodeIntelligenceResultState.Failed,
                "completion_commit_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public async ValueTask<CodeIntelligenceQuickInfoResult> GetQuickInfoAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return QuickInfoFailure(snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return QuickInfoFailure(
                    snapshot, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            }

            QuickInfoService? service = QuickInfoService.GetService(prepared.Document!);
            QuickInfoItem? item = service is null
                ? null
                : await service.GetQuickInfoAsync(
                    prepared.Document!, prepared.Offset, cancellationToken);
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                item is null ? null : Range(prepared.Text!, item.Span),
                item?.Sections
                    .Select(section => new CodeIntelligenceMessage(Bound(
                        string.Concat(section.TaggedParts.Select(part => part.Text)),
                        MaximumIssueLength)))
                    .Where(section => !string.IsNullOrWhiteSpace(section.Value))
                    .Take(12)
                    .ToArray() ?? [],
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException)
        {
            return QuickInfoFailure(snapshot, CodeIntelligenceResultState.Failed,
                "quick_info_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public async ValueTask<CodeIntelligenceSignatureHelpResult> GetSignatureHelpAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return SignatureFailure(snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return SignatureFailure(snapshot, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            }

            SyntaxNode? root = await prepared.Document!.GetSyntaxRootAsync(cancellationToken);
            SemanticModel? model = await prepared.Document.GetSemanticModelAsync(cancellationToken);
            SyntaxNode? node = root?.FindToken(Math.Max(0, prepared.Offset - 1)).Parent;
            BaseArgumentListSyntax? arguments = node?.AncestorsAndSelf()
                .OfType<BaseArgumentListSyntax>()
                .FirstOrDefault();
            SyntaxNode? callable = arguments?.Parent;
            SymbolInfo symbolInfo = callable is null || model is null
                ? default
                : model.GetSymbolInfo(callable, cancellationToken);
            IReadOnlyList<IMethodSymbol> methods = (symbolInfo.Symbol is IMethodSymbol method
                    ? [method]
                    : symbolInfo.CandidateSymbols.OfType<IMethodSymbol>())
                .Cast<ISymbol>()
                .Distinct(SymbolEqualityComparer.Default)
                .OfType<IMethodSymbol>()
                .Take(12)
                .ToArray();
            int selectedParameter = arguments?.Arguments.GetSeparators()
                .Count(separator => separator.SpanStart < prepared.Offset) ?? 0;
            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                methods.Select(method => MapSignature(method, cancellationToken)).ToArray(),
                SelectedSignature: 0,
                selectedParameter,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            return SignatureFailure(snapshot, CodeIntelligenceResultState.Failed,
                "signature_help_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    public ValueTask<CodeIntelligenceNavigationResult> FindDefinitionAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        NavigateAsync(snapshot, NavigationKind.Definition, cancellationToken);

    public ValueTask<CodeIntelligenceNavigationResult> FindReferencesAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        NavigateAsync(snapshot, NavigationKind.References, cancellationToken);

    public ValueTask<CodeIntelligenceNavigationResult> FindImplementationsAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        NavigateAsync(snapshot, NavigationKind.Implementations, cancellationToken);

    public ValueTask<CodeIntelligenceSemanticResult> SearchSymbolsAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Symbols, cancellationToken);

    public ValueTask<CodeIntelligenceSemanticResult> AnalyzeCallsAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Calls, cancellationToken);

    public ValueTask<CodeIntelligenceSemanticResult> GetTypeHierarchyAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Types, cancellationToken);

    public ValueTask<CodeIntelligenceSemanticResult> FindAssociatedTestsAsync(
        CodeIntelligenceSemanticQuery query,
        CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Tests, cancellationToken);

    private async ValueTask<CodeIntelligenceSemanticResult> SemanticAsync(
        CodeIntelligenceSemanticQuery query,
        SemanticKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.MaximumResults is < 1 or > 200 || query.Offset < 0 ||
            query.Query?.Length > 256)
            return SemanticFailure(query, "invalid_semantic_query",
                "Result limit, continuation offset, or query is outside the bounded range.");
        ActiveSession? session = MatchingSession(query.Snapshot);
        if (session is null)
            return SemanticFailure(query, "session_unavailable",
                "The Roslyn session no longer matches this source context.", CodeIntelligenceResultState.Stale);

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, query.Snapshot, cancellationToken);
            if (prepared.Issue is not null)
                return SemanticFailure(query, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value, prepared.State);
            Solution solution = prepared.Document!.Project.Solution;
            List<CodeIntelligenceSemanticItem> items = [];
            if (kind is SemanticKind.Symbols)
            {
                if (string.IsNullOrWhiteSpace(query.Query))
                    return SemanticFailure(query, "symbol_query_required", "A symbol name is required.");
                List<ISymbol> symbols = [];
                foreach (Project project in solution.Projects)
                    symbols.AddRange(await SymbolFinder.FindDeclarationsAsync(
                        project, query.Query, ignoreCase: true, filter: SymbolFilter.TypeAndMember,
                        cancellationToken: cancellationToken));
                foreach (ISymbol symbol in symbols.Take(query.Offset + query.MaximumResults + 1))
                    AddSymbol(items, CodeIntelligenceSemanticRelation.Symbol, symbol, session.RootPath);
            }
            else
            {
                ISymbol? symbol = await SymbolFinder.FindSymbolAtPositionAsync(
                    prepared.Document, prepared.Offset, cancellationToken);
                if (symbol is null)
                    return SemanticFailure(query, "symbol_unavailable",
                        "No symbol is available at the requested position.");
                if (kind is SemanticKind.Calls)
                {
                    IEnumerable<SymbolCallerInfo> callers = await SymbolFinder.FindCallersAsync(
                        symbol, solution, cancellationToken: cancellationToken);
                    foreach (ISymbol caller in callers.Select(item => item.CallingSymbol)
                                 .Distinct(SymbolEqualityComparer.Default))
                        AddSymbol(items, CodeIntelligenceSemanticRelation.IncomingCall, caller, session.RootPath);
                    foreach (SyntaxReference syntaxReference in symbol.DeclaringSyntaxReferences)
                    {
                        SyntaxNode declaration = await syntaxReference.GetSyntaxAsync(cancellationToken);
                        Document? document = solution.GetDocument(declaration.SyntaxTree);
                        if (document is null) continue;
                        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken);
                        if (model is null) continue;
                        foreach (InvocationExpressionSyntax invocation in declaration
                                     .DescendantNodes().OfType<InvocationExpressionSyntax>())
                        {
                            ISymbol? called = model.GetSymbolInfo(invocation, cancellationToken).Symbol;
                            if (called is not null)
                                AddSymbol(items, CodeIntelligenceSemanticRelation.OutgoingCall,
                                    called, session.RootPath);
                        }
                    }
                }
                else if (kind is SemanticKind.Types)
                {
                    INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
                    if (type is null)
                        return SemanticFailure(query, "type_unavailable",
                            "The selected symbol has no containing type.");
                    if (type.BaseType is not null)
                        AddSymbol(items, CodeIntelligenceSemanticRelation.BaseType, type.BaseType,
                            session.RootPath);
                    foreach (INamedTypeSymbol contract in type.Interfaces)
                        AddSymbol(items, CodeIntelligenceSemanticRelation.BaseType, contract,
                            session.RootPath);
                    IEnumerable<INamedTypeSymbol> derived = type.TypeKind is TypeKind.Interface
                        ? await SymbolFinder.FindDerivedInterfacesAsync(type, solution,
                            cancellationToken: cancellationToken)
                        : await SymbolFinder.FindDerivedClassesAsync(type, solution,
                            cancellationToken: cancellationToken);
                    foreach (INamedTypeSymbol child in derived)
                        AddSymbol(items, CodeIntelligenceSemanticRelation.DerivedType, child,
                            session.RootPath);
                    IEnumerable<ISymbol> overrides = symbol is IMethodSymbol or IPropertySymbol or IEventSymbol
                        ? await SymbolFinder.FindOverridesAsync(symbol, solution,
                            cancellationToken: cancellationToken) : [];
                    foreach (ISymbol item in overrides)
                        AddSymbol(items, CodeIntelligenceSemanticRelation.Override, item,
                            session.RootPath);
                }
                else
                {
                    IEnumerable<ReferencedSymbol> references = await SymbolFinder.FindReferencesAsync(
                        symbol, solution, cancellationToken);
                    foreach (ReferenceLocation reference in references.SelectMany(item => item.Locations))
                    {
                        Document? document = solution.GetDocument(reference.Location.SourceTree);
                        if (document is null || !IsTestDocument(document, reference.Location, cancellationToken))
                            continue;
                        items.Add(new(CodeIntelligenceSemanticRelation.AssociatedTest,
                            new(document.Name), MapDestination(reference.Location, document.Name,
                                session.RootPath)));
                    }
                }
            }

            CodeIntelligenceSemanticItem[] distinct = items
                .DistinctBy(item => $"{item.Relation}:{item.Display.Value}:{item.Destination.Path?.Value}:{item.Destination.Range}")
                .ToArray();
            CodeIntelligenceSemanticItem[] page = distinct.Skip(query.Offset)
                .Take(query.MaximumResults).ToArray();
            bool truncated = distinct.Length > query.Offset + page.Length;
            return new(query.Snapshot.ContextId, query.Snapshot.SessionId, query.Snapshot.Path,
                query.Snapshot.BufferVersion, SessionState(session), page,
                truncated ? query.Offset + page.Length : null, truncated, session.Issues.ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return SemanticFailure(query, "semantic_query_failed", exception.Message);
        }
        finally { session.OperationGate.Release(); }
    }

    private static void AddSymbol(
        ICollection<CodeIntelligenceSemanticItem> items,
        CodeIntelligenceSemanticRelation relation,
        ISymbol symbol,
        string root)
    {
        string display = Bound(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            MaximumIssueLength);
        Location? location = symbol.Locations.FirstOrDefault(item => item.IsInSource);
        CodeIntelligenceSymbolDestination destination = location is null
            ? new(CodeIntelligenceDestinationKind.Metadata, new(display), null, null)
            : MapDestination(location, display, root);
        items.Add(new(relation, new(display), destination));
    }

    private static bool IsTestDocument(
        Document document, Location location, CancellationToken cancellationToken)
    {
        if (document.Project.Name.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
            document.FilePath?.Contains("/test", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        SyntaxNode? root = location.SourceTree?.GetRoot(cancellationToken);
        MethodDeclarationSyntax? method = root?.FindNode(location.SourceSpan)
            .AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return method?.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
            attribute.Name.ToString() is "Fact" or "Theory" or "Test" or "TestCase" or
                "FactAttribute" or "TheoryAttribute" or "TestAttribute" or "TestCaseAttribute") == true;
    }

    private static CodeIntelligenceSemanticResult SemanticFailure(
        CodeIntelligenceSemanticQuery query, string code, string error,
        CodeIntelligenceResultState state = CodeIntelligenceResultState.Failed) => new(
        query.Snapshot.ContextId, query.Snapshot.SessionId, query.Snapshot.Path,
        query.Snapshot.BufferVersion, state, [], null, false,
        [new(new(code), new(Bound(error, MaximumIssueLength)))]);

    private enum SemanticKind { Symbols, Calls, Types, Tests }

    private async ValueTask<CodeIntelligenceNavigationResult> NavigateAsync(
        CodeIntelligenceInteractiveSnapshot snapshot,
        NavigationKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ActiveSession? session = MatchingSession(snapshot);
        if (session is null)
        {
            return NavigationFailure(snapshot, CodeIntelligenceResultState.Stale,
                "session_unavailable", "The Roslyn session no longer matches this source context.");
        }

        await session.OperationGate.WaitAsync(cancellationToken);
        try
        {
            PreparedInteractive prepared = await PrepareInteractiveAsync(
                session, snapshot, cancellationToken);
            if (prepared.Issue is not null)
            {
                return NavigationFailure(snapshot, prepared.State, prepared.Issue.Code.Value,
                    prepared.Issue.Message.Value);
            }

            ISymbol? symbol = await SymbolFinder.FindSymbolAtPositionAsync(
                prepared.Document!, prepared.Offset, cancellationToken);
            if (symbol is null)
            {
                return new(
                    snapshot.ContextId,
                    snapshot.SessionId,
                    snapshot.Path,
                    snapshot.BufferVersion,
                    SessionState(session),
                    [UnavailableDestination("No symbol is available at the active caret.")],
                    session.Issues.ToArray());
            }

            IReadOnlyList<CodeIntelligenceSymbolDestination> destinations;
            if (kind is NavigationKind.References)
            {
                IEnumerable<ReferencedSymbol> found = await SymbolFinder.FindReferencesAsync(
                    symbol, prepared.Document!.Project.Solution, cancellationToken);
                IReadOnlyList<Location> locations = found
                    .SelectMany(item => item.Locations)
                    .Select(item => item.Location)
                    .Take(MaximumNavigationItems)
                    .ToArray();
                List<CodeIntelligenceSymbolDestination> mapped = [];
                foreach (Location location in locations)
                {
                    mapped.Add(await MapNavigableDestinationAsync(
                        session, snapshot, prepared.Document.Project, symbol, location,
                        cancellationToken));
                }
                destinations = mapped;
            }
            else if (kind is NavigationKind.Implementations)
            {
                IEnumerable<ISymbol> found = await SymbolFinder.FindImplementationsAsync(
                    symbol, prepared.Document!.Project.Solution, cancellationToken: cancellationToken);
                IEnumerable<ISymbol> overrides = symbol is IMethodSymbol or IPropertySymbol or IEventSymbol
                    ? await SymbolFinder.FindOverridesAsync(
                        symbol,
                        prepared.Document.Project.Solution,
                        cancellationToken: cancellationToken)
                    : [];
                List<CodeIntelligenceSymbolDestination> mapped = [];
                foreach (ISymbol implementation in found.Concat(overrides)
                             .Distinct(SymbolEqualityComparer.Default))
                {
                    foreach (Location location in implementation.Locations)
                    {
                        mapped.Add(await MapNavigableDestinationAsync(
                            session, snapshot, prepared.Document.Project, implementation,
                            location, cancellationToken));
                        if (mapped.Count >= MaximumNavigationItems) break;
                    }
                    if (mapped.Count >= MaximumNavigationItems) break;
                }
                destinations = mapped;
            }
            else
            {
                List<CodeIntelligenceSymbolDestination> mapped = [];
                foreach (Location location in symbol.OriginalDefinition.Locations
                             .Take(MaximumNavigationItems))
                {
                    mapped.Add(await MapNavigableDestinationAsync(
                        session, snapshot, prepared.Document!.Project,
                        symbol.OriginalDefinition, location, cancellationToken));
                }
                destinations = mapped;
            }
            if (destinations.Count == 0)
            {
                destinations = kind is NavigationKind.Implementations
                    ? [UnavailableDestination("No source implementation is available for this symbol.")]
                    : [await MapNavigableDestinationAsync(
                        session, snapshot, prepared.Document!.Project,
                        symbol.OriginalDefinition, Location.None, cancellationToken)];
            }

            return new(
                snapshot.ContextId,
                snapshot.SessionId,
                snapshot.Path,
                snapshot.BufferVersion,
                SessionState(session),
                destinations,
                session.Issues.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            return NavigationFailure(snapshot, CodeIntelligenceResultState.Failed,
                "navigation_failed", exception.Message);
        }
        finally
        {
            session.OperationGate.Release();
        }
    }

    private enum NavigationKind
    {
        Definition,
        References,
        Implementations,
    }

}
