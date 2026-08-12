using Harness.DataAccess.CodeIntelligence;

namespace Harness.BusinessLogic.CodeIntelligence;

internal sealed partial class WorkbenchCodeIntelligenceService
{
    private const int MaximumInteractiveItems = 500;

    public async ValueTask<WorkbenchCodeCompletionView> GetCompletionsAsync(
        WorkbenchCodeCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryInteractive(request.Snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue) ||
            !Enum.IsDefined(request.TriggerKind))
        {
            return CompletionFailure(request.Snapshot, issue ??
                Issue("invalid_completion_request", "A valid completion trigger is required."));
        }

        CodeIntelligenceCompletionResult result;
        try
        {
            result = await engine.GetCompletionsAsync(new(
                ToDataSnapshot(request.Snapshot, session!),
                request.TriggerKind is WorkbenchCodeCompletionTriggerKind.Invoke
                    ? CodeIntelligenceCompletionTriggerKind.Invoke
                    : CodeIntelligenceCompletionTriggerKind.Insertion,
                request.TriggerCharacter), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CompletionFailure(request.Snapshot,
                Issue("cancelled", "Completion was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }

        if (!IsFresh(session!, request.Snapshot) || !Matches(result, session!, request.Snapshot))
        {
            return CompletionFailure(request.Snapshot,
                Issue("stale_buffer", "A newer document buffer superseded this completion result."),
                WorkbenchCodeResultState.Stale);
        }

        return new(
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            Map(result.State),
            result.ListId is null ? null : new(result.ListId.Value),
            Map(result.ApplicableRange),
            result.Items.Take(MaximumInteractiveItems).Select(item => new WorkbenchCodeCompletionItem(
                new(item.Id.Value),
                new(item.DisplayText.Value),
                new(item.FilterText.Value),
                new(item.SortText.Value),
                new(item.Description.Value),
                Map(item.Kind),
                item.CommitCharacters.ToArray(),
                item.IsRecommended)).ToArray(),
            MapIssues(result.Issues));
    }

    public async ValueTask<WorkbenchCodeCompletionCommitView> CommitCompletionAsync(
        WorkbenchCodeCompletionCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryInteractive(request.Snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue) ||
            request.ListId is null || string.IsNullOrWhiteSpace(request.ListId.Value) ||
            request.ItemId is null || string.IsNullOrWhiteSpace(request.ItemId.Value))
        {
            return CommitFailure(request.Snapshot, issue ??
                Issue("invalid_completion_commit", "A completion list and item are required."));
        }

        CodeIntelligenceCompletionCommitResult result;
        try
        {
            result = await engine.CommitCompletionAsync(new(
                ToDataSnapshot(request.Snapshot, session!),
                new(request.ListId.Value),
                new(request.ItemId.Value),
                request.CommitCharacter), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CommitFailure(request.Snapshot,
                Issue("cancelled", "Completion commit was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }

        if (!IsFresh(session!, request.Snapshot) || !Matches(result, session!, request.Snapshot))
        {
            return CommitFailure(request.Snapshot,
                Issue("stale_buffer", "A newer document buffer superseded this completion commit."),
                WorkbenchCodeResultState.Stale);
        }

        return new(
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            Map(result.State),
            result.Changes.Take(20).Select(change => new WorkbenchCodeTextChange(
                Map(change.Range),
                new(change.Text.Value))).ToArray(),
            result.NewPosition is null ? null : Map(result.NewPosition),
            MapIssues(result.Issues));
    }

    public ValueTask<WorkbenchCodeQuickInfoView> GetQuickInfoAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        QuickInfoAsync(snapshot, cancellationToken);

    public ValueTask<WorkbenchCodeSignatureHelpView> GetSignatureHelpAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        SignatureAsync(snapshot, cancellationToken);

    public ValueTask<WorkbenchCodeNavigationView> FindDefinitionAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        NavigationAsync(snapshot, NavigationKind.Definition, cancellationToken);

    public ValueTask<WorkbenchCodeNavigationView> FindReferencesAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        NavigationAsync(snapshot, NavigationKind.References, cancellationToken);

    public ValueTask<WorkbenchCodeNavigationView> FindImplementationsAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        NavigationAsync(snapshot, NavigationKind.Implementations, cancellationToken);

    public async ValueTask<WorkbenchCodeVirtualDocumentView> GetVirtualDocumentAsync(
        WorkbenchCodeVirtualDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryInteractive(request.Snapshot, out ActiveSession? session,
                out WorkbenchCodeIssue? issue) ||
            request.Id is null || !IsSha256(request.Id.Value))
        {
            return VirtualDocumentFailure(request, issue ??
                Issue("invalid_virtual_document", "A valid virtual document handle is required."));
        }

        CodeIntelligenceVirtualDocumentResult result;
        try
        {
            result = await engine.GetVirtualDocumentAsync(new(
                ToDataSnapshot(request.Snapshot, session!),
                new(request.Id.Value)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return VirtualDocumentFailure(request,
                Issue("cancelled", "Virtual source loading was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }
        if (!IsFresh(session!, request.Snapshot) || !Matches(result, session!, request.Snapshot))
        {
            return VirtualDocumentFailure(request,
                Issue("stale_buffer", "A newer document buffer superseded this virtual source."),
                WorkbenchCodeResultState.Stale);
        }

        return new(request.Snapshot.SessionId, request.Snapshot.Path,
            request.Snapshot.BufferVersion, Map(result.State), request.Id,
            result.Kind is null ? null : Map(result.Kind.Value),
            result.Title is null ? null : new(result.Title.Value),
            result.Text is null ? null : new(result.Text.Value),
            result.SelectionRange is null ? null : Map(result.SelectionRange),
            result.Origin is null ? null : new(
                new(result.Origin.Project.Value),
                new(result.Origin.ProjectVersion.Value),
                new(result.Origin.TargetFramework.Value),
                new(result.Origin.Configuration.Value),
                new(result.Origin.Assembly.Value),
                new(result.Origin.Compilation.Value)),
            result.IsReadOnly, MapIssues(result.Issues));
    }

    public ValueTask<WorkbenchCodeSemanticView> SearchSymbolsAsync(
        WorkbenchCodeSemanticQuery query, CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Symbols, cancellationToken);
    public ValueTask<WorkbenchCodeSemanticView> AnalyzeCallsAsync(
        WorkbenchCodeSemanticQuery query, CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Calls, cancellationToken);
    public ValueTask<WorkbenchCodeSemanticView> GetTypeHierarchyAsync(
        WorkbenchCodeSemanticQuery query, CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Types, cancellationToken);
    public ValueTask<WorkbenchCodeSemanticView> FindAssociatedTestsAsync(
        WorkbenchCodeSemanticQuery query, CancellationToken cancellationToken = default) =>
        SemanticAsync(query, SemanticKind.Tests, cancellationToken);

    public async ValueTask<WorkbenchCodeDocumentPresentationView>
        GetDocumentPresentationAsync(
            WorkbenchCodeDocumentPresentationRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryInteractive(request.Snapshot, out ActiveSession? session,
                out WorkbenchCodeIssue? issue))
        {
            return PresentationFailure(request, issue!);
        }

        CodeIntelligenceDocumentPresentationResult result;
        try
        {
            result = await engine.GetDocumentPresentationAsync(new(
                ToDataSnapshot(request.Snapshot, session!),
                request.VisibleRange is null ? null : new(
                    new(request.VisibleRange.Start.Line, request.VisibleRange.Start.Character),
                    new(request.VisibleRange.End.Line, request.VisibleRange.End.Character)),
                request.Scope is WorkbenchCodeDocumentPresentationScope.VisibleClassification
                    ? CodeIntelligenceDocumentPresentationScope.VisibleClassification
                    : CodeIntelligenceDocumentPresentationScope.ClassificationAndStructure,
                request.InlayHints is null ? null : new(
                    request.InlayHints.ShowParameterNames,
                    request.InlayHints.ShowInferredTypes),
                request.CodeLens is null ? null : new(
                    request.CodeLens.ShowReferences,
                    request.CodeLens.ShowImplementations,
                    request.CodeLens.ShowTests)),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PresentationFailure(request,
                Issue("cancelled", "Semantic document presentation was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }

        if (!IsFresh(session!, request.Snapshot) ||
            !Matches(result, session!, request.Snapshot))
        {
            return PresentationFailure(request,
                Issue("stale_buffer",
                    "A newer document buffer superseded this semantic presentation."),
                WorkbenchCodeResultState.Stale);
        }

        return new(
            request.Snapshot.SessionId,
            request.Snapshot.Path,
            request.Snapshot.BufferVersion,
            Map(result.State),
            result.Classifications.Select(item => new WorkbenchCodeClassifiedSpan(
                Map(item.Range), Map(item.Kind))).ToArray(),
            result.FoldingRanges.Select(item => new WorkbenchCodeFoldingRange(
                Map(item.Range), Map(item.Kind), new(item.Display.Value),
                item.IsDefaultCollapsed)).ToArray(),
            result.Outline.Select(item => new WorkbenchCodeOutlineItem(
                Map(item.Kind), new(item.Display.Value), Map(item.Range),
                Map(item.SelectionRange), item.Depth)).ToArray(),
            result.Breadcrumbs.Select(item => new WorkbenchCodeBreadcrumb(
                Map(item.Kind), new(item.Display.Value), Map(item.Range))).ToArray(),
            result.InlayHints.Select(item => new WorkbenchCodeInlayHint(
                new(item.Position.Line, item.Position.Character),
                Map(item.Kind), new(item.Label.Value), new(item.Tooltip.Value))).ToArray(),
            result.CodeLenses.Select(item => new WorkbenchCodeLens(
                new(item.Position.Line, item.Position.Character),
                new(item.Target.Line, item.Target.Character),
                Map(item.Kind), new(item.Display.Value), item.IsResolved)).ToArray(),
            result.IsTruncated,
            MapIssues(result.Issues));
    }

    public async ValueTask<WorkbenchCodeOccurrenceView> FindOccurrencesAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (!TryInteractive(snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue))
        {
            return OccurrenceFailure(snapshot, issue!);
        }

        CodeIntelligenceOccurrenceResult result;
        try
        {
            result = await engine.FindOccurrencesAsync(
                ToDataSnapshot(snapshot, session!), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OccurrenceFailure(snapshot,
                Issue("cancelled", "Semantic occurrence lookup was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }

        if (!IsFresh(session!, snapshot) || !Matches(result, session!, snapshot))
        {
            return OccurrenceFailure(snapshot,
                Issue("stale_buffer",
                    "A newer document buffer superseded this occurrence result."),
                WorkbenchCodeResultState.Stale);
        }

        return new(
            snapshot.SessionId,
            snapshot.Path,
            snapshot.BufferVersion,
            Map(result.State),
            result.Symbol is null ? null : new(result.Symbol.Value),
            result.Occurrences.Select(item => new WorkbenchCodeOccurrence(
                Map(item.Range), Map(item.Kind))).ToArray(),
            result.IsTruncated,
            MapIssues(result.Issues));
    }

    private async ValueTask<WorkbenchCodeSemanticView> SemanticAsync(
        WorkbenchCodeSemanticQuery query, SemanticKind kind, CancellationToken cancellationToken)
    {
        if (query is null)
            return SemanticFailure(null, Issue("invalid_semantic_query", "A valid semantic query is required."));
        if (!TryInteractive(query.Snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue))
            return SemanticFailure(query, issue!);
        ActiveSession active = session!;
        CodeIntelligenceSemanticQuery data = new(ToDataSnapshot(query.Snapshot, active),
            query.Query, query.MaximumResults, query.Offset);
        CodeIntelligenceSemanticResult result = kind switch
        {
            SemanticKind.Symbols => await engine.SearchSymbolsAsync(data, cancellationToken),
            SemanticKind.Calls => await engine.AnalyzeCallsAsync(data, cancellationToken),
            SemanticKind.Types => await engine.GetTypeHierarchyAsync(data, cancellationToken),
            SemanticKind.Tests => await engine.FindAssociatedTestsAsync(data, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (!IsFresh(active, query.Snapshot) || result.SessionId.Value != active.SessionId.Value ||
            result.Path.Value != query.Snapshot.Path.Value ||
            result.BufferVersion.Value != query.Snapshot.BufferVersion.Value)
            return SemanticFailure(query, Issue("stale_buffer", "A newer buffer superseded this semantic result."),
                WorkbenchCodeResultState.Stale);
        return new(query.Snapshot.SessionId, query.Snapshot.Path, query.Snapshot.BufferVersion,
            Map(result.State), result.Items.Select(item => new WorkbenchCodeSemanticItem(
                Enum.Parse<WorkbenchCodeSemanticRelation>(item.Relation.ToString()),
                new(item.Display.Value), new(Map(item.Destination.Kind),
                    new(item.Destination.Display.Value),
                    item.Destination.Path is null ? null : new(item.Destination.Path.Value),
                    item.Destination.Range is null ? null : Map(item.Destination.Range)))).ToArray(),
            result.Continuation, result.IsTruncated, MapIssues(result.Issues));
    }

    private static WorkbenchCodeSemanticView SemanticFailure(
        WorkbenchCodeSemanticQuery? query, WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        query?.Snapshot.SessionId ?? new("unavailable"),
        query?.Snapshot.Path ?? new("unavailable"),
        query?.Snapshot.BufferVersion ?? new(0), state, [], null, false, [issue]);

    private enum SemanticKind { Symbols, Calls, Types, Tests }

    private static WorkbenchCodeDocumentPresentationView PresentationFailure(
        WorkbenchCodeDocumentPresentationRequest request,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        request.Snapshot.SessionId,
        request.Snapshot.Path,
        request.Snapshot.BufferVersion,
        state,
        [], [], [], [], [], [], false,
        [issue]);

    private static WorkbenchCodeOccurrenceView OccurrenceFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot.SessionId,
        snapshot.Path,
        snapshot.BufferVersion,
        state,
        null,
        [],
        false,
        [issue]);

    private async ValueTask<WorkbenchCodeQuickInfoView> QuickInfoAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!TryInteractive(snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue))
        {
            return QuickInfoFailure(snapshot, issue!);
        }

        CodeIntelligenceQuickInfoResult result;
        try
        {
            result = await engine.GetQuickInfoAsync(
                ToDataSnapshot(snapshot, session!), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return QuickInfoFailure(snapshot, Issue("cancelled", "Quick info was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }

        if (!IsFresh(session!, snapshot) || !Matches(result, session!, snapshot))
        {
            return QuickInfoFailure(snapshot,
                Issue("stale_buffer", "A newer document buffer superseded this quick info."),
                WorkbenchCodeResultState.Stale);
        }

        return new(
            snapshot.SessionId,
            snapshot.Path,
            snapshot.BufferVersion,
            Map(result.State),
            result.ApplicableRange is null ? null : Map(result.ApplicableRange),
            result.Sections.Take(12).Select(section => new WorkbenchCodeMessage(section.Value)).ToArray(),
            MapIssues(result.Issues));
    }

    private async ValueTask<WorkbenchCodeSignatureHelpView> SignatureAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!TryInteractive(snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue))
        {
            return SignatureFailure(snapshot, issue!);
        }

        CodeIntelligenceSignatureHelpResult result;
        try
        {
            result = await engine.GetSignatureHelpAsync(
                ToDataSnapshot(snapshot, session!), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SignatureFailure(snapshot, Issue("cancelled", "Signature help was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }

        if (!IsFresh(session!, snapshot) || !Matches(result, session!, snapshot))
        {
            return SignatureFailure(snapshot,
                Issue("stale_buffer", "A newer document buffer superseded this signature help."),
                WorkbenchCodeResultState.Stale);
        }

        return new(
            snapshot.SessionId,
            snapshot.Path,
            snapshot.BufferVersion,
            Map(result.State),
            result.Signatures.Take(12).Select(signature => new WorkbenchCodeSignatureItem(
                new(signature.Display.Value),
                new(signature.Documentation.Value),
                signature.Parameters.Select(parameter => new WorkbenchCodeSignatureParameter(
                    new(parameter.Name.Value),
                    new(parameter.Display.Value),
                    new(parameter.Documentation.Value))).ToArray())).ToArray(),
            result.SelectedSignature,
            result.SelectedParameter,
            MapIssues(result.Issues));
    }

    private async ValueTask<WorkbenchCodeNavigationView> NavigationAsync(
        WorkbenchCodeInteractiveSnapshot snapshot,
        NavigationKind kind,
        CancellationToken cancellationToken)
    {
        if (!TryInteractive(snapshot, out ActiveSession? session, out WorkbenchCodeIssue? issue))
        {
            return NavigationFailure(snapshot, issue!);
        }

        CodeIntelligenceNavigationResult result;
        try
        {
            CodeIntelligenceInteractiveSnapshot data = ToDataSnapshot(snapshot, session!);
            result = kind switch
            {
                NavigationKind.Definition =>
                    await engine.FindDefinitionAsync(data, cancellationToken),
                NavigationKind.References =>
                    await engine.FindReferencesAsync(data, cancellationToken),
                NavigationKind.Implementations =>
                    await engine.FindImplementationsAsync(data, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NavigationFailure(snapshot, Issue("cancelled", "Symbol navigation was cancelled."),
                WorkbenchCodeResultState.Cancelled);
        }

        if (!IsFresh(session!, snapshot) || !Matches(result, session!, snapshot))
        {
            return NavigationFailure(snapshot,
                Issue("stale_buffer", "A newer document buffer superseded this navigation result."),
                WorkbenchCodeResultState.Stale);
        }

        return new(
            snapshot.SessionId,
            snapshot.Path,
            snapshot.BufferVersion,
            Map(result.State),
            result.Destinations.Take(MaximumInteractiveItems).Select(destination =>
                new WorkbenchCodeSymbolDestination(
                    Map(destination.Kind),
                    new(destination.Display.Value),
                    destination.Path is null ? null : new(destination.Path.Value),
                    destination.Range is null ? null : Map(destination.Range),
                    destination.VirtualDocumentId is null
                        ? null : new(destination.VirtualDocumentId.Value))).ToArray(),
            MapIssues(result.Issues));
    }

    private enum NavigationKind
    {
        Definition,
        References,
        Implementations,
    }

    private bool TryInteractive(
        WorkbenchCodeInteractiveSnapshot snapshot,
        out ActiveSession? session,
        out WorkbenchCodeIssue? issue)
    {
        session = null;
        if (snapshot is null || snapshot.SessionId is null ||
            !sessions.TryGetValue(snapshot.SessionId.Value, out session))
        {
            issue = Issue("session_unavailable", "The code-intelligence session is unavailable.");
            return false;
        }

        if (snapshot.Path is null || !IsConfinedRelativePath(snapshot.Path.Value) ||
            snapshot.BaselineHash is null || !IsSha256(snapshot.BaselineHash.Value) ||
            snapshot.BufferVersion is null || snapshot.BufferVersion.Value <= 0 ||
            snapshot.Text is null || snapshot.Position is null ||
            snapshot.Position.Line < 0 || snapshot.Position.Character < 0)
        {
            issue = Issue("invalid_snapshot",
                "Interactive code requests require a confined exact-version document and caret.");
            return false;
        }

        if (!IsFresh(session, snapshot))
        {
            issue = Issue("stale_buffer", "A newer document buffer is already active.");
            return false;
        }

        issue = null;
        return true;
    }

    private static bool IsFresh(ActiveSession session, WorkbenchCodeInteractiveSnapshot snapshot)
    {
        lock (session.Gate)
        {
            return !session.DocumentVersions.TryGetValue(snapshot.Path.Value, out long current) ||
                current <= snapshot.BufferVersion.Value;
        }
    }

    private static CodeIntelligenceInteractiveSnapshot ToDataSnapshot(
        WorkbenchCodeInteractiveSnapshot snapshot,
        ActiveSession session) => new(
        session.ContextId,
        session.SessionId,
        new(snapshot.Path.Value),
        new(snapshot.BaselineHash.Value),
        new(snapshot.BufferVersion.Value),
        new(snapshot.Text.Value),
        new(snapshot.Position.Line, snapshot.Position.Character));

    private static bool Matches(
        CodeIntelligenceCompletionResult result,
        ActiveSession session,
        WorkbenchCodeInteractiveSnapshot snapshot) =>
        Matches(result.ContextId, result.SessionId, result.Path, result.BufferVersion,
            session, snapshot);

    private static bool Matches(
        CodeIntelligenceCompletionCommitResult result,
        ActiveSession session,
        WorkbenchCodeInteractiveSnapshot snapshot) =>
        Matches(result.ContextId, result.SessionId, result.Path, result.BufferVersion,
            session, snapshot);

    private static bool Matches(
        CodeIntelligenceQuickInfoResult result,
        ActiveSession session,
        WorkbenchCodeInteractiveSnapshot snapshot) =>
        Matches(result.ContextId, result.SessionId, result.Path, result.BufferVersion,
            session, snapshot);

    private static bool Matches(
        CodeIntelligenceSignatureHelpResult result,
        ActiveSession session,
        WorkbenchCodeInteractiveSnapshot snapshot) =>
        Matches(result.ContextId, result.SessionId, result.Path, result.BufferVersion,
            session, snapshot);

    private static bool Matches(
        CodeIntelligenceNavigationResult result,
        ActiveSession session,
        WorkbenchCodeInteractiveSnapshot snapshot) =>
        Matches(result.ContextId, result.SessionId, result.Path, result.BufferVersion,
            session, snapshot);

    private static bool Matches(
        CodeIntelligenceVirtualDocumentResult result,
        ActiveSession session,
        WorkbenchCodeInteractiveSnapshot snapshot) =>
        Matches(result.ContextId, result.SessionId, result.SourcePath, result.BufferVersion,
            session, snapshot);

    private static bool Matches(
        CodeIntelligenceDocumentPresentationResult result,
        ActiveSession session,
        WorkbenchCodeInteractiveSnapshot snapshot) =>
        Matches(result.ContextId, result.SessionId, result.Path, result.BufferVersion,
            session, snapshot);

    private static bool Matches(
        CodeIntelligenceOccurrenceResult result,
        ActiveSession session,
        WorkbenchCodeInteractiveSnapshot snapshot) =>
        Matches(result.ContextId, result.SessionId, result.Path, result.BufferVersion,
            session, snapshot);

    private static bool Matches(
        CodeIntelligenceContextId contextId,
        CodeIntelligenceSessionId sessionId,
        CodeIntelligenceDocumentPath path,
        CodeIntelligenceBufferVersion version,
        ActiveSession session,
        WorkbenchCodeInteractiveSnapshot snapshot) =>
        contextId == session.ContextId && sessionId == session.SessionId &&
        path.Value == snapshot.Path.Value && version.Value == snapshot.BufferVersion.Value;

    private static WorkbenchCodeRange Map(CodeIntelligenceRange range) => new(
        Map(range.Start), Map(range.End));

    private static WorkbenchCodePosition Map(CodeIntelligencePosition position) =>
        new(position.Line, position.Character);

    private static WorkbenchCodeSymbolKind Map(CodeIntelligenceSymbolKind kind) => kind switch
    {
        CodeIntelligenceSymbolKind.Keyword => WorkbenchCodeSymbolKind.Keyword,
        CodeIntelligenceSymbolKind.Namespace => WorkbenchCodeSymbolKind.Namespace,
        CodeIntelligenceSymbolKind.Class => WorkbenchCodeSymbolKind.Class,
        CodeIntelligenceSymbolKind.Interface => WorkbenchCodeSymbolKind.Interface,
        CodeIntelligenceSymbolKind.Structure => WorkbenchCodeSymbolKind.Structure,
        CodeIntelligenceSymbolKind.Enumeration => WorkbenchCodeSymbolKind.Enumeration,
        CodeIntelligenceSymbolKind.Delegate => WorkbenchCodeSymbolKind.Delegate,
        CodeIntelligenceSymbolKind.Method => WorkbenchCodeSymbolKind.Method,
        CodeIntelligenceSymbolKind.ExtensionMethod => WorkbenchCodeSymbolKind.ExtensionMethod,
        CodeIntelligenceSymbolKind.Constructor => WorkbenchCodeSymbolKind.Constructor,
        CodeIntelligenceSymbolKind.Property => WorkbenchCodeSymbolKind.Property,
        CodeIntelligenceSymbolKind.Field => WorkbenchCodeSymbolKind.Field,
        CodeIntelligenceSymbolKind.Event => WorkbenchCodeSymbolKind.Event,
        CodeIntelligenceSymbolKind.Constant => WorkbenchCodeSymbolKind.Constant,
        CodeIntelligenceSymbolKind.Local => WorkbenchCodeSymbolKind.Local,
        CodeIntelligenceSymbolKind.Parameter => WorkbenchCodeSymbolKind.Parameter,
        CodeIntelligenceSymbolKind.TypeParameter => WorkbenchCodeSymbolKind.TypeParameter,
        CodeIntelligenceSymbolKind.Snippet => WorkbenchCodeSymbolKind.Snippet,
        CodeIntelligenceSymbolKind.Region => WorkbenchCodeSymbolKind.Region,
        CodeIntelligenceSymbolKind.Other => WorkbenchCodeSymbolKind.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static WorkbenchCodeClassificationKind Map(
        CodeIntelligenceClassificationKind kind) => kind switch
        {
            CodeIntelligenceClassificationKind.Text => WorkbenchCodeClassificationKind.Text,
            CodeIntelligenceClassificationKind.Keyword => WorkbenchCodeClassificationKind.Keyword,
            CodeIntelligenceClassificationKind.ControlKeyword =>
                WorkbenchCodeClassificationKind.ControlKeyword,
            CodeIntelligenceClassificationKind.Comment => WorkbenchCodeClassificationKind.Comment,
            CodeIntelligenceClassificationKind.DocumentationComment =>
                WorkbenchCodeClassificationKind.DocumentationComment,
            CodeIntelligenceClassificationKind.String => WorkbenchCodeClassificationKind.String,
            CodeIntelligenceClassificationKind.Number => WorkbenchCodeClassificationKind.Number,
            CodeIntelligenceClassificationKind.Preprocessor =>
                WorkbenchCodeClassificationKind.Preprocessor,
            CodeIntelligenceClassificationKind.Namespace =>
                WorkbenchCodeClassificationKind.Namespace,
            CodeIntelligenceClassificationKind.Type => WorkbenchCodeClassificationKind.Type,
            CodeIntelligenceClassificationKind.Method => WorkbenchCodeClassificationKind.Method,
            CodeIntelligenceClassificationKind.Property => WorkbenchCodeClassificationKind.Property,
            CodeIntelligenceClassificationKind.Field => WorkbenchCodeClassificationKind.Field,
            CodeIntelligenceClassificationKind.Event => WorkbenchCodeClassificationKind.Event,
            CodeIntelligenceClassificationKind.Parameter =>
                WorkbenchCodeClassificationKind.Parameter,
            CodeIntelligenceClassificationKind.Local => WorkbenchCodeClassificationKind.Local,
            CodeIntelligenceClassificationKind.TypeParameter =>
                WorkbenchCodeClassificationKind.TypeParameter,
            CodeIntelligenceClassificationKind.Operator => WorkbenchCodeClassificationKind.Operator,
            CodeIntelligenceClassificationKind.Punctuation =>
                WorkbenchCodeClassificationKind.Punctuation,
            CodeIntelligenceClassificationKind.Identifier =>
                WorkbenchCodeClassificationKind.Identifier,
            CodeIntelligenceClassificationKind.ExcludedCode =>
                WorkbenchCodeClassificationKind.ExcludedCode,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeOccurrenceKind Map(CodeIntelligenceOccurrenceKind kind) =>
        kind switch
        {
            CodeIntelligenceOccurrenceKind.Definition => WorkbenchCodeOccurrenceKind.Definition,
            CodeIntelligenceOccurrenceKind.Read => WorkbenchCodeOccurrenceKind.Read,
            CodeIntelligenceOccurrenceKind.Write => WorkbenchCodeOccurrenceKind.Write,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeFoldingKind Map(CodeIntelligenceFoldingKind kind) => kind switch
    {
        CodeIntelligenceFoldingKind.Namespace => WorkbenchCodeFoldingKind.Namespace,
        CodeIntelligenceFoldingKind.Type => WorkbenchCodeFoldingKind.Type,
        CodeIntelligenceFoldingKind.Member => WorkbenchCodeFoldingKind.Member,
        CodeIntelligenceFoldingKind.Block => WorkbenchCodeFoldingKind.Block,
        CodeIntelligenceFoldingKind.Region => WorkbenchCodeFoldingKind.Region,
        CodeIntelligenceFoldingKind.Comment => WorkbenchCodeFoldingKind.Comment,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static WorkbenchCodeInlayHintKind Map(CodeIntelligenceInlayHintKind kind) =>
        kind switch
        {
            CodeIntelligenceInlayHintKind.ParameterName =>
                WorkbenchCodeInlayHintKind.ParameterName,
            CodeIntelligenceInlayHintKind.InferredType =>
                WorkbenchCodeInlayHintKind.InferredType,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeLensKind Map(CodeIntelligenceCodeLensKind kind) => kind switch
    {
        CodeIntelligenceCodeLensKind.References => WorkbenchCodeLensKind.References,
        CodeIntelligenceCodeLensKind.Implementations => WorkbenchCodeLensKind.Implementations,
        CodeIntelligenceCodeLensKind.Tests => WorkbenchCodeLensKind.Tests,
        CodeIntelligenceCodeLensKind.Run => WorkbenchCodeLensKind.Run,
        CodeIntelligenceCodeLensKind.Debug => WorkbenchCodeLensKind.Debug,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static WorkbenchCodeDestinationKind Map(CodeIntelligenceDestinationKind kind) =>
        kind switch
        {
            CodeIntelligenceDestinationKind.Source => WorkbenchCodeDestinationKind.Source,
            CodeIntelligenceDestinationKind.Generated => WorkbenchCodeDestinationKind.Generated,
            CodeIntelligenceDestinationKind.Metadata => WorkbenchCodeDestinationKind.Metadata,
            CodeIntelligenceDestinationKind.Unavailable => WorkbenchCodeDestinationKind.Unavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeVirtualDocumentKind Map(
        CodeIntelligenceVirtualDocumentKind kind) => kind switch
        {
            CodeIntelligenceVirtualDocumentKind.GeneratedSource =>
                WorkbenchCodeVirtualDocumentKind.GeneratedSource,
            CodeIntelligenceVirtualDocumentKind.MetadataSignature =>
                WorkbenchCodeVirtualDocumentKind.MetadataSignature,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static WorkbenchCodeVirtualDocumentView VirtualDocumentFailure(
        WorkbenchCodeVirtualDocumentRequest request,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
            request.Snapshot.SessionId, request.Snapshot.Path, request.Snapshot.BufferVersion,
            state, request.Id, Kind: null, Title: null, Text: null, SelectionRange: null,
            Origin: null, IsReadOnly: true, [issue]);

    private static WorkbenchCodeCompletionView CompletionFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot?.SessionId ?? new(string.Empty), snapshot?.Path ?? new(string.Empty),
        snapshot?.BufferVersion ?? new(0), state, null,
        new(snapshot?.Position ?? new(0, 0), snapshot?.Position ?? new(0, 0)), [], [issue]);

    private static WorkbenchCodeCompletionCommitView CommitFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot?.SessionId ?? new(string.Empty), snapshot?.Path ?? new(string.Empty),
        snapshot?.BufferVersion ?? new(0), state, [], null, [issue]);

    private static WorkbenchCodeQuickInfoView QuickInfoFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot?.SessionId ?? new(string.Empty), snapshot?.Path ?? new(string.Empty),
        snapshot?.BufferVersion ?? new(0), state, null, [], [issue]);

    private static WorkbenchCodeSignatureHelpView SignatureFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot?.SessionId ?? new(string.Empty), snapshot?.Path ?? new(string.Empty),
        snapshot?.BufferVersion ?? new(0), state, [], 0, 0, [issue]);

    private static WorkbenchCodeNavigationView NavigationFailure(
        WorkbenchCodeInteractiveSnapshot snapshot,
        WorkbenchCodeIssue issue,
        WorkbenchCodeResultState state = WorkbenchCodeResultState.Failed) => new(
        snapshot?.SessionId ?? new(string.Empty), snapshot?.Path ?? new(string.Empty),
        snapshot?.BufferVersion ?? new(0), state, [], [issue]);
}
